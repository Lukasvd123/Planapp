using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App.Usage;
using Android.Content;
using Microsoft.Extensions.Logging;
using Planapp.Services;

namespace Planapp.Platforms.Android
{
    public class AndroidAppLaunchMonitor : IAppLaunchMonitor
    {
        private readonly ILogger<AndroidAppLaunchMonitor> _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly HashSet<string> _recentLaunches = new();
        private DateTime _lastCheckTime = DateTime.Now;
        private string _lastForegroundApp = string.Empty;

        public event EventHandler<AppLaunchEventArgs>? AppLaunched;
        public bool IsMonitoring { get; private set; }

        public AndroidAppLaunchMonitor(ILogger<AndroidAppLaunchMonitor> logger)
        {
            _logger = logger;
        }

        // Simplified member names as per IDE0037 diagnostic suggestion
        public async Task StartMonitoringAsync()
        {
            if (IsMonitoring)
            {
                _logger.LogInformation("App launch monitoring already started");
                return;
            }

            _logger.LogInformation("Starting app launch monitoring");

            _cancellationTokenSource = new CancellationTokenSource();
            IsMonitoring = true;
            _lastCheckTime = DateTime.Now.AddMinutes(-1); // Look back 1 minute initially

            // Start monitoring task
            _ = Task.Run(() => MonitorAppLaunches(_cancellationTokenSource.Token));

            await Task.CompletedTask;
        }

        public async Task StopMonitoringAsync()
        {
            if (!IsMonitoring)
            {
                _logger.LogInformation("App launch monitoring already stopped");
                return;
            }

            _logger.LogInformation("Stopping app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _recentLaunches.Clear();

            await Task.CompletedTask;
        }

        private async Task MonitorAppLaunches(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForAppLaunches();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking for app launches");
                    }

                    // Check every 1.5 seconds for new app launches
                    await Task.Delay(1500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("App launch monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "App launch monitoring error");
            }
            finally
            {
                IsMonitoring = false;
            }
        }

        private async Task CheckForAppLaunches()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    _logger.LogWarning("Android context not available for app launch monitoring");
                    return;
                }

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null)
                {
                    _logger.LogWarning("UsageStatsManager not available");
                    return;
                }

                var currentTime = DateTime.Now;
                var checkPeriod = currentTime - _lastCheckTime;

                // Only check if enough time has passed
                if (checkPeriod.TotalSeconds < 1) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (long)checkPeriod.TotalMilliseconds - 2000; // Extra 2 seconds buffer

                // Get usage events to detect app launches
                var events = usageStatsManager.QueryEvents(startTime, endTime);

                if (events == null)
                {
                    _logger.LogDebug("No usage events returned");
                    _lastCheckTime = currentTime;
                    return;
                }

                var appLaunches = new List<(string PackageName, long TimeStamp, int EventType)>();
                var eventObj = new UsageEvents.Event();

                while (events.HasNextEvent)
                {
                    events.GetNextEvent(eventObj);

                    // Log all events for debugging
                    if (eventObj.EventType is (UsageEventType)(int)UsageEventType.ActivityResumed or
                        (UsageEventType)(int)UsageEventType.MoveToForeground)
                    {
                        appLaunches.Add(((string PackageName, long TimeStamp, int EventType))(
                            PackageName: eventObj.PackageName ?? "",
                            TimeStamp: eventObj.TimeStamp,
                            EventType: eventObj.EventType
                        ));
                    }
                }

                // Process recent app launches
                var uniqueLaunches = appLaunches
                    .Where(e => !string.IsNullOrEmpty(e.PackageName))
                    .Where(e => e.PackageName != "com.companyname.planapp") // Don't monitor our own app
                    .GroupBy(e => e.PackageName)
                    .Select(g => g.OrderByDescending(e => e.TimeStamp).First())
                    .OrderByDescending(e => e.TimeStamp)
                    .ToList();

                _logger.LogDebug($"Found {uniqueLaunches.Count} unique app launches in the last {checkPeriod.TotalSeconds:F1} seconds");

                foreach (var launch in uniqueLaunches)
                {
                    var packageName = launch.PackageName;
                    var launchKey = $"{packageName}_{launch.TimeStamp}";

                    if (_recentLaunches.Contains(launchKey)) continue;

                    // Check if this is a different app than what was previously foreground
                    if (packageName == _lastForegroundApp) continue;

                    _recentLaunches.Add(launchKey);
                    _lastForegroundApp = packageName;

                    // Clean old entries to prevent memory leak
                    if (_recentLaunches.Count > 50)
                    {
                        var oldEntries = _recentLaunches.Take(25).ToList();
                        foreach (var old in oldEntries)
                        {
                            _recentLaunches.Remove(old);
                        }
                    }

                    var appName = UsageStatsHelper.GetAppName(packageName);

                    _logger.LogInformation($"App launched: {appName} ({packageName}) at {DateTimeOffset.FromUnixTimeMilliseconds(launch.TimeStamp):HH:mm:ss}");

                    // Fire the event
                    AppLaunched?.Invoke(this, new AppLaunchEventArgs
                    {
                        PackageName = packageName,
                        AppName = appName,
                        LaunchedAt = DateTimeOffset.FromUnixTimeMilliseconds(launch.TimeStamp).DateTime
                    });
                }

                _lastCheckTime = currentTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckForAppLaunches");
            }

            await Task.CompletedTask;
        }
    }
}