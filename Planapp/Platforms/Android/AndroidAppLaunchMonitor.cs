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

        public event EventHandler<AppLaunchEventArgs>? AppLaunched;
        public bool IsMonitoring { get; private set; }

        public AndroidAppLaunchMonitor(ILogger<AndroidAppLaunchMonitor> logger)
        {
            _logger = logger;
        }

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
            _lastCheckTime = DateTime.Now;

            // Start monitoring task
            _ = Task.Run(async () => await MonitorAppLaunches(_cancellationTokenSource.Token));

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

                    // Check every 2 seconds for new app launches
                    await Task.Delay(2000, cancellationToken);
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
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var currentTime = DateTime.Now;
                var checkPeriod = currentTime - _lastCheckTime;

                // Only check if enough time has passed
                if (checkPeriod.TotalSeconds < 1) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (long)checkPeriod.TotalMilliseconds - 5000; // Extra 5 seconds buffer

                // Get usage events to detect app launches
                var events = usageStatsManager.QueryEvents(startTime, endTime);

                if (events == null) return;

                var recentEvents = new List<(string PackageName, long TimeStamp)>();
                var eventObj = new UsageEvents.Event();

                while (events.HasNextEvent)
                {
                    events.GetNextEvent(eventObj);

                    // Correctly compare the EventType using the UsageEventType enum
                    if (eventObj.EventType == UsageEventType.ActivityResumed)
                    {
                        recentEvents.Add((
                            PackageName: eventObj.PackageName ?? "",
                            TimeStamp: eventObj.TimeStamp
                        ));
                    }
                }

                // Process the most recent events
                var uniqueLaunches = recentEvents
                    .Where(e => !string.IsNullOrEmpty(e.PackageName))
                    .GroupBy(e => e.PackageName)
                    .Select(g => g.OrderByDescending(e => e.TimeStamp).First())
                    .Where(e => !_recentLaunches.Contains(e.PackageName + e.TimeStamp))
                    .ToList();

                foreach (var launch in uniqueLaunches)
                {
                    var packageName = launch.PackageName;
                    var launchKey = packageName + launch.TimeStamp;

                    if (_recentLaunches.Contains(launchKey)) continue;

                    _recentLaunches.Add(launchKey);

                    // Clean old entries to prevent memory leak
                    if (_recentLaunches.Count > 100)
                    {
                        var oldEntries = _recentLaunches.Take(50).ToList();
                        foreach (var old in oldEntries)
                        {
                            _recentLaunches.Remove(old);
                        }
                    }

                    var appName = UsageStatsHelper.GetAppName(packageName);

                    _logger.LogInformation($"App launched: {appName} ({packageName})");

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