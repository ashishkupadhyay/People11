using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace People;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        bool isRedirect = DecideRedirection();
        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }

    private static bool DecideRedirection()
    {
        bool isRedirect = false;
        AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();

        AppInstance keyInstance = AppInstance.FindOrRegisterForKey("PeopleAppInstance");

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
        }
        else
        {
            isRedirect = true;
            RedirectActivationTo(args, keyInstance);
        }

        return isRedirect;
    }

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        var redirectSemaphore = new Semaphore(0, 1);
        Task.Run(async () =>
        {
            await keyInstance.RedirectActivationToAsync(args);
            redirectSemaphore.Release();
        });
        redirectSemaphore.WaitOne(3000);
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        if (App.Current is App app)
        {
            app.BringToForeground();
        }
    }
}

