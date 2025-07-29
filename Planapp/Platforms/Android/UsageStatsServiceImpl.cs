using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Planapp.Services;
using Android.App.Usage;
using Android.Content;
using Android.App;
using Android.OS;

[assembly: Microsoft.Maui.Controls.Dependency(typeof(Planapp.Platforms.Android.UsageStatsServiceImpl))]
namespace Planapp.Platforms.Android
{
    public class UsageStatsServiceImpl : IUsageStatsService
    {
        public Task<List<AppUsageInfo>> GetAppUsageAsync()
        {
            var androidList = UsageStatsHelper.GetDetailedAppUsage(TimeSpan.FromDays(1));
            return Task.FromResult(androidList);
        }

        public void RequestUsageAccess()
        {
            UsageStatsHelper.OpenUsageAccessSettings();
        }

        public bool HasUsagePermission()
        {
            try
            {
                var context = global::Android.App.Application.Context;
                if (context == null) return false;

                // Check if we have usage stats permission
                var appOps = (AppOpsManager?)context.GetSystemService(Context.AppOpsService);
                if (appOps == null) return false;

                var mode = appOps.CheckOpNoThrow(
                    AppOpsManager.OpstrGetUsageStats!,
                    Process.MyUid(),
                    context.PackageName!);

                if (mode != AppOpsManagerMode.Allowed)
                    return false;

                // Double check by trying to actually get usage stats
                var usageStatsManager = (UsageStatsManager?)context.GetSystemService(Context.UsageStatsService);
                if (usageStatsManager == null) return false;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 24); // 24 hours ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                // If we get stats back and it's not empty, we have permission
                return stats != null && stats.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}