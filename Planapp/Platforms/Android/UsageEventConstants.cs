using Android.App.Usage;

namespace com.usagemeter.androidapp.Platforms.Android
{
    /// <summary>
    /// Helper class for Android Usage Events API
    /// Handles the enum-based event types in modern Android
    /// </summary>
    public static class UsageEventConstants
    {
        /// <summary>
        /// Gets the event type for when an activity comes to the foreground
        /// </summary>
        public static UsageEventType GetActivityResumedEvent() => UsageEventType.ActivityResumed;

        /// <summary>
        /// Gets the event type for when an activity goes to the background
        /// </summary>
        public static UsageEventType GetActivityPausedEvent() => UsageEventType.ActivityPaused;

        /// <summary>
        /// Checks if the event indicates an app coming to foreground
        /// </summary>
        public static bool IsAppForegroundEvent(UsageEventType eventType) => eventType == UsageEventType.ActivityResumed;

        /// <summary>
        /// Checks if the event indicates an app going to background
        /// </summary>
        public static bool IsAppBackgroundEvent(UsageEventType eventType) => eventType == UsageEventType.ActivityPaused;

        /// <summary>
        /// Gets a human-readable description of the event type
        /// </summary>
        public static string GetEventDescription(UsageEventType eventType)
        {
            return eventType switch
            {
                UsageEventType.ActivityResumed => "Activity Resumed (Foreground)",
                UsageEventType.ActivityPaused => "Activity Paused (Background)",
                UsageEventType.ConfigurationChange => "Configuration Change",
                UsageEventType.UserInteraction => "User Interaction",
                UsageEventType.ShortcutInvocation => "Shortcut Invocation",
                UsageEventType.StandbyBucketChanged => "Standby Bucket Changed",
                UsageEventType.ScreenInteractive => "Screen Interactive",
                UsageEventType.ScreenNonInteractive => "Screen Non-Interactive",
                UsageEventType.KeyguardShown => "Keyguard Shown",
                UsageEventType.KeyguardHidden => "Keyguard Hidden",
                UsageEventType.ForegroundServiceStart => "Foreground Service Start",
                UsageEventType.ForegroundServiceStop => "Foreground Service Stop",
                _ => $"Unknown Event ({eventType})"
            };
        }
    }
}