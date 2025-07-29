using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Planapp.Services
{
    public class DefaultUsageStatsService : IUsageStatsService
    {
        public Task<List<AppUsageInfo>> GetAppUsageAsync()
        {
            return Task.FromResult(new List<AppUsageInfo>());
        }

        public bool HasUsagePermission()
        {
            return false;
        }

        public void RequestUsageAccess()
        {
            // No-op for non-Android platforms
        }
    }
}