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

                // Don't set MainPage here - we'll use CreateWindow instead

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
                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW START (SHELL VERSION) ===");

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

                // Create window with Shell as MainPage
                System.Diagnostics.Debug.WriteLine("Creating Shell-based window...");
                var window = new Window(new AppShell())
                {
                    Title = "Usage Meter"
                };
                System.Diagnostics.Debug.WriteLine("Shell window created successfully");

                // Add event handlers
                window.Created += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== SHELL WINDOW CREATED EVENT ===");
                    _logger?.LogInformation("Shell window created successfully");
                };

                window.Destroying += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== SHELL WINDOW DESTROYING EVENT ===");
                    _logger?.LogInformation("Shell window destroying");
                };

                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW COMPLETED SUCCESSFULLY (SHELL) ===");
                _logger?.LogInformation("Shell-based main window created successfully");

                return window;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== FATAL ERROR CREATING SHELL WINDOW: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to write crash log
                try
                {
                    var crashLog = $"CREATE SHELL WINDOW CRASH at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n" +
                                  $"Stack: {ex.StackTrace}\n\n";

                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "create_shell_window_crash.log"),
                        crashLog
                    );
                }
                catch { }

                // Try to create fallback window with basic MainPage
                try
                {
                    System.Diagnostics.Debug.WriteLine("Attempting emergency fallback to basic MainPage...");

                    var fallbackPage = new MainPage();
                    return new Window(fallbackPage) { Title = "Usage Meter - Fallback" };
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Even fallback MainPage failed: {fallbackEx}");
                    throw; // Re-throw original exception
                }
            }
        }

        protected override void OnStart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP ONSTART (SHELL) ===");
                base.OnStart();
                _logger?.LogInformation("Shell-based app OnStart called");
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
                System.Diagnostics.Debug.WriteLine("=== APP ONSLEEP (SHELL) ===");
                base.OnSleep();
                _logger?.LogInformation("Shell-based app OnSleep called");
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
                System.Diagnostics.Debug.WriteLine("=== APP ONRESUME (SHELL) ===");
                base.OnResume();
                _logger?.LogInformation("Shell-based app OnResume called");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnResume: {ex}");
            }
        }
    }
}