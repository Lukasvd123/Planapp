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

#if ANDROID
            builder.Services.AddSingleton<IUsageStatsService, Platforms.Android.UsageStatsServiceImpl>();
            builder.Services.AddSingleton<IRuleService, Platforms.Android.AndroidRuleService>();
            builder.Services.AddSingleton<IAppLaunchMonitor, Platforms.Android.AndroidAppLaunchMonitor>();
#else
            builder.Services.AddSingleton<IUsageStatsService, Services.DefaultUsageStatsService>();
            builder.Services.AddSingleton<IRuleService, Services.DefaultRuleService>();
            builder.Services.AddSingleton<IAppLaunchMonitor, Services.DefaultAppLaunchMonitor>();
#endif

            // Add rule blocking service
            builder.Services.AddSingleton<IRuleBlockService, RuleBlockService>();

            // Add rule monitor service (manual start)
            builder.Services.AddSingleton<RuleMonitorService>();

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}