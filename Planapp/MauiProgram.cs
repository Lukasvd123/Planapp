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
#else
            builder.Services.AddSingleton<IUsageStatsService, Services.DefaultUsageStatsService>();
#endif

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}