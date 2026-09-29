namespace People.Core.Interfaces;

public interface IThemeService
{
    void Initialize();
    void SetTheme(string theme);
    void SetBackdrop(string backdrop);
    void ApplyToWindow(object window);
    void ApplyBrandColorIfActive(string platformId);
    void RestoreSystemAccentColor();
}
