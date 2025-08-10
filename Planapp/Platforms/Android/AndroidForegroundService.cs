using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Services;
using AndroidApp = Android.App.Application;

namespace com.usagemeter.androidapp.Platforms.Android
{
    [Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class AndroidForegroundService : Service
    {
        private const int NOTIFICATION_ID = 1337;
        private const string CHANNEL_ID = "planapp_foreground_channel";
        private ILogger<AndroidForegroundService>? _logger;
        private RuleMonitorService? _ruleMonitor;
        private static AndroidForegroundService? _instance;

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            try
            {
                _instance = this;
                CreateNotificationChannel();
                StartForeground(NOTIFICATION_ID, CreateNotification());

                System.Diagnostics.Debug.WriteLine("AndroidForegroundService started");

                // Start monitoring in background
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000); // Wait for services to be ready

                        var app = MauiApplication.Current;
                        if (app != null)
                        {
                            var serviceProvider = IPlatformApplication.Current?.Services;
                            if (serviceProvider != null)
                            {
                                _logger = serviceProvider.GetService<ILogger<AndroidForegroundService>>();
                                _ruleMonitor = serviceProvider.GetService<RuleMonitorService>();

                                if (_ruleMonitor != null)
                                {
                                    _logger?.LogInformation("Starting rule monitor from foreground service");
                                    await _ruleMonitor.StartAsync();

                                    // Update notification to show monitoring is active
                                    UpdateNotification("Monitoring Active", "Tracking app usage and enforcing rules");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in foreground service: {ex}");
                    }
                });

                return StartCommandResult.Sticky;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex}");
                return StartCommandResult.NotSticky;
            }
        }

        public override void OnDestroy()
        {
            try
            {
                _ruleMonitor?.StopAsync().Wait(5000);
                System.Diagnostics.Debug.WriteLine("AndroidForegroundService stopped");
            }
            catch { }

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
                    "Usage Monitoring",
                    NotificationImportance.Low)
                {
                    Description = "Monitors app usage in background"
                };

                notificationManager?.CreateNotificationChannel(channel);
            }
        }

        private Notification CreateNotification()
        {
            return BuildNotification("Usage Meter Active", "Initializing monitoring...");
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
                .SetPriority(NotificationCompat.PriorityLow);

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

        public async Task StartAsync()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var intent = new Intent(context, typeof(AndroidForegroundService));

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    context.StartForegroundService(intent);
                }
                else
                {
                    context.StartService(intent);
                }

                System.Diagnostics.Debug.WriteLine("Foreground service start requested");
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

                // Method 3: Try to use system alert window if permission available
                await TryShowSystemOverlay(context, rule);

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

                // Make it urgent
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    builder.SetChannelId("planapp_urgent_channel");

                    // Create urgent channel
                    var notificationManager = NotificationManager.FromContext(context);
                    var urgentChannel = new NotificationChannel(
                        "planapp_urgent_channel",
                        "Urgent Alerts",
                        NotificationImportance.High)
                    {
                        Description = "Urgent rule blocking alerts"
                    };
                    urgentChannel.EnableVibration(true);
                    urgentChannel.EnableLights(true);
                    notificationManager?.CreateNotificationChannel(urgentChannel);
                }

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

                var notificationManager2 = NotificationManager.FromContext(context);
                notificationManager2?.Notify(2000 + rule.Id.GetHashCode(), builder.Build());

                System.Diagnostics.Debug.WriteLine("Urgent notification shown");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing urgent notification: {ex}");
            }
        }

        private static async Task TryShowSystemOverlay(Context context, com.usagemeter.androidapp.Models.AppRule rule)
        {
            try
            {
                // Check if we have SYSTEM_ALERT_WINDOW permission
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                {
                    if (!global::Android.Provider.Settings.CanDrawOverlays(context))
                    {
                        System.Diagnostics.Debug.WriteLine("No system alert window permission");
                        return;
                    }
                }

                // Try to move our task to front
                var activityManager = context.GetSystemService(Context.ActivityService) as ActivityManager;
                if (activityManager != null)
                {
                    var tasks = activityManager.GetRunningTasks(1);
                    if (tasks?.Count > 0)
                    {
                        activityManager.MoveTaskToFront(tasks[0].Id, 0);
                        System.Diagnostics.Debug.WriteLine("Moved task to front");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error with system overlay: {ex}");
            }
        }
    }
}