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
    public partial string SyncStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInitialSyncing { get; set; }

    [ObservableProperty]
    public partial int InitialSyncProgress { get; set; }

    [ObservableProperty]
    public partial string InitialSyncText { get; set; } = "Downloading your recent messages...";

    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;
    private readonly IChatRepository? _chatRepository;
    private readonly HashSet<string> _failedPictureJids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _contactNamesByPhoneDigits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contactNamesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _reloadLock = new();
    private CancellationTokenSource? _chatUpdateDebounceCts;
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

        _ = Task.Run(RebuildContactsAsync);

        if (_provider is IWhatsAppProvider waProvider)
        {
            waProvider.QrCodeReceived += OnQrCodeReceived;
            waProvider.SyncProgressChanged += OnSyncProgressChanged;
            _ = ConnectAndAuthenticateAsync(waProvider);
        }
        else
        {
            LoadMockData();
        }
    }

    private async Task ConnectAndAuthenticateAsync(IWhatsAppProvider provider)
    {
        bool isAuth = provider.IsAuthenticated;

        if (isAuth)
        {
            // Already authenticated: show chat list directly and connect in background
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                ShowQrCode = false;
                HasError = false;
                ErrorMessage = string.Empty;
                IsSyncing = false;
                SyncProgress = 0;
                SyncStatusText = string.Empty;
            });

            // Immediately load any cached chats
            _ = LoadChatsAsync();

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

            await LoadChatsAsync();
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
                    await LoadChatsAsync();
                }
                else
                {
                    InitialSyncProgress = Math.Max(0, progress);
                    InitialSyncText = $"Downloading your recent messages ({InitialSyncProgress}%)...";
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
                    InitialSyncText = $"Downloading your recent messages ({InitialSyncProgress}%)...";
                }
            }
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        lock (_reloadLock)
        {
            _chatUpdateDebounceCts?.Cancel();
            _chatUpdateDebounceCts = new CancellationTokenSource();
            var token = _chatUpdateDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (!token.IsCancellationRequested)
                    {
                        await LoadChatsAsync();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        _dispatcherService.Enqueue(() =>
        {
            var msg = e.Message;
            var chat = Chats.FirstOrDefault(c => c.Id.Equals(msg.ChatId, StringComparison.OrdinalIgnoreCase));
            if (chat != null)
            {
                chat.LastMessage = msg;
                chat.LastMessageTime = msg.Timestamp;
                SortChatsInPlace();
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
                    InitialSyncText = "Downloading your recent messages (0%)...";
                }
                HasError = false;
            });
            _ = LoadChatsAsync();
        }
        else if (e.State == ConnectionState.Error)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                IsSyncing = false;
                IsInitialSyncing = false;
                HasError = true;
                if (string.IsNullOrEmpty(ErrorMessage))
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
                    // Filter out phantom empty chat stubs: must have a non-empty last message
                    var validCached = cached.Where(c => c.LastMessage != null && !string.IsNullOrWhiteSpace(c.LastMessage.Content?.Text)).ToList();

                    // Prune phantom stubs in the background
                    var phantoms = cached.Where(c => c.LastMessage == null || string.IsNullOrWhiteSpace(c.LastMessage.Content?.Text)).ToList();
                    if (phantoms.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (var p in phantoms)
                            {
                                try { await _chatRepository.DeleteAsync(p.Id); } catch { }
                            }
                        });
                    }

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
            var validChats = chats.Where(c => c.LastMessage != null && !string.IsNullOrWhiteSpace(c.LastMessage.Content?.Text)).ToList();

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
                    var newChatIds = new HashSet<string>(validChats.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

                    // Remove chats that no longer exist
                    if (validChats.Count > 0)
                    {
                        for (int i = Chats.Count - 1; i >= 0; i--)
                        {
                            if (!newChatIds.Contains(Chats[i].Id))
                            {
                                Chats.RemoveAt(i);
                            }
                        }
                    }

                    // Update existing or add new
                    foreach (var incoming in validChats)
                    {
                        var existing = Chats.FirstOrDefault(c => c.Id.Equals(incoming.Id, StringComparison.OrdinalIgnoreCase));
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
                        }
                    }

                    SortChatsInPlace();
                }
            });

            // Asynchronously resolve missing profile picture URLs in the background (bounded to top 25 most active chats)
            _ = Task.Run(async () =>
            {
                if (_provider is IWhatsAppProvider waProvider)
                {
                    var chatsNeedingPics = validChats
                        .Where(c => string.IsNullOrEmpty(c.PictureUrl) && !_failedPictureJids.Contains(c.Id))
                        .Take(25)
                        .ToList();

                    if (chatsNeedingPics.Count == 0) return;

                    var updatedWithPics = new System.Collections.Concurrent.ConcurrentBag<Chat>();

                    await Parallel.ForEachAsync(chatsNeedingPics, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (chat, ct) =>
                    {
                        try
                        {
                            var picUrl = await waProvider.GetProfilePictureUrlAsync(chat.Id, ct);
                            if (!string.IsNullOrEmpty(picUrl))
                            {
                                _dispatcherService.Enqueue(() =>
                                {
                                    var target = Chats.FirstOrDefault(c => c.Id.Equals(chat.Id, StringComparison.OrdinalIgnoreCase));
                                    if (target != null)
                                    {
                                        target.PictureUrl = picUrl;
                                    }
                                });

                                chat.PictureUrl = picUrl;
                                updatedWithPics.Add(chat);
                            }
                            else
                            {
                                lock (_failedPictureJids)
                                {
                                    _failedPictureJids.Add(chat.Id);
                                }
                            }
                        }
                        catch
                        {
                            lock (_failedPictureJids)
                            {
                                _failedPictureJids.Add(chat.Id);
                            }
                        }
                    });

                    // Batch persist profile pictures to SQLite in a single transaction
                    if (_chatRepository != null && !updatedWithPics.IsEmpty)
                    {
                        try { await _chatRepository.SaveManyAsync(updatedWithPics.ToList()); } catch { }
                    }
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

        if (!string.IsNullOrEmpty(chat.Id) && !chat.Id.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
        {
            var userPart = chat.Id.Contains('@') ? chat.Id.Split('@')[0] : chat.Id;
            var idDigits = new string(userPart.Where(char.IsDigit).ToArray());
            if (idDigits.Length >= 7) candidateDigits.Add(idDigits);
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

        _allContacts.Clear();
        _allContacts.AddRange(contactsMap.Values.OrderBy(c => c.Name));
        _dispatcherService.Enqueue(() => FilterContacts(string.Empty));
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

        foreach (var c in _allContacts)
        {
            if (string.IsNullOrEmpty(trimmed) ||
                c.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (c.PhoneNumber != null && c.PhoneNumber.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                if (!FilteredContacts.Any(existing => existing.Id == c.Id))
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
