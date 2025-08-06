using Microsoft.Extensions.Logging;
using Planapp.Services;

namespace Planapp
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
                // Register platform-specific services
#if ANDROID
                builder.Services.AddSingleton<IUsageStatsService, Platforms.Android.UsageStatsServiceImpl>();
                builder.Services.AddSingleton<IRuleService, Platforms.Android.AndroidRuleService>();
                builder.Services.AddSingleton<IAppLaunchMonitor, Platforms.Android.AndroidAppLaunchMonitor>();
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

                // Configure logging levels for specific services
                builder.Logging.AddFilter("Planapp.Services.RuleMonitorService", LogLevel.Information);
                builder.Logging.AddFilter("Planapp.Platforms.Android.AndroidAppLaunchMonitor", LogLevel.Information);
                builder.Logging.AddFilter("Planapp.Services.RuleBlockService", LogLevel.Information);

                var app = builder.Build();

#if ANDROID
                // Initialize Android-specific features on startup with better error handling
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000); // Wait for app to fully initialize
                        
                        var logger = app.Services.GetService<ILogger<App>>();
                        logger?.LogInformation("Starting Android initialization...");
                        
                        // Initialize notification system
                        Planapp.Platforms.Android.AndroidNotificationHelper.InitializeNotificationChannel();
                        logger?.LogInformation("Notification channel initialized");
                        
                        // Check and log permission status
                        var usageService = app.Services.GetService<IUsageStatsService>();
                        if (usageService != null)
                        {
                            var hasPermission = usageService.HasUsagePermission();
                            logger?.LogInformation($"Usage stats permission status: {hasPermission}");
                            
                            if (!hasPermission)
                            {
                                logger?.LogWarning("Usage stats permission not granted, user will need to grant it manually");
                                
                                // Show notification to request permissions
                                Planapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                                    "Permissions Required", 
                                    "Please grant usage access permission in settings"
                                );
                            }
                        }
                        
                        logger?.LogInformation("Android initialization completed successfully");
                    }
                    catch (Exception ex)
                    {
                        var logger = app.Services.GetService<ILogger<App>>();
                        logger?.LogError(ex, "Error during Android initialization: {Message}", ex.Message);
                        
                        // Show error notification
                        try
                        {
                            Planapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                                "Initialization Error", 
                                $"App initialization failed: {ex.Message}"
                            );
                        }
                        catch
                        {
                            // Ignore notification errors during error handling
                        }
                    }
                });
#endif

                return app;
            }
            catch (Exception ex)
            {
                // Log the error and create a minimal app
                System.Diagnostics.Debug.WriteLine($"Error creating MAUI app: {ex}");

                // Try to create a minimal working app
                try
                {
                    builder.Services.AddSingleton<IUsageStatsService, Services.DefaultUsageStatsService>();
                    builder.Services.AddSingleton<IRuleService, Services.DefaultRuleService>();
                    builder.Services.AddSingleton<IAppLaunchMonitor, Services.DefaultAppLaunchMonitor>();
                    builder.Services.AddSingleton<IRuleBlockService, RuleBlockService>();
                    builder.Services.AddSingleton<RuleMonitorService>();
                    builder.Services.AddMauiBlazorWebView();

                    return builder.Build();
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create minimal app: {innerEx}");
                    throw;
                }
            }
        }
    }
}