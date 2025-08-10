using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace com.usagemeter.androidapp
{
    public partial class App : Application
    {
        private bool _isInitialized = false;
        private int _initAttempts = 0;
        private const int MAX_INIT_ATTEMPTS = 3;
        private ILogger<App>? _logger;

        public App()
        {
            try
            {
                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("App constructor completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in App constructor: {ex}");
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window? window = null;

            try
            {
                // Initialize logger
                var serviceProvider = Handler?.MauiContext?.Services;
                _logger = serviceProvider?.GetService<ILogger<App>>();

                _logger?.LogInformation("Creating main window");

                var mainPage = new MainPage();
                window = new Window(mainPage)
                {
                    Title = "Usage Meter",
                    MinimumHeight = 600,
                    MinimumWidth = 400
                };

                // Configure window events
                window.Created += OnWindowCreated;
                window.Destroying += OnWindowDestroying;
                window.Resumed += OnWindowResumed;
                window.Stopped += OnWindowStopped;

                _logger?.LogInformation("Main window created successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating window: {ex}");
                _logger?.LogError(ex, "Error creating main window");

                // Create fallback window
                window = CreateFallbackWindow();
            }

            return window;
        }

        private Window CreateFallbackWindow()
        {
            return new Window(new ContentPage
            {
                Content = new StackLayout
                {
                    Children =
                    {
                        new Label
                        {
                            Text = "Usage Meter",
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            TextColor = Colors.Black
                        },
                        new Label
                        {
                            Text = "Loading...",
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            TextColor = Colors.Gray
                        }
                    },
                    BackgroundColor = Colors.White,
                    Padding = new Thickness(20)
                }
            })
            {
                Title = "Usage Meter"
            };
        }

        private async void OnWindowCreated(object? sender, EventArgs e)
        {
            if (_isInitialized) return;

            while (!_isInitialized && _initAttempts < MAX_INIT_ATTEMPTS)
            {
                _initAttempts++;
                _logger?.LogInformation($"App initialization attempt {_initAttempts}");

                try
                {
                    // Progressive delay for retries
                    await Task.Delay(_initAttempts * 1000);

                    await InitializeServicesAsync();

#if ANDROID
                    await CheckAndSetupAndroidPermissions();
#endif

                    _isInitialized = true;
                    _logger?.LogInformation("App initialization successful");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"App initialization attempt {_initAttempts} failed");
                    System.Diagnostics.Debug.WriteLine($"App initialization attempt {_initAttempts} failed: {ex}");

                    if (_initAttempts >= MAX_INIT_ATTEMPTS)
                    {
                        await ShowInitializationError();
                    }
                }
            }
        }

        private async void OnWindowResumed(object? sender, EventArgs e)
        {
            _logger?.LogInformation("App window resumed");

            if (_isInitialized)
            {
                await StartMonitoringIfEnabled();
            }
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
            _logger?.LogInformation("App window stopped");
        }

        private void OnWindowDestroying(object? sender, EventArgs e)
        {
            _logger?.LogInformation("App window destroying");
            _isInitialized = false;
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                var serviceProvider = Handler?.MauiContext?.Services;
                if (serviceProvider == null)
                {
                    throw new InvalidOperationException("Service provider not available");
                }

                // Initialize settings service
                var settingsService = serviceProvider.GetRequiredService<Services.ISettingsService>();
                var settings = await settingsService.GetSettingsAsync();
                _logger?.LogInformation($"Settings loaded - Rules enabled: {settings.AllRulesEnabled}");

                // Initialize other services
                var ruleService = serviceProvider.GetService<Services.IRuleService>();
                var rules = await ruleService?.GetRulesAsync()!;
                _logger?.LogInformation($"Loaded {rules?.Count ?? 0} rules");

                _logger?.LogInformation("All services initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing services");
                throw;
            }
        }

        private async Task StartMonitoringIfEnabled()
        {
            try
            {
                var serviceProvider = Handler?.MauiContext?.Services;
                if (serviceProvider == null) return;

                var settingsService = serviceProvider.GetService<Services.ISettingsService>();
                var settings = await settingsService?.GetSettingsAsync()!;

                if (settings?.AllRulesEnabled == true)
                {
                    var ruleMonitor = serviceProvider.GetService<Services.RuleMonitorService>();
                    if (ruleMonitor != null)
                    {
                        await ruleMonitor.StartAsync();
                        _logger?.LogInformation("Rule monitoring started");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error starting monitoring");
            }
        }

#if ANDROID
        private async Task CheckAndSetupAndroidPermissions()
        {
            try
            {
                _logger?.LogInformation("Checking Android permissions");

                // Initialize notification channel
                Platforms.Android.AndroidNotificationHelper.InitializeNotificationChannel();

                // Check notification permission for Android 13+
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
                {
                    if (!Platforms.Android.AndroidNotificationHelper.CheckNotificationPermission())
                    {
                        _logger?.LogInformation("Requesting notification permission");
                        Platforms.Android.AndroidNotificationHelper.RequestNotificationPermission();
                    }
                }

                _logger?.LogInformation("Android permissions setup completed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting up Android permissions");
            }
        }
#endif

        private async Task ShowInitializationError()
        {
            try
            {
                if (Current?.MainPage != null)
                {
                    await Current.MainPage.DisplayAlert(
                        "Initialization Error",
                        "The app failed to start properly. Some features may not work correctly. Please try restarting the app.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing initialization error dialog");
            }
        }

        protected override void OnStart()
        {
            base.OnStart();
            _logger?.LogInformation("App OnStart called");
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            _logger?.LogInformation("App OnSleep called");
        }

        protected override void OnResume()
        {
            base.OnResume();
            _logger?.LogInformation("App OnResume called");

            if (_isInitialized)
            {
                Task.Run(async () => await StartMonitoringIfEnabled());
            }
        }
    }
}