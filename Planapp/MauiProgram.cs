using com.usagemeter.androidapp.Services;
using Microsoft.Extensions.Logging;

#if ANDROID
using com.usagemeter.androidapp.Platforms.Android;
#endif

namespace com.usagemeter.androidapp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== MAUI PROGRAM START ===");

                var builder = MauiApp.CreateBuilder();
                System.Diagnostics.Debug.WriteLine("MauiApp builder created");

                builder
                    .UseMauiApp<App>()
                    .ConfigureFonts(fonts =>
                    {
                        try
                        {
                            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                            System.Diagnostics.Debug.WriteLine("Fonts configured successfully");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ERROR configuring fonts: {ex}");
                        }
                    });

                System.Diagnostics.Debug.WriteLine("Basic MAUI app configured");

                // Add Blazor WebView with error handling
                try
                {
                    builder.Services.AddMauiBlazorWebView();
                    System.Diagnostics.Debug.WriteLine("Blazor WebView added successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR adding Blazor WebView: {ex}");
                    throw; // This is critical, can't continue without it
                }

#if DEBUG
                try
                {
                    builder.Services.AddBlazorWebViewDeveloperTools();
                    builder.Logging.AddDebug();
                    builder.Logging.SetMinimumLevel(LogLevel.Debug);
                    System.Diagnostics.Debug.WriteLine("Debug tools and logging configured");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR configuring debug tools: {ex}");
                    // Non-critical, continue
                }
#else
                builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

                // Core Services with individual error handling
                try
                {
                    System.Diagnostics.Debug.WriteLine("Registering core services...");
                    builder.Services.AddSingleton<ISettingsService, SettingsService>();
                    System.Diagnostics.Debug.WriteLine("✓ Settings service registered");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR registering settings service: {ex}");
                    throw;
                }

                // Platform-specific services with error handling
                try
                {
                    System.Diagnostics.Debug.WriteLine("Registering platform-specific services...");
#if ANDROID
                    builder.Services.AddSingleton<IRuleService, AndroidRuleService>();
                    System.Diagnostics.Debug.WriteLine("✓ Android rule service registered");

                    builder.Services.AddSingleton<IUsageStatsService, UsageStatsServiceImpl>();
                    System.Diagnostics.Debug.WriteLine("✓ Android usage stats service registered");

                    builder.Services.AddSingleton<IAppLaunchMonitor, AndroidAppLaunchMonitor>();
                    System.Diagnostics.Debug.WriteLine("✓ Android app launch monitor registered");
#else
                    builder.Services.AddSingleton<IRuleService, DefaultRuleService>();
                    System.Diagnostics.Debug.WriteLine("✓ Default rule service registered");
                    
                    builder.Services.AddSingleton<IUsageStatsService, DefaultUsageStatsService>();
                    System.Diagnostics.Debug.WriteLine("✓ Default usage stats service registered");
                    
                    builder.Services.AddSingleton<IAppLaunchMonitor, DefaultAppLaunchMonitor>();
                    System.Diagnostics.Debug.WriteLine("✓ Default app launch monitor registered");
#endif
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR registering platform services: {ex}");
                    throw;
                }

                // Business logic services with error handling
                try
                {
                    System.Diagnostics.Debug.WriteLine("Registering business logic services...");
                    builder.Services.AddSingleton<IRuleBlockService, Services.RuleBlockService>();
                    System.Diagnostics.Debug.WriteLine("✓ Rule block service registered");

                    builder.Services.AddSingleton<RuleMonitorService>();
                    System.Diagnostics.Debug.WriteLine("✓ Rule monitor service registered");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR registering business services: {ex}");
                    throw;
                }

                System.Diagnostics.Debug.WriteLine("All services registered, building app...");

                // Build the app with error handling
                MauiApp? app = null;
                try
                {
                    app = builder.Build();
                    System.Diagnostics.Debug.WriteLine("=== MAUI APP BUILT SUCCESSFULLY ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"=== CRITICAL ERROR BUILDING APP: {ex} ===");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                    // Try to provide more specific error info
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException}");
                    }

                    throw; // Re-throw to show system error
                }

                // Removed the problematic post-build initialization that was causing the cast exception

                System.Diagnostics.Debug.WriteLine("=== MAUI PROGRAM COMPLETED SUCCESSFULLY ===");
                return app;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== FATAL ERROR IN MAUI PROGRAM: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"=== INNER EXCEPTION: {ex.InnerException} ===");
                }

                // Try to write to a file as well for persistence
                try
                {
                    var errorLog = $"FATAL MAUI ERROR at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n" +
                                  $"Stack: {ex.StackTrace}\n" +
                                  $"Inner: {ex.InnerException}\n\n";

                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "maui_crash.log"),
                        errorLog
                    );
                }
                catch
                {
                    // Can't even write log file
                }

                throw; // Re-throw to trigger system error dialog
            }
        }
    }
}