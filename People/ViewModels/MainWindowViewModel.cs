using People.Core.Interfaces;
using People.Providers;

namespace People.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly ProviderManager _providerManager;

    public MainWindowViewModel(
        INavigationService navigationService,
        ProviderManager providerManager)
    {
        _navigationService = navigationService;
        _providerManager = providerManager;
    }

    private bool _isBackEnabled;
    public bool IsBackEnabled
    {
        get => _isBackEnabled;
        set => SetProperty(ref _isBackEnabled, value);
    }
}
