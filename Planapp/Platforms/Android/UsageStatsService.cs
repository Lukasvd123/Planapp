using Android.App.Usage;
using Android.Content;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Planapp.Platforms.Android
{
    public class AndroidAppUsageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public long TotalTimeInForeground { get; set; }
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
                return new List<AndroidAppUsageInfo>();

            return stats
                .Where(s => s.TotalTimeInForeground > 0)
                .Select(s => new AndroidAppUsageInfo
                {
                    PackageName = s.PackageName ?? "Unknown",
                    TotalTimeInForeground = s.TotalTimeInForeground
                })
                .OrderByDescending(s => s.TotalTimeInForeground)
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
