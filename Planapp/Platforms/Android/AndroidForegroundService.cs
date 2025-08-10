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

                // Start monitoring in background
                Task.Run(async () =>
                {
                    try
                    {
                        // Get services from MAUI app
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
            var builder = new NotificationCompat.Builder(this, CHANNEL_ID)
                .SetContentTitle("Usage Meter Active")
                .SetContentText("Monitoring app usage")
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityLow);

            // Create intent to open app when notification is tapped
            var intent = new Intent(this, typeof(MainActivity));
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex}");
            }
        }

        public static void ShowBlockingOverlay(com.usagemeter.androidapp.Models.AppRule rule)
        {
            try
            {
                var context = Platform.CurrentActivity ?? AndroidApp.Context;
                if (context == null) return;

                // Create an intent to bring our app to foreground with the blocking modal
                var intent = new Intent(context, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                intent.PutExtra("SHOW_BLOCK", true);
                intent.PutExtra("RULE_ID", rule.Id);

                context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing blocking overlay: {ex}");
            }
        }
    }
}