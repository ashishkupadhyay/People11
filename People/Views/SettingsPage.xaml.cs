using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using People.ViewModels;

namespace People.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private void ReplayOnboarding_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (App.Current.MainWindow is MainWindow mw)
        {
            mw.ShowOnboarding();
        }
    }
}
