using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using People.Core.Interfaces;

namespace People.Services;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private const string ThemeKey = "AppTheme";
    private string _currentTheme = "Default";

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        _currentTheme = _settingsService.GetValue(ThemeKey, "Default") ?? "Default";
    }

    public void SetTheme(string theme)
    {
        _currentTheme = theme;
        _settingsService.SetValue(ThemeKey, theme);
        
        // Apply to current window
        if (App.Current is App app)
        {
            // The actual theme change logic requires accessing the FrameworkElement of the Window content
            // and setting RequestedTheme. We will handle this at the Window level or via the root visual.
        }
    }

    public void SetBackdrop(string backdrop)
    {
        // For WinUI 3, setting backdrop (Mica / Desktop Acrylic) is usually done on the Window instance.
        // This will be invoked by the views/windows directly.
    }

    public void ApplyToWindow(object windowObj)
    {
        if (windowObj is Window window)
        {
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = _currentTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            
            // Enable Mica if on Windows 11
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }
        }
    }
}
