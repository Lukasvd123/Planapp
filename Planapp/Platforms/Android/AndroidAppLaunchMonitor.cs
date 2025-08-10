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
        private readonly Dictionary<string, long> _lastUsageCheck = new();
        private DateTime _lastCheckTime = DateTime.Now;
        private string _lastForegroundApp = string.Empty;
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 10;
        private const int CHECK_INTERVAL_MS = 500; // Check every 500ms for faster response
        private const int USAGE_THRESHOLD_MS = 500; // 500ms minimum to consider app launched
        private readonly Dictionary<string, DateTime> _lastEventCheck = new();

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

            _logger.LogInformation("Starting ultra-fast app launch monitoring with 500ms polling");

            try
            {
                // Check permissions thoroughly
                if (!await CheckAndRequestPermissions())
                {
                    _logger.LogError("Cannot start monitoring - missing required permissions");
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Permission Error",
                        "Usage stats permission required - please grant in settings"
                    );
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                IsMonitoring = true;
                _lastCheckTime = DateTime.Now.AddMinutes(-1); // Look back 1 minute initially
                _consecutiveErrors = 0;

                // Initialize baseline usage data
                await InitializeBaselineUsage();

                // Show debug notification that monitoring started
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Ultra-Fast Monitoring Started",
                    "Monitoring app usage with 500ms intervals for instant detection"
                );

                // Start both monitoring methods for maximum responsiveness
                _ = Task.Run(() => FastUsageMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _ = Task.Run(() => EventBasedMonitorLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting app launch monitoring");
                IsMonitoring = false;
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Monitoring Error",
                    $"Failed to start monitoring: {ex.Message}"
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

            _logger.LogInformation("Stopping ultra-fast app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _lastUsageCheck.Clear();
            _lastEventCheck.Clear();

            // Show debug notification that monitoring stopped
            AndroidNotificationHelper.ShowAppLaunchNotification(
                "Monitoring Stopped",
                "App launch monitoring has been stopped"
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

                // Check usage stats permission with multiple methods
                var hasUsagePermission = HasUsageStatsPermission();
                _logger.LogInformation($"Usage stats permission: {hasUsagePermission}");

                if (!hasUsagePermission)
                {
                    _logger.LogWarning("Usage stats permission not granted");
                    return false;
                }

                return true;
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

                // Method 1: Check AppOpsManager
                var appOps = context.GetSystemService(Context.AppOpsService) as AppOpsManager;
                if (appOps != null)
                {
                    var mode = appOps.CheckOpNoThrow(
                        AppOpsManager.OpstrGetUsageStats!,
                        Process.MyUid(),
                        context.PackageName!);

                    if (mode != AppOpsManagerMode.Allowed)
                    {
                        _logger.LogDebug($"AppOps permission check failed: {mode}");
                        return false;
                    }
                }

                // Method 2: Try to actually get usage stats
                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null)
                {
                    _logger.LogError("UsageStatsManager not available");
                    return false;
                }

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60); // 1 hour ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                var hasData = stats != null && stats.Count > 0;

                _logger.LogDebug($"Usage stats query returned {stats?.Count ?? 0} entries");
                return hasData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking usage stats permission");
                return false;
            }
        }

        private async Task InitializeBaselineUsage()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60); // 1 hour ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        if (!string.IsNullOrEmpty(stat.PackageName))
                        {
                            _lastUsageCheck[stat.PackageName] = stat.TotalTimeInForeground;
                            _lastEventCheck[stat.PackageName] = DateTime.Now.AddMinutes(-5);
                        }
                    }
                }

                _logger.LogInformation($"Initialized baseline usage data for {_lastUsageCheck.Count} apps");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing baseline usage");
            }
        }

        private async Task FastUsageMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Fast usage monitoring loop started with 500ms intervals");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForUsageIncreases();
                        _consecutiveErrors = 0; // Reset error count on success
                    }
                    catch (Exception ex)
                    {
                        _consecutiveErrors++;
                        _logger.LogError(ex, $"Error checking for app usage increases (consecutive errors: {_consecutiveErrors})");

                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            _logger.LogError("Too many consecutive errors, stopping monitoring");
                            AndroidNotificationHelper.ShowAppLaunchNotification(
                                "Monitoring Error",
                                "Too many errors, monitoring stopped - restart app"
                            );
                            break;
                        }

                        // Longer delay on errors
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    // Check every 500ms for ultra-fast detection
                    await Task.Delay(CHECK_INTERVAL_MS, cancellationToken);
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("Fast usage monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fast usage monitoring error");
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Monitoring Error",
                    $"Monitoring failed: {ex.Message}"
                );
            }
            finally
            {
                _logger.LogInformation("Fast usage monitoring stopped");
            }
        }

        private async Task EventBasedMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Event-based monitoring loop started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForAppEvents();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in event-based monitoring");
                    }

                    // Check events every 1 second
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("Event-based monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event-based monitoring error");
            }
            finally
            {
                _logger.LogInformation("Event-based monitoring stopped");
            }
        }

        private async Task CheckForAppEvents()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - 10000; // Last 10 seconds

                var usageEvents = usageStatsManager.QueryEvents(startTime, endTime);

                while (usageEvents.HasNextEvent)
                {
                    var eventObj = new UsageEvents.Event();
                    usageEvents.GetNextEvent(eventObj);

                    // Use the correct event type constant
                    if (eventObj.EventType == (int)UsageEvents.Event.MoveToForeground)
                    {
                        var packageName = eventObj.PackageName;
                        var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(eventObj.TimeStamp).DateTime;

                        if (!string.IsNullOrEmpty(packageName) &&
                            packageName != "com.usagemeter.androidapp" &&
                            (!_lastEventCheck.ContainsKey(packageName) ||
                            eventTime > _lastEventCheck[packageName]))
                        {
                            _lastEventCheck[packageName] = eventTime;

                            var appName = UsageStatsHelper.GetAppName(packageName);

                            _logger.LogInformation($"🚀 EVENT-BASED DETECTION: {appName} ({packageName}) resumed at {eventTime:HH:mm:ss.fff}");

                            // Show debug notification
                            AndroidNotificationHelper.ShowAppLaunchNotification(
                                $"Event Detected: {appName}",
                                $"Activity resumed at {eventTime:HH:mm:ss.fff}"
                            );

                            // Fire the event immediately
                            AppLaunched?.Invoke(this, new AppLaunchEventArgs
                            {
                                PackageName = packageName,
                                AppName = appName,
                                LaunchedAt = eventTime
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckForAppEvents");
            }

            await Task.CompletedTask;
        }

        private async Task CheckForUsageIncreases()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    _logger.LogWarning("Android context not available for monitoring");
                    return;
                }

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null)
                {
                    _logger.LogWarning("UsageStatsManager not available");
                    return;
                }

                var currentTime = DateTime.Now;
                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 60 * 2); // 2 hours ago for better data

                // Get current usage stats
                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                if (stats == null)
                {
                    _logger.LogDebug("No usage stats returned");
                    return;
                }

                var appLaunches = new List<(string PackageName, long UsageIncrease, string AppName)>();

                foreach (var stat in stats)
                {
                    if (string.IsNullOrEmpty(stat.PackageName)) continue;
                    if (stat.PackageName == "com.usagemeter.androidapp") continue; // Skip our own app

                    var packageName = stat.PackageName;
                    var currentUsage = stat.TotalTimeInForeground;

                    // Check if this is a significant usage increase
                    if (_lastUsageCheck.TryGetValue(packageName, out var lastUsage))
                    {
                        var usageIncrease = currentUsage - lastUsage;

                        // If usage increased by more than threshold, consider it an app launch
                        if (usageIncrease > USAGE_THRESHOLD_MS) // 500ms threshold for faster detection
                        {
                            var appName = UsageStatsHelper.GetAppName(packageName);
                            appLaunches.Add((packageName, usageIncrease, appName));

                            _logger.LogInformation($"🚀 USAGE-BASED DETECTION: {appName} (+{usageIncrease}ms)");
                        }
                    }

                    // Update the last known usage
                    _lastUsageCheck[packageName] = currentUsage;
                }

                // Process detected app launches
                foreach (var (packageName, usageIncrease, appName) in appLaunches)
                {
                    // Show debug notification
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        $"Usage Detected: {appName}",
                        $"Usage increase: {usageIncrease}ms"
                    );

                    // Fire the event
                    AppLaunched?.Invoke(this, new AppLaunchEventArgs
                    {
                        PackageName = packageName,
                        AppName = appName,
                        LaunchedAt = currentTime
                    });
                }

                _lastCheckTime = currentTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckForUsageIncreases");
                throw; // Re-throw to be caught by the monitoring loop
            }

            await Task.CompletedTask;
        }
    }
}