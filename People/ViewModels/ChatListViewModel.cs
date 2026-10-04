using CommunityToolkit.Mvvm.ComponentModel;
using People.Core.Models;
using System.Collections.ObjectModel;
using People.Core.Interfaces;
using System.Threading.Tasks;
namespace People.ViewModels;

public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<Chat> Chats { get; } = new();

    [ObservableProperty]
    public partial byte[]? QrCodeBytes { get; set; }

    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial bool ShowQrCode { get; set; }

    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;

    public ChatListViewModel(IMessagingProvider provider, IDispatcherService dispatcherService)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _provider.ConnectionStateChanged += OnConnectionStateChanged;
        
        if (_provider is IWhatsAppProvider waProvider)
        {
            waProvider.QrCodeReceived += OnQrCodeReceived;
            _ = ConnectAndAuthenticateAsync(waProvider);
        }

        LoadMockData();
    }

    private async Task ConnectAndAuthenticateAsync(IWhatsAppProvider provider)
    {
        try
        {
            _dispatcherService.Enqueue(() => IsConnecting = true);
            await provider.ConnectAsync();
            await provider.AuthenticateAsync();
        }
        catch (Exception)
        {
            // Ignore or log. The provider will have emitted a ConnectionState.Error anyway.
            _dispatcherService.Enqueue(() => IsConnecting = false);
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        // Update connection UI state
        if (e.State == ConnectionState.Connected)
        {
            _dispatcherService.Enqueue(() => 
            {
                IsConnecting = false;
                ShowQrCode = false;
            });
        }
    }

    private void OnQrCodeReceived(object? sender, string qrString)
    {
        _dispatcherService.Enqueue(() => 
        {
            ShowQrCode = true;
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
    }
}
