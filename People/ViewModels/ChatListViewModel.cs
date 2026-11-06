using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace People.ViewModels;

public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<Chat> Chats { get; } = new();
    public ObservableCollection<Contact> FilteredContacts { get; } = new();
    private readonly List<Contact> _allContacts = new();
    private readonly object _contactsLock = new();

    [ObservableProperty]
    public partial byte[]? QrCodeBytes { get; set; }

    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial bool ShowQrCode { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial int SyncProgress { get; set; }

    [ObservableProperty]
    public partial bool IsSyncIndeterminate { get; set; } = true;

    partial void OnSyncProgressChanged(int value)
    {
        IsSyncIndeterminate = value <= 0;
    }

    [ObservableProperty]
    public partial string SyncStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInitialSyncing { get; set; }

    [ObservableProperty]
    public partial int InitialSyncProgress { get; set; }

    [ObservableProperty]
    public partial bool IsInitialSyncIndeterminate { get; set; } = true;

    partial void OnInitialSyncProgressChanged(int value)
    {
        IsInitialSyncIndeterminate = value <= 0;
    }

    [ObservableProperty]
    public partial string InitialSyncText { get; set; } = "Downloading your recent messages...";

    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;
    private readonly IChatRepository? _chatRepository;
    private readonly HashSet<string> _failedPictureJids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _contactNamesByPhoneDigits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contactNamesById = new(StringComparer.OrdinalIgnoreCase);
    private int _isLoadingChats;
    private volatile bool _needsReloadChats;

    public ChatListViewModel(
        IMessagingProvider provider, 
        IDispatcherService dispatcherService,
        IChatRepository? chatRepository = null)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _chatRepository = chatRepository;
        _provider.ConnectionStateChanged += OnConnectionStateChanged;
        _provider.ChatUpdated += OnChatUpdated;
        _provider.MessageReceived += OnMessageReceived;

        // Synchronously preload cached chats so Chats is populated BEFORE page render
        PreloadCachedChats();

        _ = Task.Run(RebuildContactsAsync);

        if (_provider is IWhatsAppProvider waProvider)
        {
            waProvider.QrCodeReceived += OnQrCodeReceived;
            waProvider.SyncProgressChanged += OnSyncProgressChanged;
            waProvider.ChatMarkedRead += OnChatMarkedRead;
            _ = ConnectAndAuthenticateAsync(waProvider);
        }
        else
        {
            LoadMockData();
        }
    }

    private void PreloadCachedChats()
    {
        try
        {
            if (_provider is IWhatsAppProvider waProvider && waProvider.IsAuthenticated)
            {
                var cached = waProvider.GetCachedChats();
                if (cached.Count > 0)
                {
                    var valid = cached.Where(c => !string.IsNullOrWhiteSpace(c.Id)).ToList();
                    foreach (var c in valid)
                    {
                        c.Title = ResolveChatTitle(c);
                    }
                    var sorted = valid.OrderByDescending(c => c.LastMessageTime).ToList();

                    // Preload viewport batch immediately so ListView renders fully with stacking animation
                    var viewportBatch = sorted.Take(25).ToList();
                    foreach (var c in viewportBatch)
                    {
                        Chats.Add(c);
                    }

                    // Schedule remaining chats so initial launch and window presentation are never stalled
                    if (sorted.Count > 25)
                    {
                        var remaining = sorted.Skip(25).ToList();
                        _dispatcherService.Enqueue(() =>
                        {
                            foreach (var c in remaining)
                            {
                                Chats.Add(c);
                            }
                        });
                    }
                }
            }
        }
        catch { }
    }

    private async Task ConnectAndAuthenticateAsync(IWhatsAppProvider provider)
    {
        // Immediately load any cached chats so the list displays without delay
        _ = LoadChatsAsync();

        bool isAuth = await Task.Run(() => provider.IsAuthenticated);

        if (isAuth)
        {
            // Already authenticated: show chat list directly, show sync bar, and connect in background
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                ShowQrCode = false;
                HasError = false;
                ErrorMessage = string.Empty;
                IsSyncing = true;
                SyncProgress = 0;
                IsSyncIndeterminate = true;
                SyncStatusText = string.Empty;
            });

            try
            {
                await provider.ConnectAsync();
            }
            catch (Exception ex)
            {
                _dispatcherService.Enqueue(() =>
                {
                    IsSyncing = false;
                    HasError = true;
                    ErrorMessage = ex.Message;
                });
            }

            // Immediately load all live/cached chats now that native client is connected
            await LoadChatsAsync();
            _dispatcherService.Enqueue(() =>
            {
                IsSyncing = false;
            });
            return;
        }

        // Fresh login: show connecting then QR code
        try
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = true;
                ShowQrCode = false;
                HasError = false;
                ErrorMessage = string.Empty;
                QrCodeBytes = null;
            });

            await provider.ConnectAsync();
            await provider.AuthenticateAsync();
        }
        catch (Exception ex)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                HasError = true;
                ErrorMessage = ex.Message;
            });
        }
    }

    public void PrepareForReauthentication()
    {
        _dispatcherService.Enqueue(() =>
        {
            IsConnecting = true;
            ShowQrCode = false;
            HasError = false;
            ErrorMessage = string.Empty;
            QrCodeBytes = null;
            IsSyncing = false;
            IsInitialSyncing = false;
            Chats.Clear();
        });
    }

    [RelayCommand]
    private async Task RetryConnectAsync()
    {
        if (_provider is IWhatsAppProvider waProvider)
        {
            await waProvider.DisconnectAsync();
            await ConnectAndAuthenticateAsync(waProvider);
        }
    }

    [RelayCommand]
    private async Task UseTestingModeAsync()
    {
        var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        settingsService.SetValue("WhatsAppTestingMode", true);
        if (_provider is IWhatsAppProvider waProvider)
        {
            await waProvider.DisconnectAsync();
            await ConnectAndAuthenticateAsync(waProvider);
        }
    }

    private void OnSyncProgressChanged(object? sender, int progress)
    {
        _dispatcherService.Enqueue(async () =>
        {
            SyncProgress = progress;
            if (IsInitialSyncing)
            {
                if (progress >= 100)
                {
                    InitialSyncProgress = 100;
                    InitialSyncText = "Finishing sync...";
                    IsInitialSyncing = false;
                    IsSyncing = false;
                    lock (_failedPictureJids)
                    {
                        _failedPictureJids.Clear();
                    }
                    await LoadChatsAsync();
                }
                else
                {
                    InitialSyncProgress = Math.Max(0, progress);
                    InitialSyncText = "Downloading your recent messages...";
                }
            }
            else
            {
                if (progress >= 100 || progress < 0)
                {
                    IsSyncing = false;
                    SyncStatusText = string.Empty;
                    await LoadChatsAsync();
                }
                else if (Chats.Count == 0)
                {
                    IsInitialSyncing = true;
                    InitialSyncProgress = Math.Max(0, progress);
                    InitialSyncText = "Downloading your recent messages...";
                }
                else
                {
                    IsSyncing = true;
                    SyncProgress = Math.Max(0, progress);
                    IsSyncIndeterminate = progress <= 0;
                }
            }
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        lock (_failedPictureJids)
        {
            _failedPictureJids.Clear();
        }
        _ = LoadChatsAsync();
    }

    private void OnChatMarkedRead(object? sender, string chatId)
    {
        _dispatcherService.Enqueue(() =>
        {
            var cleanTarget = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;
            var targetBare = cleanTarget.Split('@')[0].Split(':')[0];
            var existing = Chats.FirstOrDefault(c =>
            {
                var cleanC = c.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? c.Id[3..] : c.Id;
                if (string.Equals(cleanC, cleanTarget, StringComparison.OrdinalIgnoreCase)) return true;
                var u = cleanC.Split('@')[0].Split(':')[0];
                return !string.IsNullOrEmpty(u) && string.Equals(u, targetBare, StringComparison.OrdinalIgnoreCase);
            });

            if (existing != null && existing.UnreadCount > 0)
            {
                existing.UnreadCount = 0;
            }
        });
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        _dispatcherService.Enqueue(() =>
        {
            var msg = e.Message;
            if (msg.Content == null || (msg.Content.Type == MessageContentType.Text && string.IsNullOrWhiteSpace(msg.Content.Text)))
            {
                return;
            }

            var chat = Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, msg.ChatId));
            if (chat != null)
            {
                if (chat.LastMessage != null && chat.LastMessage.Id == msg.Id)
                {
                    chat.LastMessage.Status = msg.Status;
                }
                else
                {
                    chat.LastMessage = msg;
                    chat.LastMessageTime = msg.Timestamp;
                    SortChatsInPlace();
                }
            }
            else
            {
                _ = LoadChatsAsync();
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.State == ConnectionState.Connected)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                if (ShowQrCode)
                {
                    ShowQrCode = false;
                    IsInitialSyncing = true;
                    InitialSyncProgress = 0;
                    InitialSyncText = "Downloading your recent messages...";
                }
                HasError = false;
            });
            lock (_failedPictureJids)
            {
                _failedPictureJids.Clear();
            }
            _ = LoadChatsAsync();
        }
        else if (e.State == ConnectionState.Error)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                ShowQrCode = false;
                IsSyncing = false;
                IsInitialSyncing = false;
                HasError = true;
                if (!string.IsNullOrEmpty(e.ErrorMessage))
                {
                    ErrorMessage = e.ErrorMessage;
                }
                else if (string.IsNullOrEmpty(ErrorMessage))
                {
                    ErrorMessage = "Unable to connect to WhatsApp backend. Check your network or use Testing Mode.";
                }
            });
        }
        else if (e.State == ConnectionState.Disconnected)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                IsSyncing = false;
                IsInitialSyncing = false;
            });
        }
    }

    public async Task LoadChatsAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _isLoadingChats, 1) != 0)
        {
            _needsReloadChats = true;
            return;
        }

        try
        {
            do
            {
                _needsReloadChats = false;
                await ExecuteLoadChatsAsync();
            } while (_needsReloadChats);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isLoadingChats, 0);
        }
    }

    private async Task ExecuteLoadChatsAsync()
    {
        try
        {
            // 1. Show cached chats immediately from SQLite
            if (Chats.Count == 0 && _chatRepository != null)
            {
                try
                {
                    var cached = await _chatRepository.GetAllAsync(0, 100);
                    var validCached = cached.Where(c => !string.IsNullOrWhiteSpace(c.Id) && (c.LastMessage != null || !string.IsNullOrWhiteSpace(c.Title))).ToList();

                    if (validCached.Count > 0 && Chats.Count == 0)
                    {
                        foreach (var c in validCached)
                        {
                            c.Title = ResolveChatTitle(c);
                        }
                        var sortedCached = validCached.OrderByDescending(c => c.LastMessageTime).ToList();

                        _dispatcherService.Enqueue(() =>
                        {
                            if (Chats.Count == 0)
                            {
                                foreach (var c in sortedCached)
                                {
                                    Chats.Add(c);
                                }
                            }
                        });
                    }
                }
                catch { }
            }

            // Trigger contact rebuild asynchronously in background; never block chat display
            if (_allContacts.Count == 0)
            {
                _ = Task.Run(RebuildContactsAsync);
            }

            var chats = await _provider.GetChatsAsync();
            var validChats = chats.Where(c => !string.IsNullOrWhiteSpace(c.Id) && c.LastMessage != null && (!string.IsNullOrWhiteSpace(c.LastMessage.Content?.Text) || c.LastMessage.Content?.Type != MessageContentType.Text)).ToList();

            // Resolve titles on background thread BEFORE dispatching to UI
            foreach (var incoming in validChats)
            {
                incoming.Title = ResolveChatTitle(incoming);
            }
            var sortedIncoming = validChats.OrderByDescending(c => c.LastMessageTime).ToList();

            _dispatcherService.Enqueue(() =>
            {
                if (Chats.Count == 0)
                {
                    foreach (var incoming in sortedIncoming)
                    {
                        Chats.Add(incoming);
                    }
                }
                else
                {
                    var newChatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in validChats)
                    {
                        if (!string.IsNullOrEmpty(c.Id))
                        {
                            newChatIds.Add(c.Id);
                            var clean = c.Id.StartsWith("wa_") ? c.Id.Substring(3) : c.Id;
                            newChatIds.Add(clean);
                            newChatIds.Add("wa_" + clean);
                        }
                    }

                    // Remove chats that no longer exist
                    if (validChats.Count > 0)
                    {
                        for (int i = Chats.Count - 1; i >= 0; i--)
                        {
                            var cId = Chats[i].Id;
                            var clean = cId.StartsWith("wa_") ? cId.Substring(3) : cId;
                            if (!newChatIds.Contains(cId) && !newChatIds.Contains(clean) && !newChatIds.Contains("wa_" + clean))
                            {
                                Chats.RemoveAt(i);
                            }
                        }
                    }

                    // Pre-index existing chats safely for O(1) lookups
                    var existingMap = new Dictionary<string, Chat>(StringComparer.OrdinalIgnoreCase);
                    void IndexChat(Chat c)
                    {
                        if (string.IsNullOrEmpty(c.Id)) return;
                        existingMap[c.Id] = c;
                        var clean = c.Id.StartsWith("wa_") ? c.Id.Substring(3) : c.Id;
                        existingMap[clean] = c;
                        existingMap["wa_" + clean] = c;
                        var noDev = clean.Split(':')[0];
                        if (clean.Contains('@'))
                        {
                            var user = noDev.Split('@')[0];
                            var server = clean.Split('@')[1];
                            existingMap[$"{user}@{server}"] = c;
                            existingMap[$"wa_{user}@{server}"] = c;
                            var digits = new string(user.Where(char.IsDigit).ToArray());
                            if (digits.Length >= 7)
                            {
                                existingMap[digits] = c;
                                existingMap[$"{digits}@s.whatsapp.net"] = c;
                                existingMap[$"wa_{digits}@s.whatsapp.net"] = c;
                            }
                        }
                    }

                    foreach (var c in Chats)
                    {
                        IndexChat(c);
                    }

                    Chat? FindExisting(string id)
                    {
                        if (string.IsNullOrEmpty(id)) return null;
                        if (existingMap.TryGetValue(id, out var found)) return found;
                        var clean = id.StartsWith("wa_") ? id.Substring(3) : id;
                        if (existingMap.TryGetValue(clean, out found)) return found;
                        if (existingMap.TryGetValue("wa_" + clean, out found)) return found;
                        var noDev = clean.Split(':')[0];
                        if (clean.Contains('@'))
                        {
                            var user = noDev.Split('@')[0];
                            var server = clean.Split('@')[1];
                            if (existingMap.TryGetValue($"{user}@{server}", out found)) return found;
                            var digits = new string(user.Where(char.IsDigit).ToArray());
                            if (digits.Length >= 7 && existingMap.TryGetValue(digits, out found)) return found;
                        }
                        return null;
                    }

                    // Update existing or add new
                    foreach (var incoming in validChats)
                    {
                        var existing = FindExisting(incoming.Id);
                        if (existing != null)
                        {
                            if (existing.Title != incoming.Title && incoming.Title != "WhatsApp Contact")
                            {
                                existing.Title = incoming.Title;
                            }
                            if (existing.LastMessageTime != incoming.LastMessageTime)
                            {
                                existing.LastMessageTime = incoming.LastMessageTime;
                            }
                            if (incoming.LastMessage != null)
                            {
                                existing.LastMessage = incoming.LastMessage;
                            }
                            existing.UnreadCount = incoming.UnreadCount;
                            if (!string.IsNullOrEmpty(incoming.PictureUrl) && existing.PictureUrl != incoming.PictureUrl)
                            {
                                existing.PictureUrl = incoming.PictureUrl;
                            }
                        }
                        else
                        {
                            Chats.Add(incoming);
                            IndexChat(incoming);
                        }
                    }

                    SortChatsInPlace();
                }
            });
        }
        catch (Exception)
        {
            _dispatcherService.Enqueue(() => LoadMockData());
        }
    }

    public void SortChatsInPlace()
    {
        var targetOrder = Chats.OrderByDescending(c => c.LastMessageTime).ToList();
        bool isAlreadySorted = true;
        for (int i = 0; i < targetOrder.Count; i++)
        {
            if (!ReferenceEquals(Chats[i], targetOrder[i]))
            {
                isAlreadySorted = false;
                break;
            }
        }
        if (isAlreadySorted) return;

        for (int i = 0; i < targetOrder.Count; i++)
        {
            var item = targetOrder[i];
            int currentIdx = Chats.IndexOf(item);
            if (currentIdx != i && currentIdx >= 0)
            {
                Chats.Move(currentIdx, i);
            }
        }
    }

    private string ResolveChatTitle(Chat chat)
    {
        if (chat.IsGroup) return chat.Title;

        if (!string.IsNullOrEmpty(chat.Id))
        {
            lock (_contactNamesById)
            {
                if (_contactNamesById.TryGetValue(chat.Id, out var name))
                {
                    return name;
                }
            }
        }

        var candidateDigits = new List<string>();
        if (!string.IsNullOrEmpty(chat.Title) && (chat.Title.StartsWith('+') || chat.Title.All(char.IsDigit)))
        {
            var tDigits = new string(chat.Title.Where(char.IsDigit).ToArray());
            if (tDigits.Length >= 7) candidateDigits.Add(tDigits);
        }

        string? idUserDigits = null;
        if (!string.IsNullOrEmpty(chat.Id) && !chat.Id.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
        {
            var userPart = chat.Id.Contains('@') ? chat.Id.Split('@')[0] : chat.Id;
            var cleanPart = userPart.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? userPart[3..] : userPart;
            var digitsOnly = cleanPart.Split(':')[0];
            var idDigits = new string(digitsOnly.Where(char.IsDigit).ToArray());
            if (idDigits.Length >= 7)
            {
                candidateDigits.Add(idDigits);
                idUserDigits = idDigits;
            }
        }

        lock (_contactNamesByPhoneDigits)
        {
            foreach (var num in candidateDigits)
            {
                if (_contactNamesByPhoneDigits.TryGetValue(num, out var name))
                {
                    return name;
                }
                if (num.Length > 10 && _contactNamesByPhoneDigits.TryGetValue(num.Substring(num.Length - 10), out var n10))
                {
                    return n10;
                }
            }
        }

        // If chat already has a valid saved contact name (not a phone number, not "WhatsApp Contact"), preserve it!
        if (!string.IsNullOrWhiteSpace(chat.Title) &&
            !chat.Title.StartsWith('+') &&
            chat.Title != "WhatsApp Contact" &&
            !chat.Title.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')'))
        {
            return chat.Title;
        }

        // Unsaved 1-on-1 contact: format international phone number matching WhatsApp
        string? phone = null;
        var waProvider = _provider as IWhatsAppProvider;
        if (waProvider != null && !string.IsNullOrEmpty(chat.Id))
        {
            phone = waProvider.GetPhoneNumber(chat.Id);
        }
        if (string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(idUserDigits))
        {
            phone = $"+{idUserDigits}";
        }
        if (string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(chat.Title))
        {
            var titleDigits = new string(chat.Title.Where(char.IsDigit).ToArray());
            if (titleDigits.Length >= 7)
            {
                phone = chat.Title.StartsWith('+') ? chat.Title : $"+{titleDigits}";
            }
        }

        if (!string.IsNullOrEmpty(phone))
        {
            return ConversationViewModel.FormatPhoneNumber(phone);
        }

        return chat.Title;
    }

    public async Task RebuildContactsAsync()
    {
        var contactsMap = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);

        // 1. Load from IContactRepository
        try
        {
            var repo = App.Current.Services.GetService<People.Core.Interfaces.Repositories.IContactRepository>();
            if (repo != null)
            {
                var dbContacts = await repo.GetAllAsync();
                foreach (var c in dbContacts)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.PhoneNumber))
                    {
                        var key = !string.IsNullOrWhiteSpace(c.PhoneNumber) ? c.PhoneNumber : c.Id;
                        contactsMap[key] = c;
                    }
                }
            }
        }
        catch
        {
            // Ignore if DB not ready
        }

        // 2. Load from Windows ContactStore (Windows Contacts) with timeout to prevent cold-launch blocking
        try
        {
            var storeTask = Windows.ApplicationModel.Contacts.ContactManager.RequestStoreAsync(
                Windows.ApplicationModel.Contacts.ContactStoreAccessType.AllContactsReadOnly).AsTask();
            if (await Task.WhenAny(storeTask, Task.Delay(1500)) == storeTask)
            {
                var contactStore = await storeTask;
                if (contactStore != null)
                {
                    var winContacts = await contactStore.FindContactsAsync();
                    foreach (var wc in winContacts)
                    {
                        var name = wc.DisplayName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = $"{wc.FirstName} {wc.LastName}".Trim();
                        }
                        var phone = wc.Phones?.FirstOrDefault()?.Number;
                        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(phone))
                        {
                            var cleanPhone = phone != null ? new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray()) : null;
                            var contactId = !string.IsNullOrEmpty(cleanPhone)
                                ? $"{cleanPhone.TrimStart('+')}@s.whatsapp.net"
                                : wc.Id ?? Guid.NewGuid().ToString();

                            var c = new Contact
                            {
                                Id = contactId,
                                Name = !string.IsNullOrWhiteSpace(name) ? name : (phone ?? "Unknown"),
                                PhoneNumber = phone,
                                PlatformId = "WhatsApp"
                            };

                            var key = !string.IsNullOrWhiteSpace(cleanPhone) ? cleanPhone : c.Id;
                            if (!contactsMap.ContainsKey(key))
                            {
                                contactsMap[key] = c;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Windows Contacts store access may be restricted
        }

        // 2.5. Load synced contacts from WhatsApp Provider
        if (_provider is IWhatsAppProvider waProvider)
        {
            try
            {
                var waContacts = await waProvider.GetContactsAsync();
                foreach (var c in waContacts)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && c.Name != "WhatsApp Contact")
                    {
                        var key = !string.IsNullOrWhiteSpace(c.PhoneNumber) ? c.PhoneNumber : c.Id;
                        contactsMap[key] = c;
                    }
                }
            }
            catch { }
        }

        // Note: We deliberately do NOT extract unsaved phone numbers from chats here,
        // so the new chat flyout only contains saved contacts from address book & WhatsApp.

        var byDigits = new Dictionary<string, string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in contactsMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                if (!string.IsNullOrWhiteSpace(c.Id))
                {
                    byId[c.Id] = c.Name;
                }
                if (!string.IsNullOrWhiteSpace(c.PhoneNumber))
                {
                    var digits = new string(c.PhoneNumber.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 7)
                    {
                        byDigits[digits] = c.Name;
                        if (digits.Length > 10)
                        {
                            byDigits[digits.Substring(digits.Length - 10)] = c.Name;
                        }
                    }
                }
            }
        }

        lock (_contactNamesById)
        {
            _contactNamesById.Clear();
            foreach (var kvp in byId) _contactNamesById[kvp.Key] = kvp.Value;
        }
        lock (_contactNamesByPhoneDigits)
        {
            _contactNamesByPhoneDigits.Clear();
            foreach (var kvp in byDigits) _contactNamesByPhoneDigits[kvp.Key] = kvp.Value;
        }

        var sorted = contactsMap.Values
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
            .OrderBy(c => c.Name ?? string.Empty)
            .ToList();

        lock (_contactsLock)
        {
            _allContacts.Clear();
            _allContacts.AddRange(sorted);
        }
        _dispatcherService.Enqueue(() =>
        {
            foreach (var chat in Chats)
            {
                var resolved = ResolveChatTitle(chat);
                if (!string.IsNullOrEmpty(resolved) && chat.Title != resolved)
                {
                    chat.Title = resolved;
                }
            }
            if (FilteredContacts.Count > 0)
            {
                FilterContacts(string.Empty);
            }
        });
    }

    public void FilterContacts(string query)
    {
        FilteredContacts.Clear();

        var trimmed = query?.Trim() ?? string.Empty;

        // If user typed numbers, offer starting direct chat with that number
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length >= 5)
        {
            var formattedNumber = trimmed.StartsWith("+") ? $"+{digits}" : $"+{digits}";
            FilteredContacts.Add(new Contact
            {
                Id = $"{digits}@s.whatsapp.net",
                Name = $"Message {formattedNumber}",
                PlatformId = "WhatsApp",
                PhoneNumber = formattedNumber
            });
        }

        List<Contact> contactsSnapshot;
        lock (_contactsLock)
        {
            contactsSnapshot = _allContacts.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)).ToList();
        }

        foreach (var c in contactsSnapshot)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Id)) continue;

            var name = c.Name ?? string.Empty;
            var phone = c.PhoneNumber ?? string.Empty;

            bool matches = string.IsNullOrEmpty(trimmed) ||
                name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(phone) && phone.Contains(trimmed, StringComparison.OrdinalIgnoreCase));

            if (matches)
            {
                if (!FilteredContacts.Any(existing => existing != null && existing.Id == c.Id))
                {
                    FilteredContacts.Add(c);
                }
            }
        }
    }

    public Chat GetOrCreateChatForContact(Contact contact)
    {
        var existing = Chats.FirstOrDefault(c => c.Id == contact.Id);
        if (existing != null)
        {
            return existing;
        }

        var title = contact.Name.StartsWith("Message +") ? contact.PhoneNumber ?? contact.Name : contact.Name;

        var newChat = new Chat
        {
            Id = contact.Id,
            PlatformId = "WhatsApp",
            Title = title,
            IsGroup = false,
            LastMessageTime = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        return newChat;
    }

    private void OnQrCodeReceived(object? sender, string qrString)
    {
        _dispatcherService.Enqueue(() =>
        {
            IsConnecting = false;
            ShowQrCode = true;
            HasError = false;
            QrCodeBytes = GenerateQrCodeBytes(qrString);
        });
    }

    private byte[] GenerateQrCodeBytes(string text)
    {
        using var qrGenerator = new QRCoder.QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new QRCoder.PngByteQRCode(qrData);
        return qrCode.GetGraphic(5);
    }

    private void LoadMockData()
    {
        Chats.Add(new Chat
        {
            Id = "1",
            Title = "John Doe",
            LastMessage = new Message { Content = new MessageContent { Text = "Hey, how are you?" }, Timestamp = DateTimeOffset.Now.AddMinutes(-5) },
            UnreadCount = 2
        });
        Chats.Add(new Chat
        {
            Id = "2",
            Title = "Family Group",
            IsGroup = true,
            LastMessage = new Message { Content = new MessageContent { Text = "Dinner at 7!" }, Timestamp = DateTimeOffset.Now.AddHours(-1) },
            UnreadCount = 0
        });
        _ = RebuildContactsAsync();
    }
}
