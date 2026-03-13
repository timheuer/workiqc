using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WorkIQC.App.Services;
using WorkIQC.App.ViewModels;
using WorkIQC.App.Views;
using WorkIQC.Persistence;
using WorkIQC.Runtime.Abstractions;

namespace WorkIQC.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private IServiceProvider? _serviceProvider;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            AppTraceBootstrapper.Initialize();
            UnhandledException += (_, args) =>
            {
                Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.App] [fatal.xaml] Handled={args.Handled}; Exception={args.Exception}");
            };
            ConfigureServices();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddPersistence();
            services.AddSingleton<ICopilotBootstrap, LocalCopilotBootstrap>();
            services.AddSingleton<ISessionCoordinator, LocalSessionCoordinator>();
            services.AddSingleton<IMessageOrchestrator, LocalMessageOrchestrator>();
            services.AddSingleton<IChatShellService, ChatShellService>();
            services.AddTransient<MainPageViewModel>();
            services.AddTransient<MainPage>();
            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Initialize database on first run
            if (_serviceProvider != null)
            {
                await _serviceProvider.InitializeDatabaseAsync();
            }

            if (_window is null)
            {
                var mainWindow = new MainWindow();
                mainWindow.SetContent(_serviceProvider?.GetRequiredService<MainPage>()!);
                _window = mainWindow;
            }

            _window.Activate();
        }

        public IServiceProvider? Services => _serviceProvider;
    }
}
