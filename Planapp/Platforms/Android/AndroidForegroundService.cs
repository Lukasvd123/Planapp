using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Services;
using AndroidApp = Android.App.Application;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace com.usagemeter.androidapp.Platforms.Android
{
    [Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class AndroidForegroundService : Service
    {
        private const int NOTIFICATION_ID = 1337;
        private const string CHANNEL_ID = "planapp_foreground_channel";
        private ILogger<AndroidForegroundService>? _logger;
        private RuleMonitorService? _ruleMonitor;
        private IAppLaunchMonitor? _appLaunchMonitor;
        private static AndroidForegroundService? _instance;
        private CancellationTokenSource? _cancellationTokenSource;
        private Timer? _keepAliveTimer;
        private Timer? _healthCheckTimer;
        private Timer? _retryTimer;
        private int _initializationRetries = 0;
        private const int MAX_INITIALIZATION_RETRIES = 20;

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            try
            {
                _instance = this;
                CreateNotificationChannel();
                StartForeground(NOTIFICATION_ID, CreateNotification());

                System.Diagnostics.Debug.WriteLine("AndroidForegroundService started - initializing enhanced monitoring");

                _cancellationTokenSource = new CancellationTokenSource();

                // Start keep-alive timer
                _keepAliveTimer = new Timer(KeepAlive, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));

                // Start health check timer
                _healthCheckTimer = new Timer(HealthCheck, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

                // Initialize services with retry logic
                StartInitializationWithRetry();

                return StartCommandResult.Sticky; // Ensure service restarts if killed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex}");
                return StartCommandResult.NotSticky;
            }
        }

        private void StartInitializationWithRetry()
        {
            // Start immediate initialization attempt
            Task.Run(async () =>
            {
                try
                {
                    await InitializeAndStartMonitoring();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Initial service initialization failed: {ex}");
                    // Retry timer will handle retries
                }
            });

            // Start retry timer for failed initializations
            _retryTimer = new Timer(async _ => await RetryInitialization(), null,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private async Task RetryInitialization()
        {
            if (_appLaunchMonitor?.IsMonitoring == true && _ruleMonitor != null)
            {
                // Already initialized successfully
                _retryTimer?.Dispose();
                _retryTimer = null;
                return;
            }

            if (_initializationRetries >= MAX_INITIALIZATION_RETRIES)
            {
                System.Diagnostics.Debug.WriteLine("Max initialization retries reached - stopping retry attempts");
                _retryTimer?.Dispose();
                _retryTimer = null;
                UpdateNotification("Initialization Failed", "Could not start monitoring after multiple attempts");
                return;
            }

            _initializationRetries++;
            System.Diagnostics.Debug.WriteLine($"Retrying service initialization (attempt {_initializationRetries})...");

            try
            {
                await InitializeAndStartMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retry {_initializationRetries} failed: {ex.Message}");
            }
        }

        private async Task InitializeAndStartMonitoring()
        {
            try
            {
                // More robust service provider access with multiple retry strategies
                IServiceProvider? serviceProvider = null;

                // Strategy 1: Try MauiApplication.Current
                try
                {
                    var app = MauiApplication.Current;
                    if (app != null)
                    {
                        var platformApp = IPlatformApplication.Current;
                        if (platformApp != null)
                        {
                            serviceProvider = platformApp.Services;
                            System.Diagnostics.Debug.WriteLine("Got service provider via MauiApplication.Current");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MauiApplication.Current approach failed: {ex.Message}");
                }

                // Strategy 2: Try IPlatformApplication.Current directly
                if (serviceProvider == null)
                {
                    try
                    {
                        var platformApp = IPlatformApplication.Current;
                        if (platformApp?.Services != null)
                        {
                            serviceProvider = platformApp.Services;
                            System.Diagnostics.Debug.WriteLine("Got service provider via IPlatformApplication.Current");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"IPlatformApplication.Current approach failed: {ex.Message}");
                    }
                }

                if (serviceProvider == null)
                {
                    throw new InvalidOperationException("Could not obtain service provider from any source");
                }

                // Get services with individual error handling
                try
                {
                    _logger = serviceProvider.GetService<ILogger<AndroidForegroundService>>();
                    _logger?.LogInformation("Logger service obtained");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting logger: {ex.Message}");
                }

                try
                {
                    _ruleMonitor = serviceProvider.GetService<RuleMonitorService>();
                    if (_ruleMonitor != null)
                    {
                        _logger?.LogInformation("Rule monitor service obtained");
                    }
                    else
                    {
                        throw new InvalidOperationException("RuleMonitorService not found");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting rule monitor: {ex.Message}");
                    throw;
                }

                try
                {
                    _appLaunchMonitor = serviceProvider.GetService<IAppLaunchMonitor>();
                    if (_appLaunchMonitor != null)
                    {
                        _logger?.LogInformation("App launch monitor service obtained");
                    }
                    else
                    {
                        throw new InvalidOperationException("IAppLaunchMonitor not found");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting app launch monitor: {ex.Message}");
                    throw;
                }

                // Check if rules are enabled
                var settingsService = serviceProvider.GetService<ISettingsService>();
                if (settingsService == null)
                {
                    throw new InvalidOperationException("ISettingsService not found");
                }

                var settings = await settingsService.GetSettingsAsync();
                if (settings?.AllRulesEnabled != true)
                {
                    _logger?.LogInformation("Rules are disabled - monitoring not started");
                    UpdateNotification("Monitoring Disabled", "Rules are disabled in settings");

                    // Stop retry timer since this is not an error condition
                    _retryTimer?.Dispose();
                    _retryTimer = null;
                    return;
                }

                // Start monitoring services
                _logger?.LogInformation("Starting monitoring services...");

                // Start app launch monitor first
                await _appLaunchMonitor.StartMonitoringAsync();
                _logger?.LogInformation($"App launch monitor started. IsMonitoring: {_appLaunchMonitor.IsMonitoring}");

                if (!_appLaunchMonitor.IsMonitoring)
                {
                    throw new InvalidOperationException("App launch monitor failed to start");
                }

                // Start rule monitor
                await _ruleMonitor.StartAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
                _logger?.LogInformation("Rule monitor started");

                // Get active rules count for status
                var activeRulesCount = await GetActiveRulesCount(serviceProvider);

                UpdateNotification("Enhanced Monitoring Active",
                    $"Successfully monitoring {activeRulesCount} active rules");

                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Background Service Ready",
                    $"Enhanced monitoring active - {activeRulesCount} rules enabled"
                );

                _logger?.LogInformation($"Service initialization completed successfully - monitoring {activeRulesCount} rules");

                // Stop retry timer on success
                _retryTimer?.Dispose();
                _retryTimer = null;
                _initializationRetries = 0; // Reset counter on success
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error in InitializeAndStartMonitoring (attempt {_initializationRetries})");
                UpdateNotification("Initialization Error", $"Attempt {_initializationRetries}: {ex.Message}");
                throw; // Re-throw to trigger retry logic
            }
        }

        private async Task<int> GetActiveRulesCount(IServiceProvider serviceProvider)
        {
            try
            {
                var ruleService = serviceProvider.GetService<IRuleService>();
                var rules = await ruleService?.GetRulesAsync()!;
                return rules?.Count(r => r.IsEnabled) ?? 0;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error getting active rules count");
                return 0;
            }
        }

        private void KeepAlive(object? state)
        {
            try
            {
                var launchStatus = _appLaunchMonitor?.IsMonitoring == true ? "✅ Active" : "❌ Inactive";
                var ruleStatus = _ruleMonitor != null ? "✅ Running" : "❌ Stopped";

                System.Diagnostics.Debug.WriteLine($"KeepAlive - Launch Monitor: {launchStatus}, Rule Monitor: {ruleStatus}");

                // Update notification with current status
                UpdateNotification("Enhanced Monitoring Active",
                    $"Launch: {launchStatus} | Rules: {ruleStatus}");

                // If monitoring stopped unexpectedly, trigger reinitialization
                if (_appLaunchMonitor?.IsMonitoring != true && _cancellationTokenSource?.Token.IsCancellationRequested != true)
                {
                    _logger?.LogWarning("Launch monitoring stopped unexpectedly - triggering reinitialization");
                    _initializationRetries = 0; // Reset retry count
                    StartInitializationWithRetry();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in KeepAlive");
            }
        }

        private void HealthCheck(object? state)
        {
            try
            {
                var isHealthy = _appLaunchMonitor?.IsMonitoring == true && _ruleMonitor != null;

                if (!isHealthy)
                {
                    _logger?.LogWarning("Health check failed - services may be unresponsive");

                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Service Health Warning",
                        "Monitoring services may need restart - check app"
                    );

                    // Trigger reinitialization on health check failure
                    if (_retryTimer == null) // Only if not already retrying
                    {
                        _initializationRetries = 0;
                        StartInitializationWithRetry();
                    }
                }
                else
                {
                    _logger?.LogDebug("Health check passed - all services responsive");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in health check");
            }
        }

        public override void OnDestroy()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("AndroidForegroundService stopping");

                _keepAliveTimer?.Dispose();
                _healthCheckTimer?.Dispose();
                _retryTimer?.Dispose();
                _cancellationTokenSource?.Cancel();

                // Stop monitoring services
                try
                {
                    _appLaunchMonitor?.StopMonitoringAsync().Wait(5000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping app launch monitor: {ex}");
                }

                try
                {
                    _ruleMonitor?.StopAsync().Wait(5000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping rule monitor: {ex}");
                }

                System.Diagnostics.Debug.WriteLine("AndroidForegroundService stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDestroy: {ex}");
            }

            _instance = null;
            base.OnDestroy();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var notificationManager = GetSystemService(NotificationService) as NotificationManager;

                var channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Enhanced Usage Monitoring",
                    NotificationImportance.Low)
                {
                    Description = "Monitors app usage and enforces rules in background"
                };

                notificationManager?.CreateNotificationChannel(channel);
            }
        }

        private Notification CreateNotification()
        {
            return BuildNotification("Usage Meter Starting", "Initializing enhanced monitoring...");
        }

        private void UpdateNotification(string title, string content)
        {
            try
            {
                var notification = BuildNotification(title, content);
                var notificationManager = GetSystemService(NotificationService) as NotificationManager;
                notificationManager?.Notify(NOTIFICATION_ID, notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating notification: {ex}");
            }
        }

        private Notification BuildNotification(string title, string content)
        {
            var builder = new NotificationCompat.Builder(this, CHANNEL_ID)
                .SetContentTitle(title)
                .SetContentText(content)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityLow)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(content));

            // Create intent to open app when notification is tapped
            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);

            var pendingIntent = PendingIntent.GetActivity(
                this, 0, intent,
                Build.VERSION.SdkInt >= BuildVersionCodes.M
                    ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                    : PendingIntentFlags.UpdateCurrent);

            builder.SetContentIntent(pendingIntent);

            return builder.Build();
        }

        public static async Task StartAsync()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot start service - no context");
                    return;
                }

                var intent = new Intent(context, typeof(AndroidForegroundService));

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    context.StartForegroundService(intent);
                }
                else
                {
                    context.StartService(intent);
                }

                System.Diagnostics.Debug.WriteLine("Enhanced foreground service start requested");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex}");
            }
        }

        public static async void ShowBlockingOverlay(com.usagemeter.androidapp.Models.AppRule rule)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ShowBlockingOverlay called for rule: {rule.Name}");

                var context = Platform.CurrentActivity ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("No context available for showing overlay");
                    return;
                }

                // Method 1: Bring our app to foreground with high priority
                var intent = new Intent(context, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.NewTask |
                               ActivityFlags.ClearTop |
                               ActivityFlags.SingleTop |
                               ActivityFlags.ReorderToFront);
                intent.PutExtra("SHOW_BLOCK", true);
                intent.PutExtra("RULE_ID", rule.Id);
                intent.PutExtra("FORCE_FOREGROUND", true);

                context.StartActivity(intent);

                // Method 2: Show high-priority notification as backup
                ShowUrgentNotification(context, rule);

                System.Diagnostics.Debug.WriteLine("Blocking overlay methods executed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing blocking overlay: {ex}");
            }
        }

        private static void ShowUrgentNotification(Context context, com.usagemeter.androidapp.Models.AppRule rule)
        {
            try
            {
                AndroidNotificationHelper.InitializeNotificationChannel();

                var builder = new NotificationCompat.Builder(context, "planapp_debug_channel")
                    .SetContentTitle("⚠️ Usage Limit Reached")
                    .SetContentText($"Rule '{rule.Name}' has been triggered - Tap to open app")
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogAlert)
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetCategory(NotificationCompat.CategoryAlarm)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultAll);

                // Create full screen intent
                var fullScreenIntent = new Intent(context, typeof(MainActivity));
                fullScreenIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                fullScreenIntent.PutExtra("SHOW_BLOCK", true);
                fullScreenIntent.PutExtra("RULE_ID", rule.Id);

                var fullScreenPendingIntent = PendingIntent.GetActivity(
                    context,
                    rule.Id.GetHashCode(),
                    fullScreenIntent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M
                        ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                        : PendingIntentFlags.UpdateCurrent);

                builder.SetFullScreenIntent(fullScreenPendingIntent, true);
                builder.SetContentIntent(fullScreenPendingIntent);

                var notificationManager = NotificationManager.FromContext(context);
                notificationManager?.Notify(2000 + rule.Id.GetHashCode(), builder.Build());

                System.Diagnostics.Debug.WriteLine("Urgent notification shown");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing urgent notification: {ex}");
            }
        }
    }
}