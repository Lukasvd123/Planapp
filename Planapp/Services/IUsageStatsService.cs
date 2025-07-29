using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Planapp.Services
{
    public class AppUsageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public long TotalTimeInForeground { get; set; }
        public byte[]? IconBytes { get; set; }
    }

    public interface IUsageStatsService
    {
        Task<List<AppUsageInfo>> GetAppUsageAsync();
        void RequestUsageAccess();
        bool HasUsagePermission();
    }
}