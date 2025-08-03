using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Planapp.Services;
using Android.App.Usage;
using Android.Content;
using Android.App;
using Android.OS;

namespace Planapp.Platforms.Android
{
    public class UsageStatsServiceImpl : IUsageStatsService
    {
        public async Task<List<AppUsageInfo>> GetAppUsageAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Get usage since start of today instead of rolling 24h
                    var today = DateTime.Today;
                    var timeSpan = DateTime.Now - today;

                    var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                    if (context == null) return new List<AppUsageInfo>();

                    var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                    if (usageStatsManager == null) return new List<AppUsageInfo>();

                    var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                    var startTime = endTime - (long)timeSpan.TotalMilliseconds;

                    var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                    if (stats == null || stats.Count == 0)
                        return new List<AppUsageInfo>();

                    return stats
                        .Where(s => s != null && s.TotalTimeInForeground > 0 && !string.IsNullOrEmpty(s.PackageName))
                        .Select(s => new AppUsageInfo
                        {
                            PackageName = s.PackageName ?? "Unknown",
                            TotalTimeInForeground = s.TotalTimeInForeground,
                            AppName = UsageStatsHelper.GetAppName(s.PackageName ?? "")
                        })
                        .OrderByDescending(s => s.TotalTimeInForeground)
                        .ToList();
                }
                catch (Exception)
                {
                    return new List<AppUsageInfo>();
                }
            });
        }

        public void RequestUsageAccess()
        {
            try
            {
                UsageStatsHelper.OpenUsageAccessSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening usage settings: {ex.Message}");
            }
        }

        public bool HasUsagePermission()
        {
            try
            {
                var context = global::Android.App.Application.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("Context is null");
                    return false;
                }

                // Check AppOpsManager permission
                var appOps = (AppOpsManager?)context.GetSystemService(Context.AppOpsService);
                if (appOps == null)
                {
                    System.Diagnostics.Debug.WriteLine("AppOpsManager is null");
                    return false;
                }

                var mode = appOps.CheckOpNoThrow(
                    AppOpsManager.OpstrGetUsageStats!,
                    Process.MyUid(),
                    context.PackageName!);

                if (mode != AppOpsManagerMode.Allowed)
                {
                    System.Diagnostics.Debug.WriteLine($"Usage stats permission not allowed: {mode}");
                    return false;
                }

                // Verify we can actually get usage stats
                var usageStatsManager = (UsageStatsManager?)context.GetSystemService(Context.UsageStatsService);
                if (usageStatsManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("UsageStatsManager is null");
                    return false;
                }

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 24); // 24 hours

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                var hasPermission = stats != null && stats.Count > 0;
                System.Diagnostics.Debug.WriteLine($"Usage permission check result: {hasPermission}");

                return hasPermission;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking usage permission: {ex.Message}");
                return false;
            }
        }
    }
}