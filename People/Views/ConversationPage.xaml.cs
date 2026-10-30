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

        ViewModel.Messages.CollectionChanged += (s, e) =>
        {
            if (ViewModel.Messages.Count > 0)
            {
                MessagesListView.ScrollIntoView(ViewModel.Messages[^1]);
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Chat chat)
        {
            ViewModel.LoadChat(chat);
        }
        base.OnNavigatedTo(e);
    }
}
