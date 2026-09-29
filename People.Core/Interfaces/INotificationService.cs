namespace People.Core.Interfaces;

public interface INotificationService
{
    void ShowToast(string title, string content, string? payload = null);
    void ClearNotifications();
}
