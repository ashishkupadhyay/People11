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

                private bool _isInitializing = true;

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
        
        _isInitializing = false;
    }

    partial void OnSelectedThemeChanged(string value) { if (!_isInitializing) { _themeService.SetTheme(value); _settingsService.SetValue("AppTheme", value); } OnPropertyChanged(nameof(SelectedThemeIndex)); OnPropertyChanged(nameof(SelectedThemeDisplay)); }

    partial void OnSelectedBackdropChanged(string value) { if (!_isInitializing) { _themeService.SetBackdrop(value); _settingsService.SetValue("AppBackdrop", value); } OnPropertyChanged(nameof(SelectedBackdropIndex)); OnPropertyChanged(nameof(SelectedBackdropDisplay)); }

    partial void OnIsWhatsAppEnabledChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("WhatsAppEnabled", value); }

    partial void OnIsFlyoutEnabledChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("FlyoutEnabled", value); }

    partial void OnShowUnreadBadgeChanged(bool value) { if (!_isInitializing) _settingsService.SetValue("ShowUnreadBadge", value); }

    partial void OnWhatsAppBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_WhatsApp", value == "Brand"); }

    partial void OnTelegramBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Telegram", value == "Brand"); }

    partial void OnDiscordBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Discord", value == "Brand"); }

    partial void OnSignalBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_Signal", value == "Brand"); }

    partial void OnSmsBubbleColorOptionChanged(string value) { if (!_isInitializing) _settingsService.SetValue("BrandColor_SMS", value == "Brand"); }
}






