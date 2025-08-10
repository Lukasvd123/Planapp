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
using AndroidApp = Android.App.Application;
using Paint = Android.Graphics.Paint;

namespace com.usagemeter.androidapp.Platforms.Android
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
        private const int ICON_SIZE_SMALL = 48;
        private const int ICON_SIZE_LARGE = 128; // Higher quality icons
        private static readonly ConcurrentDictionary<string, string> IconCache = new();
        private static readonly ConcurrentDictionary<string, string> NameCache = new();

        public static List<DetailedAppUsageInfo> GetDetailedAppUsage(TimeSpan timeSpan)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
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
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
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

        public static string GetAppIcon(string packageName, bool highQuality = false)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return string.Empty;

            var cacheKey = highQuality ? $"{packageName}_hq" : packageName;
            if (IconCache.TryGetValue(cacheKey, out var inCache))
                return inCache;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                var pm = context.PackageManager;

                var appInfo = pm.GetApplicationInfo(packageName, 0);
                var drawable = pm.GetApplicationIcon(appInfo);

                var targetSize = highQuality ? ICON_SIZE_LARGE : ICON_SIZE_SMALL;
                using var bitmap = (drawable as BitmapDrawable)?.Bitmap
                                   ?? DrawableToBitmap(drawable, targetSize);

                if (bitmap == null)
                {
                    IconCache.TryAdd(cacheKey, string.Empty);
                    return string.Empty;
                }

                // Encode to PNG with higher quality for large icons
                using var ms = new MemoryStream();
                var quality = highQuality ? 100 : 85;
                bitmap.Compress(Bitmap.CompressFormat.Png, quality, ms);
                var base64 = Convert.ToBase64String(ms.ToArray());

                IconCache.TryAdd(cacheKey, base64);
                return base64;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAppIcon] {packageName} → {ex.Message}");
                IconCache.TryAdd(cacheKey, string.Empty);
                return string.Empty;
            }
        }

        private static Bitmap? DrawableToBitmap(Drawable drawable, int size = ICON_SIZE_SMALL)
        {
            // If it is already a bitmap → scale + return
            if (drawable is BitmapDrawable bd && bd.Bitmap is not null)
            {
                return Bitmap.CreateScaledBitmap(bd.Bitmap, size, size, true);
            }

            // Vector / adaptive icon → draw on canvas with anti-aliasing
            var bmp = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888);
            var canvas = new Canvas(bmp);

            // Enable anti-aliasing for smoother icons
            var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            canvas.DrawPaint(paint);

            drawable.SetBounds(0, 0, size, size);
            drawable.Draw(canvas);
            return bmp;
        }

        public static string GetAppName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return packageName;

            // Local cache first
            if (NameCache.TryGetValue(packageName, out var cached))
                return cached;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                var pm = context.PackageManager;
                var appInfo = pm.GetApplicationInfo(packageName, PackageInfoFlags.MetaData);
                var label = pm.GetApplicationLabel(appInfo)?.ToString() ?? packageName;

                NameCache.TryAdd(packageName, label);
                return label;
            }
            catch (PackageManager.NameNotFoundException)
            {
                return packageName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAppName] {packageName} → {ex.Message}");
                return packageName;
            }
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
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
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
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
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