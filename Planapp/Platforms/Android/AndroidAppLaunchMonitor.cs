using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App.Usage;
using Android.Content;
using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Services;
using Android.App;
using Android.OS;
using AndroidApp = Android.App.Application;
using SystemOperationCanceledException = System.OperationCanceledException;

namespace com.usagemeter.androidapp.Platforms.Android
{
    public class AndroidAppLaunchMonitor : IAppLaunchMonitor
    {
        private readonly ILogger<AndroidAppLaunchMonitor> _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly Dictionary<string, DateTime> _lastTriggerTime = new();
        private readonly Dictionary<string, DateTime> _lastSeenActive = new();
        private DateTime _lastCheckTime = DateTime.Now;
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 10;
        private const int CHECK_INTERVAL_MS = 5000; // Check every 5 seconds
        private const int MIN_TRIGGER_INTERVAL_MINUTES = 30; // 30 minutes between triggers for same app
        private const int APP_LAUNCH_DETECTION_WINDOW_SECONDS = 15; // Only trigger if app became active in last 15 seconds

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

            _logger.LogInformation("Starting controlled app launch monitoring");

            try
            {
                if (!await CheckAndRequestPermissions())
                {
                    _logger.LogError("Cannot start monitoring - missing required permissions");
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Permission Error",
                        "Usage stats permission required"
                    );
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                IsMonitoring = true;
                _consecutiveErrors = 0;

                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Controlled Monitoring Started",
                    "Monitoring actual app launches only"
                );

                // Start controlled monitoring - only actual launches
                _ = Task.Run(() => ControlledLaunchMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting app launch monitoring");
                IsMonitoring = false;
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Monitoring Error",
                    $"Failed to start: {ex.Message}"
                );
            }
        }

        public async Task StopMonitoringAsync()
        {
            if (!IsMonitoring)
            {
                _logger.LogInformation("App launch monitoring already stopped");
                return;
            }

            _logger.LogInformation("Stopping controlled app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _lastTriggerTime.Clear();
            _lastSeenActive.Clear();

            AndroidNotificationHelper.ShowAppLaunchNotification(
                "Monitoring Stopped",
                "App monitoring has been stopped"
            );

            await Task.CompletedTask;
        }

        private async Task<bool> CheckAndRequestPermissions()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    _logger.LogError("Android context not available");
                    return false;
                }

                var hasUsagePermission = HasUsageStatsPermission();
                _logger.LogInformation($"Usage stats permission: {hasUsagePermission}");

                return hasUsagePermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking permissions");
                return false;
            }
        }

        private bool HasUsageStatsPermission()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return false;

                var appOps = context.GetSystemService(Context.AppOpsService) as AppOpsManager;
                if (appOps != null)
                {
                    var mode = appOps.CheckOpNoThrow(
                        AppOpsManager.OpstrGetUsageStats!,
                        Process.MyUid(),
                        context.PackageName!);

                    if (mode != AppOpsManagerMode.Allowed)
                    {
                        return false;
                    }
                }

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return false;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60);

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                return stats != null && stats.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking usage stats permission");
                return false;
            }
        }

        private async Task ControlledLaunchMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Controlled launch monitoring started (5s intervals)");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForActualAppLaunches();
                        _consecutiveErrors = 0;
                    }
                    catch (Exception ex)
                    {
                        _consecutiveErrors++;
                        _logger.LogError(ex, $"Error checking app launches (consecutive errors: {_consecutiveErrors})");

                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            _logger.LogError("Too many consecutive errors, stopping monitoring");
                            break;
                        }

                        await Task.Delay(10000, cancellationToken);
                        continue;
                    }

                    await Task.Delay(CHECK_INTERVAL_MS, cancellationToken);
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("Controlled launch monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Controlled launch monitoring error");
            }
        }

        private async Task CheckForActualAppLaunches()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (CHECK_INTERVAL_MS + 5000); // Check events in the last check interval + buffer

                // Check recent usage events for actual activity transitions
                var usageEvents = usageStatsManager.QueryEvents(startTime, endTime);
                var recentLaunches = new List<(string PackageName, DateTime LaunchTime)>();

                while (usageEvents.HasNextEvent)
                {
                    var eventObj = new UsageEvents.Event();
                    usageEvents.GetNextEvent(eventObj);

                    if (eventObj.EventType == UsageEventType.ActivityResumed)
                    {
                        var packageName = eventObj.PackageName;
                        var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(eventObj.TimeStamp).DateTime;

                        // Only consider as "launch" if we haven't seen this app active recently
                        if (IsActualAppLaunch(packageName, eventTime))
                        {
                            recentLaunches.Add((packageName, eventTime));
                            _lastSeenActive[packageName] = eventTime;
                        }
                    }
                }

                // Process actual launches
                foreach (var (packageName, launchTime) in recentLaunches)
                {
                    if (ShouldTriggerForPackage(packageName, launchTime))
                    {
                        var appName = UsageStatsHelper.GetAppName(packageName);

                        _logger.LogInformation($"🚀 ACTUAL LAUNCH DETECTED: {appName} ({packageName}) at {launchTime:HH:mm:ss}");

                        AndroidNotificationHelper.ShowAppLaunchNotification(
                            $"App Launch: {appName}",
                            $"Detected fresh launch at {launchTime:HH:mm:ss}"
                        );

                        AppLaunched?.Invoke(this, new AppLaunchEventArgs
                        {
                            PackageName = packageName,
                            AppName = appName,
                            LaunchedAt = launchTime
                        });

                        _lastTriggerTime[packageName] = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking actual app launches");
                throw;
            }

            await Task.CompletedTask;
        }

        private bool IsActualAppLaunch(string packageName, DateTime eventTime)
        {
            // Skip our own app
            if (packageName == "com.usagemeter.androidapp") return false;

            // Check if we've seen this app active recently
            if (_lastSeenActive.TryGetValue(packageName, out var lastSeen))
            {
                var timeSinceLastSeen = eventTime - lastSeen;

                // Only consider as "launch" if app hasn't been active for at least 15 seconds
                if (timeSinceLastSeen.TotalSeconds < APP_LAUNCH_DETECTION_WINDOW_SECONDS)
                {
                    return false; // App was already active recently, not a fresh launch
                }
            }

            return true;
        }

        private bool ShouldTriggerForPackage(string packageName, DateTime launchTime)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (packageName == "com.usagemeter.androidapp") return false;

            // Check if we've triggered for this app recently (much longer cooldown)
            if (_lastTriggerTime.TryGetValue(packageName, out var lastTrigger))
            {
                var timeSinceLastTrigger = DateTime.Now - lastTrigger;
                if (timeSinceLastTrigger.TotalMinutes < MIN_TRIGGER_INTERVAL_MINUTES)
                {
                    _logger.LogDebug($"Skipping trigger for {packageName} - too soon since last trigger ({timeSinceLastTrigger.TotalMinutes:F1} minutes ago)");
                    return false;
                }
            }

            // Only trigger for very recent launches (within last 15 seconds)
            var launchAge = DateTime.Now - launchTime;
            if (launchAge.TotalSeconds > APP_LAUNCH_DETECTION_WINDOW_SECONDS)
            {
                _logger.LogDebug($"Skipping trigger for {packageName} - launch too old ({launchAge.TotalSeconds:F1} seconds)");
                return false;
            }

            return true;
        }
    }
}