using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.PM;
using Planapp.Models;
using Planapp.Services;

namespace Planapp.Platforms.Android
{
    public class AndroidRuleService : DefaultRuleService
    {
        public override async Task<List<Models.AppInfo>> GetAllAppsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                    if (context == null) return new List<Models.AppInfo>();

                    var packageManager = context.PackageManager;
                    if (packageManager == null) return new List<Models.AppInfo>();

                    var installedApps = packageManager.GetInstalledApplications(PackageInfoFlags.MetaData);

                    var appList = new List<Models.AppInfo>();

                    foreach (var app in installedApps)
                    {
                        try
                        {
                            // Skip system apps that users typically don't interact with
                            if ((app.Flags & ApplicationInfoFlags.System) != 0 &&
                                (app.Flags & ApplicationInfoFlags.UpdatedSystemApp) == 0)
                                continue;

                            var appName = packageManager.GetApplicationLabel(app)?.ToString() ?? app.PackageName ?? "";
                            var iconBase64 = UsageStatsHelper.GetAppIcon(app.PackageName ?? "");

                            appList.Add(new Models.AppInfo
                            {
                                PackageName = app.PackageName ?? "",
                                AppName = appName,
                                IconBase64 = iconBase64,
                                HasIcon = !string.IsNullOrEmpty(iconBase64)
                            });
                        }
                        catch
                        {
                            // Skip apps that cause errors
                            continue;
                        }
                    }

                    return appList.OrderBy(a => a.AppName).ToList();
                }
                catch
                {
                    return new List<Models.AppInfo>();
                }
            });
        }

        public override async Task<long> GetCombinedUsageForAppsAsync(List<string> packageNames)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Get usage since start of today instead of last 24 hours
                    var today = DateTime.Today;
                    var timeSpan = DateTime.Now - today;

                    var usageStats = UsageStatsHelper.GetDetailedAppUsage(timeSpan);

                    return usageStats
                        .Where(u => packageNames.Contains(u.PackageName))
                        .Sum(u => u.TotalTimeInForeground);
                }
                catch
                {
                    return 0L;
                }
            });
        }
    }
}