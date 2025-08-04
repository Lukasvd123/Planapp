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
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 5;

        public event EventHandler<AppLaunchEventArgs>? AppLaunched;
        public bool IsMonitoring { get; private set; }

        public AndroidAppLaunchMonitor(ILogger<AndroidAppLaunchMonitor> logger)
        {
            _logger = logger;

            // Initialize notification channel
            AndroidNotificationHelper.InitializeNotificationChannel();
        }

        public async Task StartMonitoringAsync()
        {
            if (IsMonitoring)
            {
                _logger.LogInformation("App launch monitoring already started");
                return;
            }

            _logger.LogInformation("Starting app launch monitoring");

            // Check permissions first
            if (!HasRequiredPermissions())
            {
                _logger.LogError("Missing required permissions for app launch monitoring");
                AndroidNotificationHelper.ShowAppLaunchNotification("Permission Error", "Missing usage stats permission");
                return;
            }

            // Request notification permission if needed
            if (!AndroidNotificationHelper.CheckNotificationPermission())
            {
                _logger.LogInformation("Requesting notification permission");
                AndroidNotificationHelper.RequestNotificationPermission();
            }

            _cancellationTokenSource = new CancellationTokenSource();
            IsMonitoring = true;
            _lastCheckTime = DateTime.Now.AddMinutes(-1);
            _consecutiveErrors = 0;

            // Show debug notification that monitoring started
            AndroidNotificationHelper.ShowAppLaunchNotification("Monitoring Started", "App launch monitoring is now active");

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

            // Show debug notification that monitoring stopped
            AndroidNotificationHelper.ShowAppLaunchNotification("Monitoring Stopped", "App launch monitoring has been stopped");

            await Task.CompletedTask;
        }

        private bool HasRequiredPermissions()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    _logger.LogError("Android context not available");
                    return false;
                }

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null)
                {
                    _logger.LogError("UsageStatsManager not available");
                    return false;
                }

                // Test if we can actually get usage stats
                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60); // 1 hour ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                var hasPermission = stats != null && stats.Count > 0;

                _logger.LogInformation($"Usage stats permission check: {hasPermission}");
                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking permissions");
                return false;
            }
        }

        private async Task MonitorAppLaunches(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("App launch monitoring loop started");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForAppLaunches();
                        _consecutiveErrors = 0; // Reset error count on success
                    }
                    catch (Exception ex)
                    {
                        _consecutiveErrors++;
                        _logger.LogError(ex, $"Error checking for app launches (consecutive errors: {_consecutiveErrors})");

                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            _logger.LogError("Too many consecutive errors, stopping monitoring");
                            AndroidNotificationHelper.ShowAppLaunchNotification("Monitoring Error", "Too many errors, monitoring stopped");
                            break;
                        }
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
                AndroidNotificationHelper.ShowAppLaunchNotification("Monitoring Error", $"Monitoring failed: {ex.Message}");
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
                var startTime = endTime - (long)checkPeriod.TotalMilliseconds - 5000; // Extra 5 seconds buffer

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

                    // Look for activity resume and move to foreground events
                    if (eventObj.EventType == UsageEventType.ActivityResumed ||
                        eventObj.EventType == UsageEventType.MoveToForeground)
                    {
                        appLaunches.Add((
                            PackageName: eventObj.PackageName ?? "",
                            TimeStamp: eventObj.TimeStamp,
                            EventType: (int)eventObj.EventType
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

                    // Skip if we've already processed this launch
                    if (_recentLaunches.Contains(launchKey)) continue;

                    // Skip if this is the same app that was previously foreground
                    if (packageName == _lastForegroundApp) continue;

                    _recentLaunches.Add(launchKey);
                    _lastForegroundApp = packageName;

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
                    var launchTime = DateTimeOffset.FromUnixTimeMilliseconds(launch.TimeStamp);

                    _logger.LogInformation($"App launched: {appName} ({packageName}) at {launchTime:HH:mm:ss}");

                    // Show debug notification for app launch
                    AndroidNotificationHelper.ShowAppLaunchNotification(appName, packageName);

                    // Fire the event
                    AppLaunched?.Invoke(this, new AppLaunchEventArgs
                    {
                        PackageName = packageName,
                        AppName = appName,
                        LaunchedAt = launchTime.DateTime
                    });
                }

                _lastCheckTime = currentTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckForAppLaunches");
                throw; // Re-throw to be caught by the monitoring loop
            }

            await Task.CompletedTask;
        }
    }
}