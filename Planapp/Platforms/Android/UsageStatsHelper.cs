using Android.App.Usage;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Planapp.Services;
using Android.Content.PM;

namespace Planapp.Platforms.Android
{
    public static class UsageStatsHelper
    {
        public static List<AppUsageInfo> GetDetailedAppUsage(TimeSpan timeSpan)
        {
            var context = global::Android.App.Application.Context;
            if (context?.PackageManager == null) return new List<AppUsageInfo>();

            var usageStatsManager = (UsageStatsManager?)context.GetSystemService(Context.UsageStatsService);
            if (usageStatsManager == null) return new List<AppUsageInfo>();

            var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
            var startTime = endTime - (long)timeSpan.TotalMilliseconds;

            // Use the same simple approach as the original working code
            var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
            if (stats == null || stats.Count == 0)
                return new List<AppUsageInfo>();

            var pm = context.PackageManager;
            var result = new List<AppUsageInfo>();

            // Get all apps with usage time, just like the original code
            var appsWithUsage = stats
                .Where(s => s?.TotalTimeInForeground > 0 && !string.IsNullOrEmpty(s.PackageName))
                .OrderByDescending(s => s.TotalTimeInForeground)
                .ToList();

            foreach (var s in appsWithUsage)
            {
                try
                {
                    // Get app name
                    var appName = GetAppName(pm, s.PackageName!);

                    result.Add(new AppUsageInfo
                    {
                        PackageName = s.PackageName!,
                        AppName = appName,
                        TotalTimeInForeground = s.TotalTimeInForeground,
                        IconBytes = null // Load icons lazily to improve performance
                    });
                }
                catch
                {
                    // Skip apps that can't be processed
                }
            }

            return result;
        }

        public static byte[]? GetAppIcon(string packageName)
        {
            try
            {
                var context = global::Android.App.Application.Context;
                if (context?.PackageManager == null) return null;

                var pm = context.PackageManager;
                var appInfo = pm.GetApplicationInfo(packageName, PackageInfoFlags.MetaData);
                if (appInfo == null) return null;

                var iconDrawable = pm.GetApplicationIcon(appInfo);
                return ConvertDrawableToByteArray(iconDrawable);
            }
            catch
            {
                return null;
            }
        }

        private static string GetAppName(PackageManager pm, string packageName)
        {
            try
            {
                var appInfo = pm.GetApplicationInfo(packageName, PackageInfoFlags.MetaData);
                if (appInfo == null) return FormatPackageName(packageName);

                var label = pm.GetApplicationLabel(appInfo)?.ToString();
                return !string.IsNullOrEmpty(label) ? label : FormatPackageName(packageName);
            }
            catch
            {
                return FormatPackageName(packageName);
            }
        }

        private static string FormatPackageName(string packageName)
        {
            var parts = packageName.Split('.');
            if (parts.Length > 0)
            {
                var lastPart = parts[^1];
                if (lastPart.Length > 0)
                {
                    return char.ToUpper(lastPart[0]) + lastPart[1..];
                }
            }
            return packageName;
        }

        private static byte[]? ConvertDrawableToByteArray(Drawable? drawable)
        {
            if (drawable == null) return null;

            try
            {
                Bitmap? bitmap = null;

                if (drawable is BitmapDrawable bitmapDrawable)
                {
                    bitmap = bitmapDrawable.Bitmap;
                }
                else
                {
                    // Create a standard 64x64 bitmap for consistent sizing
                    bitmap = Bitmap.CreateBitmap(64, 64, Bitmap.Config.Argb8888!);
                    var canvas = new Canvas(bitmap);
                    drawable.SetBounds(0, 0, 64, 64);
                    drawable.Draw(canvas);
                }

                if (bitmap == null) return null;

                using var stream = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png!, 85, stream);
                return stream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public static void OpenUsageAccessSettings()
        {
            var context = global::Android.App.Application.Context;
            if (context == null) return;

            try
            {
                var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch
            {
                try
                {
                    var fallbackIntent = new Intent(global::Android.Provider.Settings.ActionSettings);
                    fallbackIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(fallbackIntent);
                }
                catch
                {
                    // Ignore if settings can't be opened
                }
            }
        }
    }
}