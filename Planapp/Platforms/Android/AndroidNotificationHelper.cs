using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Android.OS;
using AndroidApp = Android.App.Application;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using com.usagemeter.androidapp.Services;

namespace com.usagemeter.androidapp.Platforms.Android
{
    public static class AndroidNotificationHelper
    {
        private const string CHANNEL_ID = "planapp_debug_channel";
        private const string CHANNEL_NAME = "Usage Meter Debug";
        private const int BASE_NOTIFICATION_ID = 1001;

        private static readonly ConcurrentDictionary<int, DateTime> ActiveNotifications = new();
        private static System.Threading.Timer? CleanupTimer;
        private static ISettingsService? _settingsService;

        public static void Initialize(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public static void InitializeNotificationChannel()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    var notificationManager = NotificationManager.FromContext(context);
                    if (notificationManager == null) return;

                    var existingChannel = notificationManager.GetNotificationChannel(CHANNEL_ID);
                    if (existingChannel != null) return;

                    var channel = new NotificationChannel(CHANNEL_ID, CHANNEL_NAME, NotificationImportance.Low)
                    {
                        Description = "Debug notifications for Usage Meter"
                    };

                    notificationManager.CreateNotificationChannel(channel);
                    StartCleanupTimer();

                    DebugLog("✅ Notification channel created");
                }
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error creating notification channel: {ex.Message}");
            }
        }

        private static void StartCleanupTimer()
        {
            CleanupTimer?.Dispose();
            CleanupTimer = new System.Threading.Timer(CleanupOldNotifications, null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private static void CleanupOldNotifications(object? state)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null) return;

                var now = DateTime.Now;
                var toRemove = new List<int>();

                foreach (var kvp in ActiveNotifications)
                {
                    if ((now - kvp.Value).TotalMinutes >= 1)
                    {
                        notificationManager.Cancel(kvp.Key);
                        toRemove.Add(kvp.Key);
                        DebugLog($"🧹 Auto-cleaned notification {kvp.Key}");
                    }
                }

                foreach (var id in toRemove)
                {
                    ActiveNotifications.TryRemove(id, out _);
                }
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error cleaning notifications: {ex.Message}");
            }
        }

        public static async void ShowAppLaunchNotification(string title, string content)
        {
            if (!await ShouldShowDebugNotification("app_launch")) return;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    DebugLog("❌ Context is null, cannot show notification");
                    return;
                }

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null)
                {
                    DebugLog("❌ NotificationManager is null");
                    return;
                }

                var notificationId = BASE_NOTIFICATION_ID + content.GetHashCode();

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                    .SetContentTitle($"🔍 {title}")
                    .SetContentText(content)
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"{content}\n⏰ {DateTime.Now:HH:mm:ss}"))
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetAutoCancel(true)
                    .SetTimeoutAfter(60000);

                var intent = new Intent(context, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

                var pendingIntent = PendingIntent.GetActivity(
                    context, 0, intent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ?
                        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable :
                        PendingIntentFlags.UpdateCurrent);

                builder.SetContentIntent(pendingIntent);
                notificationManager.Notify(notificationId, builder.Build());
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                DebugLog($"📱 Debug notification shown: {title} - {content}");
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error showing notification: {ex.Message}");
            }
        }

        public static async void ShowRuleTriggeredNotification(string ruleName, string appName)
        {
            if (!await ShouldShowDebugNotification("rule_trigger")) return;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null) return;

                var notificationId = BASE_NOTIFICATION_ID + 1000 + ruleName.GetHashCode();

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogAlert)
                    .SetContentTitle($"🚨 Rule Triggered: {ruleName}")
                    .SetContentText($"Blocked {appName} - limit exceeded")
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"Rule: {ruleName}\nBlocked: {appName}\nTime: {DateTime.Now:HH:mm:ss}"))
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultAll)
                    .SetTimeoutAfter(60000);

                notificationManager.Notify(notificationId, builder.Build());
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                DebugLog($"🚨 Rule notification shown: {ruleName} blocked {appName}");
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error showing rule notification: {ex.Message}");
            }
        }

        public static async void ShowCurrentAppNotification(string currentApp, bool isMonitored)
        {
            if (!await ShouldShowDebugNotification("app_monitor")) return;

            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null) return;

                var notificationId = BASE_NOTIFICATION_ID + 2000;

                var icon = isMonitored ? "🎯" : "👁️";
                var status = isMonitored ? "MONITORED" : "Not monitored";
                var title = $"{icon} Current App: {currentApp}";
                var content = $"Status: {status} | {DateTime.Now:HH:mm:ss}";

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                    .SetContentTitle(title)
                    .SetContentText(content)
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"Current app: {currentApp}\nStatus: {status}\nTime: {DateTime.Now:HH:mm:ss}"))
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetAutoCancel(true)
                    .SetOngoing(false)
                    .SetTimeoutAfter(60000);

                notificationManager.Notify(notificationId, builder.Build());
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                DebugLog($"📱 Current app notification: {currentApp} ({status})");
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error showing current app notification: {ex.Message}");
            }
        }

        public static void ClearAllNotifications()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return;

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null) return;

                notificationManager.CancelAll();
                ActiveNotifications.Clear();

                DebugLog("🧹 All notifications cleared");
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error clearing notifications: {ex.Message}");
            }
        }

        private static async Task<bool> ShouldShowDebugNotification(string type)
        {
            try
            {
                if (_settingsService == null) return false;

                var settings = await _settingsService.GetSettingsAsync();

                if (!settings.DebugMode || !settings.ShowDebugNotifications)
                    return false;

                return type switch
                {
                    "app_launch" => settings.ShowAppLaunchNotifications,
                    "rule_trigger" => settings.ShowRuleCheckNotifications,
                    "app_monitor" => settings.ShowAppLaunchNotifications,
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private static async void DebugLog(string message)
        {
            try
            {
                if (_settingsService != null)
                {
                    var settings = await _settingsService.GetSettingsAsync();
                    if (settings.DebugMode && settings.VerboseLogging)
                    {
                        System.Diagnostics.Debug.WriteLine(message);
                    }
                }
            }
            catch
            {
                // Fail silently
            }
        }

        public static bool CheckNotificationPermission()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return false;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                        context,
                        global::Android.Manifest.Permission.PostNotifications) ==
                        global::Android.Content.PM.Permission.Granted;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void RequestNotificationPermission()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    var activity = Platform.CurrentActivity as AndroidX.AppCompat.App.AppCompatActivity;
                    if (activity != null)
                    {
                        ActivityCompat.RequestPermissions(
                            activity,
                            new[] { global::Android.Manifest.Permission.PostNotifications },
                            1001);
                    }
                }
            }
            catch (System.Exception ex)
            {
                DebugLog($"❌ Error requesting notification permission: {ex.Message}");
            }
        }
    }
}