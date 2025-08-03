using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Planapp.Platforms.Android
{
    public class AndroidAppUsageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public long TotalTimeInForeground { get; set; }
        public string AppName { get; set; } = string.Empty;
    }

    public class DetailedAppUsageInfo : AndroidAppUsageInfo
    {
        public int LaunchCount { get; set; }
        public DateTime FirstTimeStamp { get; set; }
        public DateTime LastTimeStamp { get; set; }
    }

    public static class UsageStatsHelper
    {
        private const int ICON_SIZE = 48;
        private static readonly ConcurrentDictionary<string, string> IconCache = new();
        private static readonly ConcurrentDictionary<string, string> NameCache = new();
        // Add this method to the existing UsageStatsHelper class

        // Add this method to your existing UsageStatsHelper class

        public static List<DetailedAppUsageInfo> GetDetailedAppUsage(TimeSpan timeSpan)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null) return new List<DetailedAppUsageInfo>();

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return new List<DetailedAppUsageInfo>();

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (long)timeSpan.TotalMilliseconds;

                var rawStats = usageStatsManager.QueryUsageStats(
                                   UsageStatsInterval.Daily,
                                   startTime,
                                   endTime);

                if (rawStats is null || rawStats.Count == 0)
                    return new List<DetailedAppUsageInfo>();

                // Filter → GroupBy → pick the entry with the max usage
                var result = rawStats
                    .Where(s => s != null
                                && !string.IsNullOrEmpty(s.PackageName)
                                && s.TotalTimeInForeground > 0)
                    .GroupBy(s => s.PackageName!)                  // group per package
                    .Select(g =>
                    {
                        // pick the one with the largest foreground-time
                        var best = g.OrderByDescending(x => x.TotalTimeInForeground)
                                     .First();

                        return new DetailedAppUsageInfo
                        {
                            PackageName = best.PackageName!,
                            TotalTimeInForeground = best.TotalTimeInForeground,
                            FirstTimeStamp = DateTimeOffset
                                                     .FromUnixTimeMilliseconds(best.FirstTimeStamp)
                                                     .DateTime,
                            LastTimeStamp = DateTimeOffset
                                                     .FromUnixTimeMilliseconds(best.LastTimeStamp)
                                                     .DateTime,
                            AppName = GetAppName(best.PackageName!)
                        };
                    })
                    .OrderByDescending(info => info.TotalTimeInForeground)
                    .ToList();

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetDetailedAppUsage] {ex}");
                return new List<DetailedAppUsageInfo>();
            }
        }
        public static List<DetailedAppUsageInfo> GetTodayAppUsage()
        {
            try
            {
                var today = DateTime.Today;
                var timeSpan = DateTime.Now - today;

                return GetDetailedAppUsage(timeSpan);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetTodayAppUsage] {ex}");
                return new List<DetailedAppUsageInfo>();
            }
        }
        public static List<AndroidAppUsageInfo> GetAppUsage(TimeSpan timeSpan)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null) return new List<AndroidAppUsageInfo>();

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return new List<AndroidAppUsageInfo>();

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (long)timeSpan.TotalMilliseconds;

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                if (stats == null || stats.Count == 0)
                    return new List<AndroidAppUsageInfo>();

                return stats
                    .Where(s => s != null && s.TotalTimeInForeground > 0 && !string.IsNullOrEmpty(s.PackageName))
                    .Select(s => new AndroidAppUsageInfo
                    {
                        PackageName = s.PackageName ?? "Unknown",
                        TotalTimeInForeground = s.TotalTimeInForeground,
                        AppName = GetAppName(s.PackageName ?? "")
                    })
                    .OrderByDescending(s => s.TotalTimeInForeground)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<AndroidAppUsageInfo>();
            }
        }

     
        public static string GetAppIcon(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return string.Empty;

            if (IconCache.TryGetValue(packageName, out var inCache))
                return inCache;                     // may be empty string (= not found)

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext
               ?? global::Android.App.Application.Context;
                var pm = context.PackageManager;

                // -- one happy path --------------------------------------------------
                var appInfo = pm.GetApplicationInfo(packageName, 0);
                var drawable = pm.GetApplicationIcon(appInfo);

                using var bitmap = (drawable as BitmapDrawable)?.Bitmap
                                   ?? DrawableToBitmap(drawable);

                if (bitmap == null)
                {
                    IconCache.TryAdd(packageName, string.Empty);
                    return string.Empty;
                }

                // Encode to PNG ------------------------------------------------------
                using var ms = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png, 100, ms);
                var base64 = Convert.ToBase64String(ms.ToArray());

                IconCache.TryAdd(packageName, base64);
                return base64;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAppIcon] {packageName} → {ex.Message}");
                IconCache.TryAdd(packageName, string.Empty);
                return string.Empty;
            }
        }

     // already defined in the class

        private static Bitmap? DrawableToBitmap(Drawable drawable)
        {
            // If it is already a bitmap → scale + return
            if (drawable is BitmapDrawable bd && bd.Bitmap is not null)
            {
                return Bitmap.CreateScaledBitmap(bd.Bitmap,
                                                 ICON_SIZE, ICON_SIZE, true);
            }

            // Vector / adaptive icon → draw on canvas
            var bmp = Bitmap.CreateBitmap(ICON_SIZE, ICON_SIZE,
                                          Bitmap.Config.Argb8888);
            var canvas = new Canvas(bmp);
            drawable.SetBounds(0, 0, ICON_SIZE, ICON_SIZE);
            drawable.Draw(canvas);
            return bmp;
        }
        private static string GetIconWithFallbacks(PackageManager packageManager, string packageName)
        {
            var methods = new Func<Drawable?>[]
            {
                // Method 1: Standard application icon
                () => {
                    try
                    {
                        var appInfo = packageManager.GetApplicationInfo(packageName, 0);
                        return packageManager.GetApplicationIcon(appInfo);
                    }
                    catch { return null; }
                },

                // Method 2: Direct package manager call
                () => {
                    try
                    {
                        return packageManager.GetApplicationIcon(packageName);
                    }
                    catch { return null; }
                },

                // Method 3: Launch intent icon
                () => {
                    try
                    {
                        var intent = packageManager.GetLaunchIntentForPackage(packageName);
                        if (intent?.Component != null)
                        {
                            var activityInfo = packageManager.GetActivityInfo(intent.Component, 0);
                            return activityInfo.LoadIcon(packageManager);
                        }
                    }
                    catch { }
                    return null;
                },

                // Method 4: Query all activities and get first icon
                () => {
                    try
                    {
                        var intent = new Intent(Intent.ActionMain);
                        intent.SetPackage(packageName);
                        var activities = packageManager.QueryIntentActivities(intent, 0);
                        var firstActivity = activities?.FirstOrDefault();
                        return firstActivity?.LoadIcon(packageManager);
                    }
                    catch { }
                    return null;
                },

                // Method 5: Get default activity icon from package info
                () => {
                    try
                    {
                        var packageInfo = packageManager.GetPackageInfo(packageName, PackageInfoFlags.Activities);
                        var firstActivity = packageInfo.Activities?.FirstOrDefault();
                        if (firstActivity != null)
                        {
                            return packageManager.GetActivityIcon(new ComponentName(packageName, firstActivity.Name));
                        }
                    }
                    catch { }
                    return null;
                }
            };

            foreach (var method in methods)
            {
                try
                {
                    var drawable = method();
                    if (drawable != null)
                    {
                        var bitmap = ConvertDrawableToBitmap(drawable);
                        if (bitmap != null)
                        {
                            using var stream = new MemoryStream();
                            bitmap.Compress(Bitmap.CompressFormat.Png!, 85, stream);
                            var bytes = stream.ToArray();

                            if (bitmap != (drawable as BitmapDrawable)?.Bitmap)
                            {
                                bitmap.Recycle();
                            }

                            return Convert.ToBase64String(bytes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback method failed for {packageName}: {ex.Message}");
                    continue;
                }
            }

            return "";
        }

        private static Bitmap? ConvertDrawableToBitmap(Drawable drawable)
        {
            try
            {
                if (drawable is BitmapDrawable bitmapDrawable && bitmapDrawable.Bitmap != null)
                {
                    return Bitmap.CreateScaledBitmap(bitmapDrawable.Bitmap, ICON_SIZE, ICON_SIZE, true);
                }

                var bitmap = Bitmap.CreateBitmap(ICON_SIZE, ICON_SIZE, Bitmap.Config.Argb8888!);
                var canvas = new Canvas(bitmap);
                drawable.SetBounds(0, 0, ICON_SIZE, ICON_SIZE);
                drawable.Draw(canvas);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static string GetAppName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return packageName;

            // Local cache first -----------------------------------------------------
            if (NameCache.TryGetValue(packageName, out var cached))
                return cached;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext
              ?? global::Android.App.Application.Context;
                var pm = context.PackageManager;
                // MetaData flag keeps it compatible with API-24 – 34
                var appInfo = pm.GetApplicationInfo(packageName,
                                                    PackageInfoFlags.MetaData);
                var label = pm.GetApplicationLabel(appInfo)?.ToString() ?? packageName;

                NameCache.TryAdd(packageName, label);
                return label;
            }
            catch (PackageManager.NameNotFoundException)
            {
                // Package removed between the time we got usage stats and now
                return packageName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAppName] {packageName} → {ex.Message}");
                return packageName;
            }
        }
        private static string GetNameWithFallbacks(PackageManager packageManager, string packageName)
        {
            var methods = new Func<string?>[]
            {
                // Method 1: Application label from app info
                () => {
                    try
                    {
                        var appInfo = packageManager.GetApplicationInfo(packageName, 0);
                        return packageManager.GetApplicationLabel(appInfo)?.ToString();
                    }
                    catch { return null; }
                },

                // Method 2: Load label from package info
                () => {
                    try
                    {
                        var packageInfo = packageManager.GetPackageInfo(packageName, 0);
                        return packageInfo.ApplicationInfo?.LoadLabel(packageManager)?.ToString();
                    }
                    catch { return null; }
                },

                // Method 3: Activity label from launch intent
                () => {
                    try
                    {
                        var intent = packageManager.GetLaunchIntentForPackage(packageName);
                        if (intent?.Component != null)
                        {
                            var activityInfo = packageManager.GetActivityInfo(intent.Component, 0);
                            return activityInfo.LoadLabel(packageManager)?.ToString();
                        }
                    }
                    catch { }
                    return null;
                }
            };

            foreach (var method in methods)
            {
                try
                {
                    var name = method();
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return packageName;
        }

        public static void ClearCache()
        {
            IconCache.Clear();
            NameCache.Clear();
        }

        public static void OpenUsageAccessSettings()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null) return;

                var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch (Exception)
            {
                // Settings couldn't be opened
            }
        }

        public static bool HasUsagePermission()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null) return false;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return false;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (60 * 60 * 1000);

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                return stats != null && stats.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}