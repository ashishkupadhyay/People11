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
    private void Reauthenticate()
    {
        // TODO: Trigger re-authentication flow
    }

    public SettingsViewModel(ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        SelectedTheme = _settingsService.GetValue<string>("AppTheme") ?? "Default";
        SelectedBackdrop = _settingsService.GetValue<string>("AppBackdrop") ?? "Mica";
        IsWhatsAppEnabled = _settingsService.GetValue<bool>("WhatsAppEnabled", true);
        IsFlyoutEnabled = _settingsService.GetValue<bool>("FlyoutEnabled", true);
        ShowUnreadBadge = _settingsService.GetValue<bool>("ShowUnreadBadge", true);
        WhatsAppBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_WhatsApp", false) ? "Brand" : "Accent";
        TelegramBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Telegram", false) ? "Brand" : "Accent";
        DiscordBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Discord", false) ? "Brand" : "Accent";
        SignalBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_Signal", false) ? "Brand" : "Accent";
        SmsBubbleColorOption = _settingsService.GetValue<bool>("BrandColor_SMS", false) ? "Brand" : "Accent";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _themeService.SetTheme(value);
        _settingsService.SetValue("AppTheme", value);
    }

    partial void OnSelectedBackdropChanged(string value)
    {
        _themeService.SetBackdrop(value);
        _settingsService.SetValue("AppBackdrop", value);
    }

    partial void OnIsWhatsAppEnabledChanged(bool value)
    {
        _settingsService.SetValue("WhatsAppEnabled", value);
    }

    partial void OnIsFlyoutEnabledChanged(bool value)
    {
        _settingsService.SetValue("FlyoutEnabled", value);
    }

    partial void OnShowUnreadBadgeChanged(bool value)
    {
        _settingsService.SetValue("ShowUnreadBadge", value);
    }

    partial void OnWhatsAppBubbleColorOptionChanged(string value)
    {
        _settingsService.SetValue("BrandColor_WhatsApp", value == "Brand");
    }

    partial void OnTelegramBubbleColorOptionChanged(string value)
    {
        _settingsService.SetValue("BrandColor_Telegram", value == "Brand");
    }

    partial void OnDiscordBubbleColorOptionChanged(string value)
    {
        _settingsService.SetValue("BrandColor_Discord", value == "Brand");
    }

    partial void OnSignalBubbleColorOptionChanged(string value)
    {
        _settingsService.SetValue("BrandColor_Signal", value == "Brand");
    }

    partial void OnSmsBubbleColorOptionChanged(string value)
    {
        _settingsService.SetValue("BrandColor_SMS", value == "Brand");
    }
}
