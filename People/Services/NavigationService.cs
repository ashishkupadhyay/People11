using Microsoft.UI.Xaml.Controls;
using People.Core.Interfaces;

namespace People.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;
    private readonly Dictionary<string, Type> _pages = new();

    public void Initialize(Frame frame)
    {
        _frame = frame;
    }

    public void Configure(string key, Type pageType)
    {
        _pages.TryAdd(key, pageType);
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void GoBack()
    {
        if (CanGoBack)
        {
            _frame?.GoBack();
        }
    }

    private object? _lastParameter;

    public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false)
    {
        if (_frame == null) return false;

        if (_pages.TryGetValue(pageKey, out var pageType))
        {
            if (_frame.SourcePageType == pageType && Equals(parameter, _lastParameter))
            {
                return false;
            }

            if (clearNavigation)
            {
                _frame.BackStack.Clear();
            }
            
            _lastParameter = parameter;
            return _frame.Navigate(pageType, parameter);
        }
        
        return false;
    }
}
