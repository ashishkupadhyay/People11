using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
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

    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;

    public ChatListViewModel(IMessagingProvider provider, IDispatcherService dispatcherService)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _provider.ConnectionStateChanged += OnConnectionStateChanged;
        _provider.ChatUpdated += OnChatUpdated;

        _ = RebuildContactsAsync();

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
            await LoadChatsAsync();

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
        _dispatcherService.Enqueue(() =>
        {
            SyncProgress = progress;
            if (progress >= 100 || progress <= 0)
            {
                IsSyncing = false;
                SyncStatusText = string.Empty;
            }
            else
            {
                IsSyncing = true;
                SyncStatusText = $"Syncing chats ({progress}%)...";
            }
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        _dispatcherService.Enqueue(() =>
        {
            _ = LoadChatsAsync();
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.State == ConnectionState.Connected)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                ShowQrCode = false;
                HasError = false;
                IsSyncing = false;
                SyncStatusText = string.Empty;
            });
            _ = LoadChatsAsync();
        }
        else if (e.State == ConnectionState.Error)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                IsSyncing = false;
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
            });
        }
    }

    public async Task LoadChatsAsync()
    {
        try
        {
            if (_allContacts.Count == 0)
            {
                await RebuildContactsAsync();
            }

            var chats = await _provider.GetChatsAsync();
            _dispatcherService.Enqueue(async () =>
            {
                Chats.Clear();
                foreach (var chat in chats)
                {
                    chat.Title = ResolveChatTitle(chat);
                    Chats.Add(chat);
                }

                // Refresh contacts list for New Chat flyout
                await RebuildContactsAsync();
            });
        }
        catch (Exception)
        {
            _dispatcherService.Enqueue(() => LoadMockData());
        }
    }

    private string ResolveChatTitle(Chat chat)
    {
        if (chat.IsGroup) return chat.Title;

        var candidateNumbers = new List<string>();
        if (!string.IsNullOrEmpty(chat.Title) && (chat.Title.StartsWith('+') || chat.Title.All(char.IsDigit)))
        {
            var tDigits = new string(chat.Title.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(tDigits))
            {
                candidateNumbers.Add(tDigits);
            }
        }

        if (!string.IsNullOrEmpty(chat.Id) && !chat.Id.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
        {
            var userPart = chat.Id.Contains('@') ? chat.Id.Split('@')[0] : chat.Id;
            var idDigits = new string(userPart.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(idDigits))
            {
                candidateNumbers.Add(idDigits);
            }
        }

        foreach (var c in _allContacts)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;

            if (!string.IsNullOrWhiteSpace(c.PhoneNumber))
            {
                var cDigits = new string(c.PhoneNumber.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(cDigits))
                {
                    foreach (var num in candidateNumbers)
                    {
                        if (cDigits == num ||
                            (cDigits.Length >= 10 && num.Length >= 10 &&
                             (cDigits.EndsWith(num.Substring(num.Length - 10)) || num.EndsWith(cDigits.Substring(cDigits.Length - 10)))))
                        {
                            return c.Name;
                        }
                    }
                }
            }

            if (c.Id.Equals(chat.Id, StringComparison.OrdinalIgnoreCase))
            {
                return c.Name;
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

        // 2. Load from Windows ContactStore (Windows Contacts)
        try
        {
            var contactStore = await Windows.ApplicationModel.Contacts.ContactManager.RequestStoreAsync(
                Windows.ApplicationModel.Contacts.ContactStoreAccessType.AllContactsReadOnly);
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
        catch
        {
            // Windows Contacts store access may be restricted
        }

        // 3. Extract 1-on-1 contacts from chats
        foreach (var chat in Chats)
        {
            if (!chat.IsGroup && !string.IsNullOrWhiteSpace(chat.Title) && chat.Title != "WhatsApp Contact")
            {
                string? phone = null;
                if (chat.Title.StartsWith('+'))
                {
                    phone = chat.Title;
                }
                else if (!chat.Id.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
                {
                    var userPart = chat.Id.Contains('@') ? chat.Id.Split('@')[0] : chat.Id;
                    if (userPart.All(char.IsDigit) && userPart.Length >= 7)
                    {
                        phone = $"+{userPart}";
                    }
                }

                var key = phone ?? chat.Id;
                if (!contactsMap.ContainsKey(key))
                {
                    contactsMap[key] = new Contact
                    {
                        Id = chat.Id,
                        Name = chat.Title,
                        PlatformId = chat.PlatformId,
                        PhoneNumber = phone
                    };
                }
            }
        }

        _allContacts.Clear();
        _allContacts.AddRange(contactsMap.Values.OrderBy(c => c.Name));
        FilterContacts(string.Empty);
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

        Chats.Insert(0, newChat);
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
