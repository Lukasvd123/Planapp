using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Services;

namespace com.usagemeter.androidapp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            try
            {
                // Add settings service first
                builder.Services.AddSingleton<ISettingsService, SettingsService>();

                // Register platform-specific services
#if ANDROID
                builder.Services.AddSingleton<IUsageStatsService, Platforms.Android.UsageStatsServiceImpl>();
                builder.Services.AddSingleton<IRuleService, Platforms.Android.AndroidRuleService>();
                builder.Services.AddSingleton<IAppLaunchMonitor, Platforms.Android.AndroidAppLaunchMonitor>();
                builder.Services.AddSingleton<Platforms.Android.AndroidForegroundService>();
#else
                builder.Services.AddSingleton<IUsageStatsService, Services.DefaultUsageStatsService>();
                builder.Services.AddSingleton<IRuleService, Services.DefaultRuleService>();
                builder.Services.AddSingleton<IAppLaunchMonitor, Services.DefaultAppLaunchMonitor>();
#endif

                // Add core services
                builder.Services.AddSingleton<IRuleBlockService, RuleBlockService>();
                builder.Services.AddSingleton<RuleMonitorService>();

                // Add Blazor WebView
                builder.Services.AddMauiBlazorWebView();

#if DEBUG
                builder.Services.AddBlazorWebViewDeveloperTools();
                builder.Logging.AddDebug();
                builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
                builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

                // Configure logging levels
                builder.Logging.AddFilter("com.usagemeter.androidapp.Services.RuleMonitorService", LogLevel.Information);
                builder.Logging.AddFilter("com.usagemeter.androidapp.Platforms.Android.AndroidAppLaunchMonitor", LogLevel.Information);
                builder.Logging.AddFilter("com.usagemeter.androidapp.Services.RuleBlockService", LogLevel.Information);

                var app = builder.Build();

#if ANDROID
                // Delayed initialization with better error recovery
                Task.Run(async () =>
                {
                    // Wait longer for full initialization
                    await Task.Delay(5000);

                    try
                    {
                        var logger = app.Services.GetService<ILogger<App>>();
                        logger?.LogInformation("Starting Android initialization...");

                        // Initialize notification channel
                        com.usagemeter.androidapp.Platforms.Android.AndroidNotificationHelper.InitializeNotificationChannel();

                        // Check permissions
                        var usageService = app.Services.GetService<IUsageStatsService>();
                        if (usageService != null && !usageService.HasUsagePermission())
                        {
                            logger?.LogWarning("Usage stats permission not granted");
                        }

                        // Start foreground service for background monitoring
                        var foregroundService = app.Services.GetService<Platforms.Android.AndroidForegroundService>();
                        if (foregroundService != null)
                        {
                            await foregroundService.StartAsync();
                            logger?.LogInformation("Foreground service started");
                        }

                        logger?.LogInformation("Android initialization completed");
                    }
                    catch (Exception ex)
                    {
                        var logger = app.Services.GetService<ILogger<App>>();
                        logger?.LogError(ex, "Error during Android initialization (will retry): {Message}", ex.Message);

                        // Retry after delay
                        await Task.Delay(3000);
                        try
                        {
                            com.usagemeter.androidapp.Platforms.Android.AndroidNotificationHelper.InitializeNotificationChannel();
                            logger?.LogInformation("Retry successful");
                        }
                        catch
                        {
                            // Ignore retry errors
                        }
                    }
                });
#endif

                return app;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating MAUI app: {ex}");

                // Create minimal app that will work
                builder.Services.AddSingleton<ISettingsService, SettingsService>();
                builder.Services.AddSingleton<IUsageStatsService, Services.DefaultUsageStatsService>();
                builder.Services.AddSingleton<IRuleService, Services.DefaultRuleService>();
                builder.Services.AddSingleton<IAppLaunchMonitor, Services.DefaultAppLaunchMonitor>();
                builder.Services.AddSingleton<IRuleBlockService, RuleBlockService>();
                builder.Services.AddSingleton<RuleMonitorService>();
                builder.Services.AddMauiBlazorWebView();

                return builder.Build();
            }
        }
    }
}