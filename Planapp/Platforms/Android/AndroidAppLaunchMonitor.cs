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
        private const int MAX_CONSECUTIVE_ERRORS = 5;
        private const int CHECK_INTERVAL_MS = 1000; // Check every 1 second for better responsiveness
        private const int APP_LAUNCH_DETECTION_WINDOW_SECONDS = 5; // Detect app launches in last 5 seconds
        private string? _lastForegroundApp = null;

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

            _logger.LogInformation("Starting enhanced app launch monitoring...");

            try
            {
                if (!await CheckAndRequestPermissions())
                {
                    _logger.LogError("Cannot start monitoring - missing required permissions");
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Permission Error",
                        "Usage stats permission required for app monitoring"
                    );
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                IsMonitoring = true;
                _consecutiveErrors = 0;
                _lastCheckTime = DateTime.Now;
                _lastSeenActive.Clear();
                _lastForegroundApp = null;

                // Initialize current foreground app
                await InitializeCurrentState();

                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Enhanced Monitoring Started",
                    "Actively monitoring app launches and switches"
                );

                // Start monitoring loop
                _ = Task.Run(() => MonitoringLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

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

            _logger.LogInformation("Stopping app launch monitoring");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            IsMonitoring = false;
            _lastSeenActive.Clear();
            _lastForegroundApp = null;

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

                var hasUsagePermission = HasUsageStatsPermission();
                _logger.LogInformation($"Usage stats permission: {hasUsagePermission}");

                if (!hasUsagePermission)
                {
                    _logger.LogWarning("Usage stats permission not granted");
                    // The service will show notification about missing permission
                }

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

                // Check AppOps permission
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

                // Verify by actually trying to get usage stats
                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return false;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (1000L * 60 * 5); // 5 minutes

                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                return stats != null && stats.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking usage stats permission");
                return false;
            }
        }

        private async Task InitializeCurrentState()
        {
            try
            {
                var currentApp = await GetCurrentForegroundApp();
                if (!string.IsNullOrEmpty(currentApp))
                {
                    _lastForegroundApp = currentApp;
                    _lastSeenActive[currentApp] = DateTime.Now;
                    _logger.LogInformation($"Initial foreground app: {currentApp}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing current state");
            }
        }

        private async Task MonitoringLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Enhanced app launch monitoring loop started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForAppSwitches();
                        _consecutiveErrors = 0;
                    }
                    catch (Exception ex)
                    {
                        _consecutiveErrors++;
                        _logger.LogError(ex, $"Error checking app switches (consecutive errors: {_consecutiveErrors})");

                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            _logger.LogError("Too many consecutive errors, stopping monitoring");
                            AndroidNotificationHelper.ShowAppLaunchNotification(
                                "Monitoring Error",
                                "Too many errors - monitoring stopped"
                            );
                            break;
                        }

                        // Wait longer after errors
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    await Task.Delay(CHECK_INTERVAL_MS, cancellationToken);
                }
            }
            catch (SystemOperationCanceledException)
            {
                _logger.LogInformation("App launch monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "App launch monitoring error");
            }
        }

        private async Task CheckForAppSwitches()
        {
            try
            {
                var currentApp = await GetCurrentForegroundApp();

                if (string.IsNullOrEmpty(currentApp))
                {
                    // No current app detected - this is normal (home screen, etc.)
                    return;
                }

                // Check if this is a new app switch
                if (currentApp != _lastForegroundApp && !string.IsNullOrEmpty(currentApp))
                {
                    var appName = UsageStatsHelper.GetAppName(currentApp);
                    var launchTime = DateTime.Now;

                    // Validate this is a legitimate app launch
                    if (IsValidAppLaunch(currentApp, appName, launchTime))
                    {
                        _logger.LogInformation($"🚀 APP SWITCH DETECTED: {appName} ({currentApp}) at {launchTime:HH:mm:ss}");

                        _lastSeenActive[currentApp] = launchTime;
                        _lastForegroundApp = currentApp;

                        AndroidNotificationHelper.ShowAppLaunchNotification(
                            $"App Switch: {appName}",
                            $"Switched to {appName} at {launchTime:HH:mm:ss}"
                        );

                        // Fire the app launched event
                        AppLaunched?.Invoke(this, new AppLaunchEventArgs
                        {
                            PackageName = currentApp,
                            AppName = appName,
                            LaunchedAt = launchTime
                        });
                    }
                    else
                    {
                        _logger.LogDebug($"Ignoring app switch to {appName} (filtered out)");
                    }
                }

                // Update last check time
                _lastCheckTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking app switches");
                throw;
            }
        }

        private async Task<string?> GetCurrentForegroundApp()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return null;

                var usageStatsManager = context.GetSystemService(Context.UsageStatsService) as UsageStatsManager;
                if (usageStatsManager == null) return null;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - (CHECK_INTERVAL_MS * 3); // Look back 3 intervals

                // Method 1: Use usage events to find the most recent activity resume
                var usageEvents = usageStatsManager.QueryEvents(startTime, endTime);
                string? mostRecentApp = null;
                long mostRecentTime = 0;

                while (usageEvents.HasNextEvent)
                {
                    var eventObj = new UsageEvents.Event();
                    usageEvents.GetNextEvent(eventObj);

                    if (eventObj.EventType == UsageEventType.ActivityResumed &&
                        eventObj.TimeStamp > mostRecentTime)
                    {
                        mostRecentApp = eventObj.PackageName;
                        mostRecentTime = eventObj.TimeStamp;
                    }
                }

                if (!string.IsNullOrEmpty(mostRecentApp))
                {
                    return mostRecentApp;
                }

                // Method 2: Fallback - use usage stats to find the app with most recent activity
                var stats = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
                if (stats != null && stats.Count > 0)
                {
                    var recentApp = stats
                        .Where(s => s != null && !string.IsNullOrEmpty(s.PackageName) && s.LastTimeUsed > 0)
                        .OrderByDescending(s => s.LastTimeUsed)
                        .FirstOrDefault();

                    return recentApp?.PackageName;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current foreground app");
                return null;
            }

            await Task.CompletedTask;
        }

        private bool IsValidAppLaunch(string packageName, string appName, DateTime launchTime)
        {
            try
            {
                // Skip our own app
                if (packageName == "com.usagemeter.androidapp") return false;

                // Skip system apps that users typically don't interact with
                var systemApps = new[]
                {
                    "com.android.systemui",
                    "com.android.launcher",
                    "com.android.launcher3",
                    "com.google.android.launcher",
                    "com.android.settings",
                    "android",
                    "com.android.phone",
                    "com.android.keyguard"
                };

                if (systemApps.Any(sys => packageName.Contains(sys, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                // Skip if app name is empty or generic
                if (string.IsNullOrWhiteSpace(appName) || appName == packageName)
                {
                    return false;
                }

                // Skip very rapid switches (less than 500ms)
                if (_lastSeenActive.TryGetValue(packageName, out var lastSeen))
                {
                    var timeSinceLastSeen = launchTime - lastSeen;
                    if (timeSinceLastSeen.TotalMilliseconds < 500)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating app launch for {packageName}");
                return false;
            }
        }
    }
}