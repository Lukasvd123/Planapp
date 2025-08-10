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
        private readonly Dictionary<string, DateTime> _lastAppTrigger = new();
        private readonly Dictionary<string, long> _lastUsageSnapshot = new();
        private DateTime _lastCheckTime = DateTime.Now;
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 10;
        private const int CHECK_INTERVAL_MS = 3000; // Check every 3 seconds - less aggressive
        private const long SIGNIFICANT_USAGE_INCREASE_MS = 5000; // 5 seconds minimum usage increase
        private const int MIN_TRIGGER_INTERVAL_SECONDS = 30; // 30 seconds between triggers for same app

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

            _logger.LogInformation("Starting refined app launch monitoring (less sensitive)");

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

                await InitializeUsageBaseline();

                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Refined Monitoring Started",
                    "Monitoring significant app usage changes only"
                );

                // Start focused monitoring - only significant events
                _ = Task.Run(() => SignificantUsageMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => AppLaunchEventMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

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

            _logger.LogInformation("Stopping refined app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _lastAppTrigger.Clear();
            _lastUsageSnapshot.Clear();

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

        private async Task InitializeUsageBaseline()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 2); // 2 hours ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        if (!string.IsNullOrEmpty(stat.PackageName))
                        {
                            _lastUsageSnapshot[stat.PackageName] = stat.TotalTimeInForeground;
                        }
                    }
                }

                _logger.LogInformation($"Initialized usage baseline for {_lastUsageSnapshot.Count} apps");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing usage baseline");
            }
        }

        private async Task SignificantUsageMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Significant usage monitoring started (3s intervals)");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForSignificantUsageChanges();
                        _consecutiveErrors = 0;
                    }
                    catch (Exception ex)
                    {
                        _consecutiveErrors++;
                        _logger.LogError(ex, $"Error checking usage changes (consecutive errors: {_consecutiveErrors})");

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
                _logger.LogInformation("Significant usage monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Significant usage monitoring error");
            }
        }

        private async Task AppLaunchEventMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("App launch event monitoring started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForAppLaunchEvents();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in app launch event monitoring");
                    }

                    await Task.Delay(5000, cancellationToken); // Check every 5 seconds
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("App launch event monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "App launch event monitoring error");
            }
        }

        private async Task CheckForAppLaunchEvents()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - 20000; // Last 20 seconds

                var usageEvents = usageStatsManager.QueryEvents(startTime, endTime);

                while (usageEvents.HasNextEvent)
                {
                    var eventObj = new UsageEvents.Event();
                    usageEvents.GetNextEvent(eventObj);

                    if (eventObj.EventType == UsageEventType.ActivityResumed)
                    {
                        var packageName = eventObj.PackageName;
                        var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(eventObj.TimeStamp).DateTime;

                        if (ShouldTriggerForApp(packageName, eventTime))
                        {
                            var appName = UsageStatsHelper.GetAppName(packageName);

                            _logger.LogInformation($"🚀 LAUNCH EVENT: {appName} ({packageName}) at {eventTime:HH:mm:ss}");

                            AndroidNotificationHelper.ShowAppLaunchNotification(
                                $"App Launch: {appName}",
                                $"Detected at {eventTime:HH:mm:ss}"
                            );

                            AppLaunched?.Invoke(this, new AppLaunchEventArgs
                            {
                                PackageName = packageName,
                                AppName = appName,
                                LaunchedAt = eventTime
                            });

                            _lastAppTrigger[packageName] = DateTime.Now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking app launch events");
            }

            await Task.CompletedTask;
        }

        private async Task CheckForSignificantUsageChanges()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 3); // 3 hours ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                if (stats == null) return;

                var significantChanges = new List<(string PackageName, long UsageIncrease, string AppName)>();

                foreach (var stat in stats)
                {
                    if (string.IsNullOrEmpty(stat.PackageName)) continue;
                    if (stat.PackageName == "com.usagemeter.androidapp") continue;

                    var packageName = stat.PackageName;
                    var currentUsage = stat.TotalTimeInForeground;

                    if (_lastUsageSnapshot.TryGetValue(packageName, out var previousUsage))
                    {
                        var usageIncrease = currentUsage - previousUsage;

                        // Only trigger for significant usage increases
                        if (usageIncrease > SIGNIFICANT_USAGE_INCREASE_MS)
                        {
                            if (ShouldTriggerForApp(packageName, DateTime.Now))
                            {
                                var appName = UsageStatsHelper.GetAppName(packageName);
                                significantChanges.Add((packageName, usageIncrease, appName));

                                _logger.LogInformation($"📈 SIGNIFICANT USAGE: {appName} (+{usageIncrease / 1000}s)");
                                _lastAppTrigger[packageName] = DateTime.Now;
                            }
                        }
                    }

                    _lastUsageSnapshot[packageName] = currentUsage;
                }

                // Process significant changes
                foreach (var (packageName, usageIncrease, appName) in significantChanges)
                {
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        $"Significant Usage: {appName}",
                        $"Usage increased by {usageIncrease / 1000} seconds"
                    );

                    AppLaunched?.Invoke(this, new AppLaunchEventArgs
                    {
                        PackageName = packageName,
                        AppName = appName,
                        LaunchedAt = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking significant usage changes");
                throw;
            }

            await Task.CompletedTask;
        }

        private bool ShouldTriggerForApp(string packageName, DateTime eventTime)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (packageName == "com.usagemeter.androidapp") return false;

            // Check if we've triggered for this app recently
            if (_lastAppTrigger.TryGetValue(packageName, out var lastTrigger))
            {
                var timeSinceLastTrigger = DateTime.Now - lastTrigger;
                if (timeSinceLastTrigger.TotalSeconds < MIN_TRIGGER_INTERVAL_SECONDS)
                {
                    return false; // Too soon since last trigger
                }
            }

            return true;
        }
    }
}