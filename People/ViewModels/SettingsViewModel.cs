using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
using System.Collections.ObjectModel;

namespace People.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "Default";
    
    [ObservableProperty]
    public partial string SelectedBackdrop { get; set; } = "Mica";
    
    [ObservableProperty]
    public partial bool IsWhatsAppEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsWhatsAppTestingMode { get; set; } = false;

    [ObservableProperty]
    public partial bool WhatsAppAutoDownloadPhotos { get; set; } = true;

    [ObservableProperty]
    public partial bool WhatsAppAutoDownloadVideos { get; set; } = false;

    [ObservableProperty]
    public partial bool WhatsAppAutoDownloadDocuments { get; set; } = true;

    [ObservableProperty]
    public partial bool EnterSendsMessage { get; set; } = true;

    [ObservableProperty]
    public partial string WhatsAppBubbleColorOption { get; set; } = "Accent";

    [ObservableProperty]
    public partial string TelegramBubbleColorOption { get; set; } = "Accent";


    [ObservableProperty]
    public partial string DiscordBubbleColorOption { get; set; } = "Accent";

    [ObservableProperty]
    public partial string SmsBubbleColorOption { get; set; } = "Accent";

    [ObservableProperty]
    public partial string SignalBubbleColorOption { get; set; } = "Accent";

    [ObservableProperty]
    public partial bool IsFlyoutEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowUnreadBadge { get; set; } = true;
    public int SelectedThemeIndex
    {
        get => SelectedTheme switch { "Light" => 0, "Dark" => 1, _ => 2 };
        set => SelectedTheme = value switch { 0 => "Light", 1 => "Dark", _ => "Default" };
    }
    public string SelectedThemeDisplay => SelectedTheme switch { "Light" => "Light", "Dark" => "Dark", _ => "Use system setting" };

    public int SelectedBackdropIndex
    {
        get => SelectedBackdrop switch { "Mica" => 0, "Desktop Acrylic" => 1, "Mica Alt" => 2, _ => 3 };
        set => SelectedBackdrop = value switch { 0 => "Mica", 1 => "Desktop Acrylic", 2 => "Mica Alt", _ => "None" };
    }
    public string SelectedBackdropDisplay => SelectedBackdrop switch { "Desktop Acrylic" => "Acrylic", "Mica Alt" => "Mica Alt", "None" => "None", _ => "Mica" };

    public int WhatsAppBubbleColorIndex
    {
        get => WhatsAppBubbleColorOption == "Brand" ? 0 : 1;
        set => WhatsAppBubbleColorOption = value == 0 ? "Brand" : "Accent";
    }
    public string WhatsAppBubbleColorDisplay => WhatsAppBubbleColorOption == "Brand" ? "WhatsApp (default colour scheme)" : "Use system setting";

    public int TelegramBubbleColorIndex
    {
        get => TelegramBubbleColorOption == "Brand" ? 0 : 1;
        set => TelegramBubbleColorOption = value == 0 ? "Brand" : "Accent";
    }
    public string TelegramBubbleColorDisplay => TelegramBubbleColorOption == "Brand" ? "Telegram (default colour scheme)" : "Use system setting";

    public int DiscordBubbleColorIndex
    {
        get => DiscordBubbleColorOption == "Brand" ? 0 : 1;
        set => DiscordBubbleColorOption = value == 0 ? "Brand" : "Accent";
    }
    public string DiscordBubbleColorDisplay => DiscordBubbleColorOption == "Brand" ? "Discord (default colour scheme)" : "Use system setting";

    public int SmsBubbleColorIndex
    {
        get => SmsBubbleColorOption == "Brand" ? 0 : 1;
        set => SmsBubbleColorOption = value == 0 ? "Brand" : "Accent";
    }
    public string SmsBubbleColorDisplay => SmsBubbleColorOption == "Brand" ? "SMS (default colour scheme)" : "Use system setting";

    public int SignalBubbleColorIndex
    {
        get => SignalBubbleColorOption == "Brand" ? 0 : 1;
        set => SignalBubbleColorOption = value == 0 ? "Brand" : "Accent";
    }
    public string SignalBubbleColorDisplay => SignalBubbleColorOption == "Brand" ? "Signal (default colour scheme)" : "Use system setting";

    public ObservableCollection<string> Themes { get; } = new() { "Light", "Dark", "Default" };
    public ObservableCollection<string> Backdrops { get; } = new() { "Mica", "Desktop Acrylic", "Mica Alt", "None" };
    public ObservableCollection<string> ColorOptions { get; } = new() { "Accent", "Brand" };

    [RelayCommand]
    private void TestNotification()
    {
        var trayService = App.Current.Services.GetRequiredService<ITrayIconService>();
        trayService.ShowNotification("New Message", "Hello from People App!");
    }

    [RelayCommand]
    private async Task ReauthenticateAsync()
    {
        var provider = App.Current.Services.GetService<IMessagingProvider>();
        if (provider is IWhatsAppProvider waProvider)
        {
            await waProvider.ReauthenticateAsync();
            var navService = App.Current.Services.GetRequiredService<INavigationService>();
            navService.NavigateTo("Chats", "WhatsApp");
        }
    }

    [RelayCommand]
    private async Task InjectSampleDataAsync()
    {
        var factory = App.Current.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<People.Data.PeopleDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var existingChat = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(dbContext.Chats, c => c.Id == "sample_wa_1");
        if (existingChat == null)
        {
            var userContact = new People.Core.Models.Contact
            {
                Id = "contact_sarah",
                Name = "Sarah Connor",
                PhoneNumber = "+1 555-0199",
                PlatformId = "WhatsApp"
            };

            var sampleChat = new People.Core.Models.Chat
            {
                Id = "sample_wa_1",
                Title = "Sarah Connor",
                PlatformId = "WhatsApp",
                UnreadCount = 1,
                LastMessageTime = DateTimeOffset.UtcNow
            };

            var sampleMsg1 = new People.Core.Models.Message
            {
                Id = "msg_1",
                ChatId = sampleChat.Id,
                SenderId = userContact.Id,
                PlatformId = "WhatsApp",
                Content = new People.Core.Models.MessageContent { Text = "Hey! Is the Windows 11 People app ready?" },
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15),
                Status = People.Core.Models.MessageStatus.Read,
                IsOutgoing = false
            };

            var sampleMsg2 = new People.Core.Models.Message
            {
                Id = "msg_2",
                ChatId = sampleChat.Id,
                SenderId = "me",
                PlatformId = "WhatsApp",
                Content = new People.Core.Models.MessageContent { Text = "Yes, we just implemented the full WhatsApp provider and testing mode!" },
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                Status = People.Core.Models.MessageStatus.Delivered,
                IsOutgoing = true
            };

            var groupChat = new People.Core.Models.Chat
            {
                Id = "sample_wa_2",
                Title = "Windows Dev Team",
                PlatformId = "WhatsApp",
                IsGroup = true,
                UnreadCount = 3,
                LastMessageTime = DateTimeOffset.UtcNow.AddHours(-1)
            };

            var groupMsg = new People.Core.Models.Message
            {
                Id = "msg_3",
                ChatId = groupChat.Id,
                SenderId = "dev_alex",
                PlatformId = "WhatsApp",
                Content = new People.Core.Models.MessageContent { Text = "The Fluent Design Mica backdrop looks amazing!" },
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-20),
                Status = People.Core.Models.MessageStatus.Read,
                IsOutgoing = false
            };

            dbContext.Contacts.Add(userContact);
            dbContext.Chats.AddRange(sampleChat, groupChat);
            await dbContext.SaveChangesAsync();

            dbContext.Messages.AddRange(sampleMsg1, sampleMsg2, groupMsg);
            await dbContext.SaveChangesAsync();

            sampleChat.LastMessage = sampleMsg2;
            groupChat.LastMessage = groupMsg;
            await dbContext.SaveChangesAsync();
        }

        var trayService = App.Current.Services.GetService<ITrayIconService>();
        trayService?.ShowNotification("Sample Data Injected", "Test chats and messages were added to WhatsApp.");
    }

    public SettingsViewModel(ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        
        LoadSettings();
    }

                private bool _isInitializing = true;

    private void LoadSettings()
    {
        SelectedTheme = _settingsService.GetValue<string>("AppTheme") ?? "Default";
        SelectedBackdrop = _settingsService.GetValue<string>("AppBackdrop") ?? "Mica";
        IsWhatsAppEnabled = _settingsService.GetValue<bool>("WhatsAppEnabled", true);
        IsWhatsAppTestingMode = _settingsService.GetValue<bool>("WhatsAppTestingMode", false);
        WhatsAppAutoDownloadPhotos = _settingsService.GetValue<bool>("WhatsAppAutoDownloadPhotos", true);
        WhatsAppAutoDownloadVideos = _settingsService.GetValue<bool>("WhatsAppAutoDownloadVideos", false);
        WhatsAppAutoDownloadDocuments = _settingsService.GetValue<bool>("WhatsAppAutoDownloadDocuments", true);
        EnterSendsMessage = _settingsService.GetValue<bool>("EnterSendsMessage", true);
        IsFlyoutEnabled = _settingsService.GetValue<bool>("FlyoutEnabled", true);
        ShowUnreadBadge = _settingsService.GetValue<bool>("ShowUnreadBadge", true);
        WhatsAppBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_WhatsApp", false) ? "Brand" : "Accent";
        TelegramBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Telegram", false) ? "Brand" : "Accent";
        DiscordBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Discord", false) ? "Brand" : "Accent";
        SignalBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Signal", false) ? "Brand" : "Accent";
        SmsBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_SMS", false) ? "Brand" : "Accent";
        
        _isInitializing = false;
    }

    partial void OnSelectedThemeChanged(string value) { if (!_isInitializing) { _themeService.SetTheme(value); _settingsService.SetValue("AppTheme", value); } OnPropertyChanged(nameof(SelectedThemeIndex)); OnPropertyChanged(nameof(SelectedThemeDisplay)); }

    partial void OnSelectedBackdropChanged(string value) { if (!_isInitializing) { _themeService.SetBackdrop(value); _settingsService.SetValue("AppBackdrop", value); } OnPropertyChanged(nameof(SelectedBackdropIndex)); OnPropertyChanged(nameof(SelectedBackdropDisplay)); }

    partial void OnIsWhatsAppEnabledChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppEnabled", value); }

    partial void OnIsWhatsAppTestingModeChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppTestingMode", value); }

    partial void OnWhatsAppAutoDownloadPhotosChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppAutoDownloadPhotos", value); }

    partial void OnWhatsAppAutoDownloadVideosChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppAutoDownloadVideos", value); }

    partial void OnWhatsAppAutoDownloadDocumentsChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppAutoDownloadDocuments", value); }

    partial void OnEnterSendsMessageChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("EnterSendsMessage", value); }

    partial void OnIsFlyoutEnabledChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("FlyoutEnabled", value); }

    partial void OnShowUnreadBadgeChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("ShowUnreadBadge", value); }

    partial void OnWhatsAppBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_WhatsApp", value == "Brand"); }

    partial void OnTelegramBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Telegram", value == "Brand"); }

    partial void OnDiscordBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Discord", value == "Brand"); }

    partial void OnSignalBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Signal", value == "Brand"); }

    partial void OnSmsBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_SMS", value == "Brand"); }
}






