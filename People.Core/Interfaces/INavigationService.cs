namespace People.Core.Interfaces;

public interface INavigationService
{
    bool CanGoBack { get; }
    void GoBack();
    bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false);
}
