using Android.App.Usage;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Planapp.Platforms.Android
{
  
    public static List<AppUsageInfo> GetDetailedAppUsage(TimeSpan timeSpan)
        {
            var context = global::Android.App.Application.Context;
            var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
            var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
            var startTime = endTime - (long)timeSpan.TotalMilliseconds;

            var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
            if (stats == null || stats.Count == 0)
                return new();

            var pm = context.PackageManager;

            var result = new List<AppUsageInfo>();
            foreach (var s in stats.Where(s => s.TotalTimeInForeground > 0))
            {
                try
                {
                    var appInfo = pm.GetApplicationInfo(s.PackageName, 0);
                    var label = pm.GetApplicationLabel(appInfo)?.ToString() ?? s.PackageName;
                    var iconDrawable = pm.GetApplicationIcon(appInfo);
                    using var bitmap = ((BitmapDrawable)iconDrawable).Bitmap;
                    using var ms = new MemoryStream();
                    bitmap.Compress(Bitmap.CompressFormat.Png, 100, ms);
                    var iconBytes = ms.ToArray();

                    result.Add(new AppUsageInfo
                    {
                        PackageName = s.PackageName,
                        AppName = label,
                        TotalTimeInForeground = s.TotalTimeInForeground,
                        IconBytes = iconBytes
                    });
                }
                catch
                {
                    // Ignore missing apps or permissions
                }
            }

            return result.OrderByDescending(a => a.TotalTimeInForeground).ToList();
        }
    public static class UsageStatsHelper
    {
        public static List<AndroidAppUsageInfo> GetAppUsage(TimeSpan timeSpan)
        {
            var usageStatsManager = (UsageStatsManager)global::Android.App.Application.Context.GetSystemService(Context.UsageStatsService);
            var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
            var startTime = endTime - (long)timeSpan.TotalMilliseconds;

            var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

            if (stats == null || stats.Count == 0)
                return new();

            return stats
                .Where(s => s.TotalTimeInForeground > 0)
                .Select(s => new AndroidAppUsageInfo
                {
                    PackageName = s.PackageName ?? "Unknown",
                    TotalTimeInForeground = s.TotalTimeInForeground
                })
                .ToList();
        }

        public static void OpenUsageAccessSettings()
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
        }
    }
}
