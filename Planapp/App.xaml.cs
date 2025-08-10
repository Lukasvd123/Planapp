using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace com.usagemeter.androidapp
{
    public partial class App : Application
    {
        private ILogger<App>? _logger;

        public App()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP CONSTRUCTOR START ===");

                InitializeComponent();

                System.Diagnostics.Debug.WriteLine("=== APP CONSTRUCTOR COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== FATAL ERROR IN APP CONSTRUCTOR: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to write crash log
                try
                {
                    var crashLog = $"APP CONSTRUCTOR CRASH at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n\n";
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "app_constructor_crash.log"),
                        crashLog
                    );
                }
                catch { }

                throw; // Re-throw to trigger system error
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW START ===");

                // Try to get logger (might fail if services aren't ready)
                try
                {
                    var serviceProvider = Handler?.MauiContext?.Services;
                    _logger = serviceProvider?.GetService<ILogger<App>>();
                    System.Diagnostics.Debug.WriteLine("Logger obtained successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot get logger (non-critical): {ex.Message}");
                }

                // Create minimal window to test basic functionality
                System.Diagnostics.Debug.WriteLine("Creating MainPage...");
                var mainPage = new MainPage();
                System.Diagnostics.Debug.WriteLine("MainPage created successfully");

                System.Diagnostics.Debug.WriteLine("Creating Window...");
                var window = new Window(mainPage)
                {
                    Title = "Usage Meter - Debug Mode"
                };
                System.Diagnostics.Debug.WriteLine("Window created successfully");

                // Add minimal event handlers
                window.Created += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== WINDOW CREATED EVENT ===");
                    _logger?.LogInformation("Window created successfully");
                };

                window.Destroying += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== WINDOW DESTROYING EVENT ===");
                    _logger?.LogInformation("Window destroying");
                };

                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW COMPLETED SUCCESSFULLY ===");
                _logger?.LogInformation("Main window created successfully");

                return window;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== FATAL ERROR CREATING WINDOW: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to write crash log
                try
                {
                    var crashLog = $"CREATE WINDOW CRASH at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n" +
                                  $"Stack: {ex.StackTrace}\n\n";

                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "create_window_crash.log"),
                        crashLog
                    );
                }
                catch { }

                // Try to create absolute minimal fallback window
                try
                {
                    System.Diagnostics.Debug.WriteLine("Attempting emergency fallback window...");

                    var fallbackPage = new ContentPage();
                    fallbackPage.Content = new Label
                    {
                        Text = $"CRASH DEBUG MODE\n\nError: {ex.Message}\n\nCheck debug output for details.",
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Center,
                        BackgroundColor = Colors.Red,
                        TextColor = Colors.White,
                        Padding = 20
                    };

                    return new Window(fallbackPage) { Title = "CRASH DEBUG" };
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Even fallback window failed: {fallbackEx}");
                    throw; // Re-throw original exception
                }
            }
        }

        protected override void OnStart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP ONSTART ===");
                base.OnStart();
                _logger?.LogInformation("App OnStart called");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnStart: {ex}");
            }
        }

        protected override void OnSleep()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP ONSLEEP ===");
                base.OnSleep();
                _logger?.LogInformation("App OnSleep called");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSleep: {ex}");
            }
        }

        protected override void OnResume()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP ONRESUME ===");
                base.OnResume();
                _logger?.LogInformation("App OnResume called");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnResume: {ex}");
            }
        }
    }
}