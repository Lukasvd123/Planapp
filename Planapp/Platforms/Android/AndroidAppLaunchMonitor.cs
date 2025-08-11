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
        private readonly Dictionary<string, DateTime> _lastSeenActive = new();
        private DateTime _lastCheckTime = DateTime.Now;
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 10;
        private const int CHECK_INTERVAL_MS = 2000; // Check every 2 seconds for immediate response
        private const int APP_LAUNCH_DETECTION_WINDOW_SECONDS = 10; // Detect app launches in last 10 seconds

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

            _logger.LogInformation("Starting IMMEDIATE app launch monitoring (NO COOLDOWNS)");

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
                    "Immediate Monitoring Started",
                    "Monitoring app launches with immediate triggers - NO COOLDOWNS"
                );

                // Start immediate monitoring - every app launch triggers rules
                _ = Task.Run(() => ImmediateLaunchMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

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

            _logger.LogInformation("Stopping immediate app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
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

        private async Task ImmediateLaunchMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Immediate launch monitoring started (2s intervals, NO COOLDOWNS)");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForImmediateAppLaunches();
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

                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    await Task.Delay(CHECK_INTERVAL_MS, cancellationToken);
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("Immediate launch monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Immediate launch monitoring error");
            }
        }

        private async Task CheckForImmediateAppLaunches()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (CHECK_INTERVAL_MS + 3000); // Check events in the last check interval + buffer

                // Check recent usage events for activity transitions
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

                        // Check if this is a valid app launch (not our own app)
                        if (IsValidAppLaunch(packageName, eventTime))
                        {
                            recentLaunches.Add((packageName, eventTime));
                            _lastSeenActive[packageName] = eventTime;
                        }
                    }
                }

                // Process ALL launches IMMEDIATELY (no cooldown check)
                foreach (var (packageName, launchTime) in recentLaunches)
                {
                    var appName = UsageStatsHelper.GetAppName(packageName);

                    _logger.LogInformation($"🚀 IMMEDIATE LAUNCH DETECTED: {appName} ({packageName}) at {launchTime:HH:mm:ss}");

                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        $"App Launch: {appName}",
                        $"Detected launch at {launchTime:HH:mm:ss} - checking rules immediately"
                    );

                    AppLaunched?.Invoke(this, new AppLaunchEventArgs
                    {
                        PackageName = packageName,
                        AppName = appName,
                        LaunchedAt = launchTime
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking immediate app launches");
                throw;
            }

            await Task.CompletedTask;
        }

        private bool IsValidAppLaunch(string packageName, DateTime eventTime)
        {
            // Skip our own app
            if (packageName == "com.usagemeter.androidapp") return false;

            // Skip system launcher apps
            if (packageName.Contains("launcher")) return false;

            // Only consider launches in the last detection window
            var launchAge = DateTime.Now - eventTime;
            if (launchAge.TotalSeconds > APP_LAUNCH_DETECTION_WINDOW_SECONDS)
            {
                return false;
            }

            return true;
        }
    }
}