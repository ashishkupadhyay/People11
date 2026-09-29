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
}
