using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Planapp.Services;
using Planapp.Platforms.Android;

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
    }
}
