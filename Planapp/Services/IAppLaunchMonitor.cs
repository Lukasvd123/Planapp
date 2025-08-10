using System;
using System.Threading.Tasks;

namespace com.usagemeter.androidapp.Services
{
    public interface IAppLaunchMonitor
    {
        event EventHandler<AppLaunchEventArgs>? AppLaunched;

        Task StartMonitoringAsync();
        Task StopMonitoringAsync();

        bool IsMonitoring { get; }
    }

    public class AppLaunchEventArgs : EventArgs
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public DateTime LaunchedAt { get; set; } = DateTime.Now;
    }
}