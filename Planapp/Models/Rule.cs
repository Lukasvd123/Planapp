using System;
using System.Collections.Generic;

namespace com.usagemeter.androidapp.Models
{
    public class AppRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<string> SelectedPackages { get; set; } = new();
        public List<string> SelectedAppNames { get; set; } = new();
        public int ThresholdHours { get; set; } = 0;
        public int ThresholdMinutes { get; set; } = 30;
        public string ActionType { get; set; } = "OpenApp"; // "OpenApp" or "LockInApp"
        public string TargetPackage { get; set; } = string.Empty;
        public string TargetAppName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime LastTriggered { get; set; }

        public long ThresholdInMilliseconds => (ThresholdHours * 60 + ThresholdMinutes) * 60 * 1000L;
    }

    public class AppInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string IconBase64 { get; set; } = string.Empty;
        public bool HasIcon { get; set; } = false;
    }
}