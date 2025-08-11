using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Android.OS;
using AndroidApp = Android.App.Application;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace com.usagemeter.androidapp.Platforms.Android
{
    public static class AndroidNotificationHelper
    {
        private const string CHANNEL_ID = "planapp_debug_channel";
        private const string CHANNEL_NAME = "Usage Meter Debug";
        private const int BASE_NOTIFICATION_ID = 1001;

        // Track notifications for auto-cleanup
        private static readonly ConcurrentDictionary<int, DateTime> ActiveNotifications = new();
        private static System.Threading.Timer? CleanupTimer;

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

                    var channel = new NotificationChannel(CHANNEL_ID, CHANNEL_NAME, NotificationImportance.Default)
                    {
                        Description = "Debug notifications for Usage Meter with auto-cleanup"
                    };

                    notificationManager.CreateNotificationChannel(channel);

                    // Start cleanup timer for 1-minute auto-delete
                    StartCleanupTimer();

                    System.Diagnostics.Debug.WriteLine("✅ Notification channel created with auto-cleanup");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating notification channel: {ex.Message}");
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
                    if ((now - kvp.Value).TotalMinutes >= 1) // 1 minute old
                    {
                        notificationManager.Cancel(kvp.Key);
                        toRemove.Add(kvp.Key);
                        System.Diagnostics.Debug.WriteLine($"🧹 Auto-cleaned notification {kvp.Key}");
                    }
                }

                foreach (var id in toRemove)
                {
                    ActiveNotifications.TryRemove(id, out _);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error cleaning notifications: {ex.Message}");
            }
        }

        public static void ShowAppLaunchNotification(string title, string content)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Context is null, cannot show notification");
                    return;
                }

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ NotificationManager is null");
                    return;
                }

                var notificationId = BASE_NOTIFICATION_ID + content.GetHashCode();

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                    .SetContentTitle($"🔍 {title}")
                    .SetContentText(content)
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"{content}\n⏰ {DateTime.Now:HH:mm:ss} (Auto-expires in 1 min)"))
                    .SetPriority(NotificationCompat.PriorityDefault)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultSound)
                    .SetTimeoutAfter(60000); // 1 minute timeout

                var intent = new Intent(context, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

                var pendingIntent = PendingIntent.GetActivity(
                    context, 0, intent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ?
                        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable :
                        PendingIntentFlags.UpdateCurrent);

                builder.SetContentIntent(pendingIntent);

                notificationManager.Notify(notificationId, builder.Build());

                // Track for cleanup
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                System.Diagnostics.Debug.WriteLine($"📱 Debug notification shown: {title} - {content}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error showing notification: {ex.Message}");
            }
        }

        public static void ShowRuleTriggeredNotification(string ruleName, string appName)
        {
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
                        .BigText($"Rule: {ruleName}\nBlocked: {appName}\nTime: {DateTime.Now:HH:mm:ss}\n\n⚠️ Usage limit exceeded (Auto-expires in 1 min)"))
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultAll)
                    .SetTimeoutAfter(60000); // 1 minute timeout

                notificationManager.Notify(notificationId, builder.Build());

                // Track for cleanup
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                System.Diagnostics.Debug.WriteLine($"🚨 Rule notification shown: {ruleName} blocked {appName}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error showing rule notification: {ex.Message}");
            }
        }

        public static void ShowCurrentAppNotification(string currentApp, bool isMonitored)
        {
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
                        .BigText($"Current app: {currentApp}\nStatus: {status}\nTime: {DateTime.Now:HH:mm:ss}\n\n(Auto-expires in 1 min)"))
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetAutoCancel(true)
                    .SetOngoing(false)
                    .SetTimeoutAfter(60000); // 1 minute timeout

                notificationManager.Notify(notificationId, builder.Build());

                // Track for cleanup
                ActiveNotifications.TryAdd(notificationId, DateTime.Now);

                System.Diagnostics.Debug.WriteLine($"📱 Current app notification: {currentApp} ({status})");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error showing current app notification: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine("🧹 All notifications cleared");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing notifications: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"❌ Error requesting notification permission: {ex.Message}");
            }
        }
    }
}