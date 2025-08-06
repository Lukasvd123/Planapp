using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Planapp.Services;
using Android.App.Usage;
using Android.Content;
using Android.App;
using Android.OS;
using AndroidApp = Android.App.Application;

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
                    if (!HasUsagePermission())
                    {
                        System.Diagnostics.Debug.WriteLine("No usage permission, returning empty list");
                        return new List<AppUsageInfo>();
                    }

                    // Get usage since start of today instead of rolling 24h
                    var today = DateTime.Today;
                    var timeSpan = DateTime.Now - today;

                    var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                    if (context == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Context is null");
                        return new List<AppUsageInfo>();
                    }

                    var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                    if (usageStatsManager == null)
                    {
                        System.Diagnostics.Debug.WriteLine("UsageStatsManager is null");
                        return new List<AppUsageInfo>();
                    }

                    var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                    var startTime = endTime - (long)timeSpan.TotalMilliseconds;

                    System.Diagnostics.Debug.WriteLine($"Querying usage stats from {DateTimeOffset.FromUnixTimeMilliseconds(startTime)} to {DateTimeOffset.FromUnixTimeMilliseconds(endTime)}");

                    var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                    if (stats == null)
                    {
                        System.Diagnostics.Debug.WriteLine("QueryUsageStats returned null");
                        return new List<AppUsageInfo>();
                    }

                    System.Diagnostics.Debug.WriteLine($"QueryUsageStats returned {stats.Count} entries");

                    var result = stats
                        .Where(s => s != null && s.TotalTimeInForeground > 0 && !string.IsNullOrEmpty(s.PackageName))
                        .Where(s => s.PackageName != "com.usagemeter.androidapp") // Don't include our own app
                        .GroupBy(s => s.PackageName) // Group by package in case of duplicates
                        .Select(g => g.OrderByDescending(s => s.TotalTimeInForeground).First()) // Take the one with max usage
                        .Select(s => new AppUsageInfo
                        {
                            PackageName = s.PackageName ?? "Unknown",
                            TotalTimeInForeground = s.TotalTimeInForeground,
                            AppName = UsageStatsHelper.GetAppName(s.PackageName ?? "")
                        })
                        .OrderByDescending(s => s.TotalTimeInForeground)
                        .ToList();

                    System.Diagnostics.Debug.WriteLine($"Returning {result.Count} filtered apps with usage");
                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in GetAppUsageAsync: {ex}");
                    return new List<AppUsageInfo>();
                }
            });
        }

        public void RequestUsageAccess()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Requesting usage access");

                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("Context is null, cannot request usage access");
                    return;
                }

                var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
                intent.AddFlags(ActivityFlags.NewTask);

                // Try to go directly to our app's settings if possible
                try
                {
                    intent.SetData(global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
                }
                catch
                {
                    // Fallback to general usage access settings
                    System.Diagnostics.Debug.WriteLine("Cannot navigate directly to app settings, using general settings");
                }

                context.StartActivity(intent);
                System.Diagnostics.Debug.WriteLine("Successfully opened usage access settings");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening usage settings: {ex.Message}");

                // Show notification as fallback
                try
                {
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Manual Setup Required",
                        "Please go to Settings > Apps > Special access > Usage access and enable this app"
                    );
                }
                catch
                {
                    // Last resort fallback
                }
            }
        }

        public bool HasUsagePermission()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("Context is null for permission check");
                    return false;
                }

                // Check AppOpsManager permission first
                var appOps = context.GetSystemService(Context.AppOpsService) as AppOpsManager;
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

                // Double-check by trying to get actual usage stats
                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("UsageStatsManager is null");
                    return false;
                }

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 24); // 24 hours

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                var hasPermission = stats != null && stats.Count > 0;
                System.Diagnostics.Debug.WriteLine($"Final usage permission check result: {hasPermission} (found {stats?.Count ?? 0} apps)");

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