using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Models;
using People.ViewModels;

namespace People.Views;

public sealed partial class ConversationPage : Page
{
    public ConversationViewModel ViewModel { get; }

    public ConversationPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Chat chat)
        {
            ViewModel.LoadChat(chat);
            
            string platformId = chat.PlatformId;
            if (string.IsNullOrEmpty(platformId))
            {
                if (chat.Id?.StartsWith("wa_") == true) platformId = "WhatsApp";
                else if (chat.Id?.StartsWith("tg_") == true) platformId = "Telegram";
                else platformId = "WhatsApp";
            }
            
            var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
            themeService.SetActivePlatform(platformId);
        }
        base.OnNavigatedTo(e);
    }
}
