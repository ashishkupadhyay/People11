namespace People.Core.Interfaces;

public interface ITrayIconService
{
    void Initialize();
    void SetBadgeCount(int count);
    void ShowFlyout();
    void HideFlyout();
    void ShowNotification(string title, string message);
}
