using System;
using System.Threading.Tasks;

namespace com.usagemeter.androidapp.Services
{
    public class DefaultAppLaunchMonitor : IAppLaunchMonitor
    {
        public event EventHandler<AppLaunchEventArgs>? AppLaunched;

        public bool IsMonitoring { get; private set; }

        public async Task StartMonitoringAsync()
        {
            IsMonitoring = true;
            await Task.CompletedTask;
        }

        public async Task StopMonitoringAsync()
        {
            IsMonitoring = false;
            await Task.CompletedTask;
        }
    }
}