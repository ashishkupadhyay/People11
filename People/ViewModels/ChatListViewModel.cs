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
                IsSyncing = true;
                SyncProgress = 0;
                SyncStatusText = "Connecting and syncing WhatsApp...";
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
            if (progress >= 100)
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
            var chats = await _provider.GetChatsAsync();
            _dispatcherService.Enqueue(() =>
            {
                Chats.Clear();
                foreach (var chat in chats)
                {
                    Chats.Add(chat);
                }

                // Refresh contacts list for New Chat flyout
                RebuildContacts();
            });
        }
        catch (Exception)
        {
            _dispatcherService.Enqueue(() => LoadMockData());
        }
    }

    private void RebuildContacts()
    {
        _allContacts.Clear();

        // Extract contacts from chats
        foreach (var chat in Chats)
        {
            if (!chat.IsGroup && !string.IsNullOrWhiteSpace(chat.Title))
            {
                _allContacts.Add(new Contact
                {
                    Id = chat.Id,
                    Name = chat.Title,
                    PlatformId = chat.PlatformId,
                    PhoneNumber = chat.Id.Contains("@") ? chat.Id.Split('@')[0] : chat.Id
                });
            }
        }

        FilterContacts(string.Empty);
    }

    public void FilterContacts(string query)
    {
        FilteredContacts.Clear();

        var trimmed = query?.Trim() ?? string.Empty;

        // If user typed numbers, offer starting direct chat
        if (!string.IsNullOrEmpty(trimmed) && trimmed.All(c => char.IsDigit(c) || c == '+' || c == '-' || c == ' '))
        {
            var cleanDigits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (cleanDigits.Length >= 5)
            {
                FilteredContacts.Add(new Contact
                {
                    Id = $"{cleanDigits}@s.whatsapp.net",
                    Name = $"+{cleanDigits}",
                    PlatformId = "WhatsApp",
                    PhoneNumber = $"+{cleanDigits}"
                });
            }
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

        var newChat = new Chat
        {
            Id = contact.Id,
            PlatformId = "WhatsApp",
            Title = contact.Name,
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
        RebuildContacts();
    }
}
