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

                // Initialize services in background
                Task.Run(async () =>
                {
                    try
                    {
                        await InitializeAndStartMonitoring();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in foreground service initialization: {ex}");
                        _logger?.LogError(ex, "Error in foreground service initialization");
                        UpdateNotification("Service Error", $"Monitoring failed: {ex.Message}");
                    }
                });

                return StartCommandResult.Sticky; // Ensure service restarts if killed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex}");
                return StartCommandResult.NotSticky;
            }
        }

        private async Task InitializeAndStartMonitoring()
        {
            try
            {
                var retries = 0;
                var maxRetries = 15; // Increased retries

                while (retries < maxRetries)
                {
                    var app = MauiApplication.Current;
                    if (app != null)
                    {
                        var serviceProvider = IPlatformApplication.Current?.Services;
                        if (serviceProvider != null)
                        {
                            _logger = serviceProvider.GetService<ILogger<AndroidForegroundService>>();
                            _ruleMonitor = serviceProvider.GetService<RuleMonitorService>();
                            _appLaunchMonitor = serviceProvider.GetService<IAppLaunchMonitor>();

                            if (_ruleMonitor != null && _appLaunchMonitor != null)
                            {
                                _logger?.LogInformation("Services found - starting enhanced monitoring");

                                var settingsService = serviceProvider.GetService<ISettingsService>();
                                var settings = await settingsService?.GetSettingsAsync()!;

                                if (settings?.AllRulesEnabled == true)
                                {
                                    // Start both monitoring services
                                    await _appLaunchMonitor.StartMonitoringAsync();
                                    _logger?.LogInformation($"App launch monitor started. IsMonitoring: {_appLaunchMonitor.IsMonitoring}");

                                    await _ruleMonitor.StartAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
                                    _logger?.LogInformation("Enhanced rule monitor started");

                                    var activeRulesCount = await GetActiveRulesCount(serviceProvider);
                                    UpdateNotification("Enhanced Monitoring Active",
                                        $"Monitoring launches + usage for {activeRulesCount} active rules");

                                    AndroidNotificationHelper.ShowAppLaunchNotification(
                                        "Enhanced Background Service Started",
                                        "Service actively monitoring app launches and usage limits"
                                    );

                                    return; // Success
                                }
                                else
                                {
                                    UpdateNotification("Monitoring Disabled", "Rules are disabled in settings");
                                    _logger?.LogInformation("Rules are disabled - monitoring not started");
                                    return;
                                }
                            }
                        }
                    }

                    retries++;
                    await Task.Delay(3000); // Wait 3 seconds before retry
                }

                _logger?.LogError("Failed to initialize services after maximum retries");
                UpdateNotification("Initialization Failed", "Could not start monitoring services");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in InitializeAndStartMonitoring");
                UpdateNotification("Service Error", $"Failed to start: {ex.Message}");
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
            catch
            {
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
                    $"Launch Monitor: {launchStatus} | Rule Monitor: {ruleStatus}");

                // If monitoring stopped unexpectedly, try to restart it
                if (_appLaunchMonitor?.IsMonitoring != true && _cancellationTokenSource?.Token.IsCancellationRequested != true)
                {
                    _logger?.LogWarning("Launch monitoring stopped unexpectedly - attempting restart");
                    Task.Run(async () =>
                    {
                        try
                        {
                            await InitializeAndStartMonitoring();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error restarting monitoring");
                        }
                    });
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
                // Check if services are still responsive
                var isHealthy = _appLaunchMonitor?.IsMonitoring == true && _ruleMonitor != null;

                if (!isHealthy)
                {
                    _logger?.LogWarning("Health check failed - services may be unresponsive");

                    // Show notification about health issue
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Service Health Warning",
                        "Monitoring services may need restart - check app"
                    );
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
                _cancellationTokenSource?.Cancel();

                // Stop monitoring services
                _appLaunchMonitor?.StopMonitoringAsync().Wait(5000);
                _ruleMonitor?.StopAsync().Wait(5000);

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