using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Android.OS;
using AndroidApp = Android.App.Application;

namespace Planapp.Platforms.Android
{
    public static class AndroidNotificationHelper
    {
        private const string CHANNEL_ID = "planapp_debug_channel";
        private const string CHANNEL_NAME = "Planapp Debug";
        private const int NOTIFICATION_ID = 1001;

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

                    // Check if channel already exists
                    var existingChannel = notificationManager.GetNotificationChannel(CHANNEL_ID);
                    if (existingChannel != null) return;

                    var channel = new NotificationChannel(CHANNEL_ID, CHANNEL_NAME, NotificationImportance.Default)
                    {
                        Description = "Debug notifications for Planapp"
                    };

                    notificationManager.CreateNotificationChannel(channel);
                    System.Diagnostics.Debug.WriteLine("Notification channel created successfully");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating notification channel: {ex.Message}");
            }
        }

        public static void ShowAppLaunchNotification(string appName, string packageName)
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null)
                {
                    System.Diagnostics.Debug.WriteLine("Context is null, cannot show notification");
                    return;
                }

                var notificationManager = NotificationManager.FromContext(context);
                if (notificationManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("NotificationManager is null");
                    return;
                }

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo) // Use built-in icon
                    .SetContentTitle("App Launched - Debug")
                    .SetContentText($"{appName} ({packageName})")
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"App: {appName}\nPackage: {packageName}\nTime: {DateTime.Now:HH:mm:ss}"))
                    .SetPriority(NotificationCompat.PriorityDefault)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultSound | NotificationCompat.DefaultVibrate);

                // Create an intent for when the notification is tapped
                var intent = new Intent(context, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

                var pendingIntent = PendingIntent.GetActivity(
                    context, 0, intent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ?
                        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable :
                        PendingIntentFlags.UpdateCurrent);

                builder.SetContentIntent(pendingIntent);

                notificationManager.Notify(NOTIFICATION_ID + packageName.GetHashCode(), builder.Build());

                System.Diagnostics.Debug.WriteLine($"Debug notification shown for app launch: {appName}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing notification: {ex.Message}");
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

                var builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo) // Use built-in icon
                    .SetContentTitle("Rule Triggered - Debug")
                    .SetContentText($"Rule '{ruleName}' triggered by {appName}")
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"Rule: {ruleName}\nTriggered by: {appName}\nTime: {DateTime.Now:HH:mm:ss}"))
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetAutoCancel(true)
                    .SetDefaults(NotificationCompat.DefaultAll);

                notificationManager.Notify(NOTIFICATION_ID + 1000, builder.Build());

                System.Diagnostics.Debug.WriteLine($"Debug notification shown for rule trigger: {ruleName}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing rule notification: {ex.Message}");
            }
        }

        public static bool CheckNotificationPermission()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? AndroidApp.Context;
                if (context == null) return false;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) // Android 13+
                {
                    return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                        context,
                        global::Android.Manifest.Permission.PostNotifications) ==
                        global::Android.Content.PM.Permission.Granted;
                }

                // For Android 12 and below, notifications are enabled by default
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
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) // Android 13+
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
                System.Diagnostics.Debug.WriteLine($"Error requesting notification permission: {ex.Message}");
            }
        }
    }
}