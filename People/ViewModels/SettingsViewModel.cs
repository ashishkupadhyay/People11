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

    public bool IsWhatsAppBrandColor
    {
        get => _settingsService.GetValue<bool>("BrandColor_WhatsApp", false);
        set 
        { 
            _settingsService.SetValue("BrandColor_WhatsApp", value);
            OnPropertyChanged(nameof(IsWhatsAppBrandColor));
            OnPropertyChanged(nameof(IsWhatsAppAccentColor));
        }
    }
    
    public bool IsWhatsAppAccentColor
    {
        get => !IsWhatsAppBrandColor;
        set { if (value) IsWhatsAppBrandColor = false; }
    }

    public bool IsTelegramBrandColor
    {
        get => _settingsService.GetValue<bool>("BrandColor_Telegram", false);
        set 
        { 
            _settingsService.SetValue("BrandColor_Telegram", value);
            OnPropertyChanged(nameof(IsTelegramBrandColor));
            OnPropertyChanged(nameof(IsTelegramAccentColor));
        }
    }
    
    public bool IsTelegramAccentColor
    {
        get => !IsTelegramBrandColor;
        set { if (value) IsTelegramBrandColor = false; }
    }

    [ObservableProperty]
    public partial bool IsFlyoutEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowUnreadBadge { get; set; } = true;

    public ObservableCollection<string> Themes { get; } = new() { "Light", "Dark", "Default" };
    public ObservableCollection<string> Backdrops { get; } = new() { "Mica", "Desktop Acrylic", "Mica Alt", "None" };

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
}
