using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Data;
using People.Data.Repositories;
using People.Providers;
using People.Services;
using People.ViewModels;
using Windows.Storage;

namespace People;

public partial class App : Application
{
    public Window? MainWindow { get; private set; }
    
    public new static App Current => (App)Application.Current;
    public IServiceProvider Services { get; private set; }

    public App()
    {
        InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var logFile = Path.Combine(desktop, "people_crash.txt");
            File.WriteAllText(logFile, e.Exception.ToString());
        };
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Data Layer
        var dbPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "people.db");
        services.AddDbContextFactory<PeopleDbContext>(options =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(options, $"Data Source={dbPath}"));
            
        services.AddSingleton<IChatRepository, ChatRepository>();
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<IContactRepository, ContactRepository>();

        // Services
        services.AddLogging(builder => 
        {
            builder.AddDebug();
        });
        
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICredentialStore, SecureCredentialStore>();
        
        // Navigation and Theme
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        // services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        
        var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue != null)
        {
            services.AddSingleton<IDispatcherService>(new DispatcherService(dispatcherQueue));
        }

        // Providers
        services.AddSingleton<IMessagingProvider, People.Providers.WhatsApp.WhatsAppProvider>();
        services.AddSingleton<ProviderManager>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<ChatListViewModel>();
        services.AddTransient<ConversationViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow.Activate();

            var trayService = Services.GetRequiredService<ITrayIconService>();
            trayService.Initialize();
        }
        catch (Exception ex)
        {
            var logger = Services.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Failed to start application properly.");
        }
    }
}

