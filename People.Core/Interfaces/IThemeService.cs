namespace People.Core.Interfaces;

public interface IThemeService
{
    void Initialize();
    void SetTheme(string theme);
    void SetBackdrop(string backdrop);
    void ApplyToWindow(object window);
    void SetActivePlatform(string? platformId);
    void ClearActivePlatform();
    void ApplyBrandColorIfActive(string platformId);
    void RestoreSystemAccentColor();
}
