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
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Add Blazor WebView
            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
            builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

            // Core Services - Order matters for dependency injection
            builder.Services.AddSingleton<ISettingsService, SettingsService>();

            // Platform-specific services
#if ANDROID
            builder.Services.AddSingleton<IRuleService, AndroidRuleService>();
            builder.Services.AddSingleton<IUsageStatsService, UsageStatsServiceImpl>();
            builder.Services.AddSingleton<IAppLaunchMonitor, AndroidAppLaunchMonitor>();
#else
            builder.Services.AddSingleton<IRuleService, DefaultRuleService>();
            builder.Services.AddSingleton<IUsageStatsService, DefaultUsageStatsService>();
            builder.Services.AddSingleton<IAppLaunchMonitor, DefaultAppLaunchMonitor>();
#endif

            // Business logic services - depend on platform services
            builder.Services.AddSingleton<IRuleBlockService, RuleBlockService>();
            builder.Services.AddSingleton<RuleMonitorService>();

            var app = builder.Build();

            // Initialize services after building
#if ANDROID
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait for app to fully initialize

                    var serviceProvider = app.Services;
                    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("MauiProgram");
                    var settings = serviceProvider.GetRequiredService<ISettingsService>();

                    logger.LogInformation("MAUI app initialized successfully");

                    // Load settings to ensure they're cached
                    var appSettings = await settings.GetSettingsAsync();
                    logger.LogInformation($"Settings loaded - Rules enabled: {appSettings.AllRulesEnabled}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in post-init: {ex}");
                }
            });
#endif

            return app;
        }
    }
}