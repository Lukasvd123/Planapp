namespace Planapp
{
    public partial class App : Application
    {
        private bool _isInitialized = false;
        private int _initAttempts = 0;
        private const int MAX_INIT_ATTEMPTS = 3;

        public App()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in App constructor: {ex}");
                // Try to continue anyway
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window? window = null;

            try
            {
                var mainPage = new MainPage();
                window = new Window(mainPage)
                {
                    Title = "Usage Meter",
                    MinimumHeight = 600,
                    MinimumWidth = 400
                };

                // Handle window creation events
                window.Created += OnWindowCreated;
                window.Destroying += OnWindowDestroying;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating window: {ex}");

                // Create a minimal window as fallback
                window = new Window(new ContentPage
                {
                    Content = new Label
                    {
                        Text = "Error starting app. Please restart.",
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                })
                {
                    Title = "Usage Meter"
                };
            }

            return window;
        }

        private async void OnWindowCreated(object? sender, EventArgs e)
        {
            if (_isInitialized) return;

            while (!_isInitialized && _initAttempts < MAX_INIT_ATTEMPTS)
            {
                _initAttempts++;

                try
                {
                    // Wait a bit for services to be ready
                    await Task.Delay(_initAttempts * 1000); // Increase delay with each attempt

#if ANDROID
                    // Check if we have necessary permissions
                    await CheckAndRequestPermissions();
#endif

                    _isInitialized = true;
                    System.Diagnostics.Debug.WriteLine("App initialization successful");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"App initialization attempt {_initAttempts} failed: {ex}");

                    if (_initAttempts >= MAX_INIT_ATTEMPTS)
                    {
                        // Show error to user
                        if (Current?.MainPage != null)
                        {
                            await Current.MainPage.DisplayAlert(
                                "Initialization Error",
                                "The app failed to start properly. Please close and restart the app.",
                                "OK");
                        }
                    }
                }
            }
        }

        private void OnWindowDestroying(object? sender, EventArgs e)
        {
            // Cleanup if needed
            _isInitialized = false;
        }

#if ANDROID
        private async Task CheckAndRequestPermissions()
        {
            try
            {
                // Check for usage stats permission
                var usagePermission = await Permissions.CheckStatusAsync<Permissions.StorageRead>();

                // Note: PACKAGE_USAGE_STATS permission cannot be requested through the normal 
                // permission flow, user must grant it manually in settings

                // Check for notification permission (Android 13+)
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
                {
                    var notificationStatus = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                    if (notificationStatus != PermissionStatus.Granted)
                    {
                        await Permissions.RequestAsync<Permissions.PostNotifications>();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking permissions: {ex}");
            }
        }
#endif

        protected override void OnStart()
        {
            base.OnStart();
            System.Diagnostics.Debug.WriteLine("App OnStart called");
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            System.Diagnostics.Debug.WriteLine("App OnSleep called");
        }

        protected override void OnResume()
        {
            base.OnResume();
            System.Diagnostics.Debug.WriteLine("App OnResume called");
        }
    }
}