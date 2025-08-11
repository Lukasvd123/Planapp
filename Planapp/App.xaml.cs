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
                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW START (BLAZOR ONLY VERSION) ===");

                // Safe service provider access
                try
                {
                    // More defensive approach to getting services
                    if (Handler?.MauiContext != null)
                    {
                        var services = Handler.MauiContext.Services;
                        if (services != null)
                        {
                            _logger = services.GetService<ILogger<App>>();
                            System.Diagnostics.Debug.WriteLine("Logger obtained successfully");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot get logger (non-critical): {ex.Message}");
                }

                // Create window with MainPage directly (no Shell)
                System.Diagnostics.Debug.WriteLine("Creating BlazorWebView-based window...");

                MainPage mainPage;
                try
                {
                    mainPage = new MainPage();
                }
                catch (Exception pageEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating MainPage: {pageEx}");
                    throw;
                }

                var window = new Window(mainPage)
                {
                    Title = "Usage Meter"
                };
                System.Diagnostics.Debug.WriteLine("BlazorWebView window created successfully");

                // Add event handlers
                window.Created += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== BLAZOR WINDOW CREATED EVENT ===");
                    _logger?.LogInformation("BlazorWebView window created successfully");
                };

                window.Destroying += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("=== BLAZOR WINDOW DESTROYING EVENT ===");
                    _logger?.LogInformation("BlazorWebView window destroying");
                };

                System.Diagnostics.Debug.WriteLine("=== CREATE WINDOW COMPLETED SUCCESSFULLY (BLAZOR) ===");
                _logger?.LogInformation("BlazorWebView-based main window created successfully");

                return window;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== FATAL ERROR CREATING BLAZOR WINDOW: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to write crash log
                try
                {
                    var crashLog = $"CREATE BLAZOR WINDOW CRASH at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n" +
                                  $"Stack: {ex.StackTrace}\n\n";

                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "create_blazor_window_crash.log"),
                        crashLog
                    );
                }
                catch { }

                throw; // Re-throw original exception
            }
        }

        protected override void OnStart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== APP ONSTART (BLAZOR) ===");
                base.OnStart();
                _logger?.LogInformation("BlazorWebView-based app OnStart called");
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
                System.Diagnostics.Debug.WriteLine("=== APP ONSLEEP (BLAZOR) ===");
                base.OnSleep();
                _logger?.LogInformation("BlazorWebView-based app OnSleep called");
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
                System.Diagnostics.Debug.WriteLine("=== APP ONRESUME (BLAZOR) ===");
                base.OnResume();
                _logger?.LogInformation("BlazorWebView-based app OnResume called");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnResume: {ex}");
            }
        }
    }
}