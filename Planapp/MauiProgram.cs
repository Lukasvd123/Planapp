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
            // Initialize Android-specific features on startup
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait for app to fully initialize
                    
                    // Initialize notification system
                    Planapp.Platforms.Android.AndroidNotificationHelper.InitializeNotificationChannel();
                    
                    // Check and request permissions
                    var usageService = app.Services.GetService<IUsageStatsService>();
                    if (usageService != null && !usageService.HasUsagePermission())
                    {
                        Microsoft.Extensions.Logging.ILogger? logger = app.Services.GetService<ILogger<App>>();
                        logger?.LogInformation("Usage stats permission not granted, user will need to grant it manually");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during Android initialization: {ex.Message}");
                }
            });
#endif

            return app;
        }
    }
}