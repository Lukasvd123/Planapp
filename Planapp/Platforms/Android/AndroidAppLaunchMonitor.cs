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
        private const int CHECK_INTERVAL_MS = 3000; // Check every 3 seconds

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

            _logger.LogInformation("Starting enhanced app launch monitoring");

            try
            {
                // Initialize notification channel first
                AndroidNotificationHelper.InitializeNotificationChannel();

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
                _lastCheckTime = DateTime.Now.AddMinutes(-5); // Look back 5 minutes initially
                _consecutiveErrors = 0;

                // Initialize baseline usage data
                await InitializeBaselineUsage();

                // Show debug notification that monitoring started
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Enhanced Monitoring Started",
                    "Continuous app usage monitoring is now active"
                );

                // Start monitoring task
                _ = Task.Run(() => EnhancedMonitorLoop(_cancellationTokenSource.Token));

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

            _logger.LogInformation("Stopping enhanced app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _lastUsageCheck.Clear();

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

                // Check usage stats permission
                var hasUsagePermission = HasUsageStatsPermission();
                _logger.LogInformation($"Usage stats permission: {hasUsagePermission}");

                if (!hasUsagePermission)
                {
                    _logger.LogWarning("Usage stats permission not granted");

                    // Try to open usage settings
                    try
                    {
                        var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
                        intent.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(intent);
                        _logger.LogInformation("Opened usage access settings");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to open usage settings");
                    }

                    return false;
                }

                // Check notification permission for Android 13+
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    if (!AndroidNotificationHelper.CheckNotificationPermission())
                    {
                        _logger.LogInformation("Requesting notification permission");
                        AndroidNotificationHelper.RequestNotificationPermission();
                    }
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
                var startTime = endTime - (1000L * 60 * 60 * 2); // 2 hours ago

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
                var startTime = endTime - (1000L * 60 * 60 * 24); // 24 hours ago

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);

                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        if (!string.IsNullOrEmpty(stat.PackageName))
                        {
                            _lastUsageCheck[stat.PackageName] = stat.TotalTimeInForeground;
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

        private async Task EnhancedMonitorLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Enhanced monitoring loop started");

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
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    // Check every 3 seconds
                    await Task.Delay(CHECK_INTERVAL_MS, cancellationToken);
                }
            }
            catch (System.OperationCanceledException)
            {
                _logger.LogInformation("Enhanced monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enhanced monitoring error");
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Monitoring Error",
                    $"Monitoring failed: {ex.Message}"
                );
            }
            finally
            {
                IsMonitoring = false;
                _logger.LogInformation("Enhanced monitoring stopped");
            }
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
                var startTime = endTime - (1000L * 60 * 60 * 24); // 24 hours ago

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

                        // If usage increased by more than 5 seconds, consider it an app launch
                        if (usageIncrease > 5000) // 5 seconds in milliseconds
                        {
                            var appName = UsageStatsHelper.GetAppName(packageName);
                            appLaunches.Add((packageName, usageIncrease, appName));

                            _logger.LogInformation($"Detected app usage: {appName} (+{usageIncrease}ms)");
                        }
                    }

                    // Update the last known usage
                    _lastUsageCheck[packageName] = currentUsage;
                }

                // Process detected app launches
                foreach (var (packageName, usageIncrease, appName) in appLaunches)
                {
                    // Show debug notification
                    AndroidNotificationHelper.ShowAppLaunchNotification($"App Used: {appName}", $"Usage increase: {usageIncrease}ms");

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