using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Interfaces;

namespace People.Views;

public sealed partial class OnboardingPage : Page
{
    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += OnboardingPage_Loaded;
    }

    private void OnboardingPage_Loaded(object sender, RoutedEventArgs e)
    {
        CanopyEntranceStoryboard.Begin();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (App.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.SetTitleBar(OnboardingTitleBar);
        }
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        settingsService.SetValue("HasCompletedOnboarding", true);

        if (this.Frame is Frame rootFrame)
        {
            rootFrame.Navigate(typeof(MainShellPage), null, new DrillInNavigationTransitionInfo());
            rootFrame.BackStack.Clear();
        }
    }
}
