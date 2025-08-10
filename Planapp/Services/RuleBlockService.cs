using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Models;
using System.Linq;
#if ANDROID
using AndroidApp = Android.App.Application;
#endif

namespace com.usagemeter.androidapp.Services
{
    public class RuleBlockService : IRuleBlockService
    {
        private readonly ILogger<RuleBlockService> _logger;
        private readonly ISettingsService _settingsService;

        public event EventHandler<RuleBlockEventArgs>? RuleTriggered;
        public event EventHandler? RuleAcknowledged;

        public bool IsBlocking { get; private set; }
        public AppRule? CurrentBlockedRule { get; private set; }

        public RuleBlockService(ILogger<RuleBlockService> logger, ISettingsService settingsService)
        {
            _logger = logger;
            _settingsService = settingsService;
        }

        public async Task TriggerRuleBlock(AppRule rule)
        {
            if (IsBlocking)
            {
                _logger.LogInformation($"Rule block already active, ignoring new trigger for rule: {rule.Name}");
                return;
            }

            _logger.LogInformation($"Triggering rule block for rule: {rule.Name} with action: {rule.ActionType}");

            var settings = await _settingsService.GetSettingsAsync();

#if ANDROID
            // Show notification if enabled
            if (settings.ShowNotifications)
            {
                com.usagemeter.androidapp.Platforms.Android.AndroidNotificationHelper.ShowRuleTriggeredNotification(
                    rule.Name,
                    string.Join(", ", rule.SelectedAppNames.Take(2))
                );
            }

            // Vibrate if enabled
            if (settings.VibrationEnabled)
            {
                try
                {
                    var vibrator = Platform.CurrentActivity?.GetSystemService(Android.Content.Context.VibratorService) as Android.OS.Vibrator;
                    if (vibrator?.HasVibrator == true)
                    {
                        vibrator.Vibrate(Android.OS.VibrationEffect.CreateOneShot(500, Android.OS.VibrationEffect.DefaultAmplitude));
                    }
                }
                catch { }
            }

            // Bring app to foreground to show blocking modal
            com.usagemeter.androidapp.Platforms.Android.AndroidForegroundService.ShowBlockingOverlay(rule);
#endif

            IsBlocking = true;
            CurrentBlockedRule = rule;

            // Handle based on rule's action type and global blocking mode
            var effectiveBlockingMode = DetermineEffectiveBlockingMode(rule, settings);

            switch (effectiveBlockingMode)
            {
                case "Instant":
                    // Kill app immediately and go home
                    await KillCurrentForegroundApp();
                    await GoToHome(settings.HomeAppPackage);
                    IsBlocking = false;
                    CurrentBlockedRule = null;
                    break;

                case "Timer":
                    // Show blocking screen with timer
                    await KillCurrentForegroundApp();
                    RuleTriggered?.Invoke(this, new RuleBlockEventArgs { Rule = rule });
                    break;

                case "Choice":
                    // Show modal with options
                    await KillCurrentForegroundApp();
                    RuleTriggered?.Invoke(this, new RuleBlockEventArgs { Rule = rule });
                    break;

                case "OpenApp":
                    // Open target app directly
                    await KillCurrentForegroundApp();
                    if (!string.IsNullOrEmpty(rule.TargetPackage))
                    {
                        await OpenSpecificApp(rule.TargetPackage);
                    }
                    else
                    {
                        await GoToHome(settings.HomeAppPackage);
                    }
                    IsBlocking = false;
                    CurrentBlockedRule = null;
                    break;

                default:
                    // Fallback to choice mode
                    await KillCurrentForegroundApp();
                    RuleTriggered?.Invoke(this, new RuleBlockEventArgs { Rule = rule });
                    break;
            }

            await Task.CompletedTask;
        }

        private string DetermineEffectiveBlockingMode(AppRule rule, AppSettings settings)
        {
            // Rule-specific action takes precedence over global settings
            switch (rule.ActionType)
            {
                case "Timer":
                    return "Timer";
                case "Instant":
                    return "Instant";
                case "OpenApp":
                    return "OpenApp";
                case "Choice":
                    return "Choice";
                // Legacy support
                case "LockInApp":
                    return "Timer";
                default:
                    // Fall back to global settings if rule doesn't specify
                    return settings.BlockingMode;
            }
        }

        public async Task AcknowledgeBlock()
        {
            if (!IsBlocking || CurrentBlockedRule == null)
            {
                _logger.LogWarning("Attempted to acknowledge block when no block is active");
                return;
            }

            _logger.LogInformation($"Rule block acknowledged for rule: {CurrentBlockedRule.Name}");

            var acknowledgedRule = CurrentBlockedRule;
            var settings = await _settingsService.GetSettingsAsync();

            IsBlocking = false;
            CurrentBlockedRule = null;

            // If the rule action is to open an app, do it now
            if (acknowledgedRule.ActionType == "OpenApp" && !string.IsNullOrEmpty(acknowledgedRule.TargetPackage))
            {
                await OpenSpecificApp(acknowledgedRule.TargetPackage);
            }
            else
            {
                // Go to configured home app
                await GoToHome(settings.HomeAppPackage);
            }

            RuleAcknowledged?.Invoke(this, EventArgs.Empty);

            await Task.CompletedTask;
        }

        public async Task OpenTargetApp(string packageName)
        {
            await OpenSpecificApp(packageName);
            await AcknowledgeBlock();
        }

        private async Task KillCurrentForegroundApp()
        {
            try
            {
#if ANDROID
                _logger.LogInformation("Attempting to minimize current app");

                // Move task to back instead of killing
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    activity.MoveTaskToBack(true);
                    await Task.Delay(500);
                }
#else
                _logger.LogInformation("App killing not supported on this platform");
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error killing current foreground app");
            }

            await Task.CompletedTask;
        }

        private async Task GoToHome(string? homePackage = null)
        {
            try
            {
#if ANDROID
                var context = Platform.CurrentActivity ?? AndroidApp.Context;
                if (context == null)
                {
                    _logger.LogError("Android context not available for home navigation");
                    return;
                }

                // Try to use configured home app first
                if (!string.IsNullOrEmpty(homePackage))
                {
                    var success = await TryLaunchApp(homePackage);
                    if (success) return;
                }

                // Fallback to default home
                var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask |
                                   Android.Content.ActivityFlags.ClearTop |
                                   Android.Content.ActivityFlags.ResetTaskIfNeeded);

                context.StartActivity(homeIntent);
                _logger.LogInformation("Successfully navigated to home screen");
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating to home screen");
            }

            await Task.CompletedTask;
        }

        private async Task OpenSpecificApp(string packageName)
        {
            try
            {
                _logger.LogInformation($"Opening specific app: {packageName}");

#if ANDROID
                var success = await TryLaunchApp(packageName);

                if (!success)
                {
                    _logger.LogWarning($"Failed to launch {packageName}, going to home");

                    var settings = await _settingsService.GetSettingsAsync();
                    if (settings.ShowNotifications)
                    {
                        com.usagemeter.androidapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                            "Launch Failed",
                            $"Could not launch {packageName}"
                        );
                    }

                    await GoToHome(settings.HomeAppPackage);
                }
                else
                {
                    _logger.LogInformation($"Successfully launched {packageName}");

                    var settings = await _settingsService.GetSettingsAsync();
                    if (settings.ShowNotifications)
                    {
                        var appName = com.usagemeter.androidapp.Platforms.Android.UsageStatsHelper.GetAppName(packageName);
                        com.usagemeter.androidapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                            "App Launched",
                            $"Successfully opened {appName}"
                        );
                    }
                }
#else
                _logger.LogInformation($"App opening not supported on this platform: {packageName}");
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening specific app: {packageName}");
                var settings = await _settingsService.GetSettingsAsync();
                await GoToHome(settings.HomeAppPackage);
            }

            await Task.CompletedTask;
        }

#if ANDROID
        private async Task<bool> TryLaunchApp(string packageName)
        {
            try
            {
                var context = Platform.CurrentActivity ?? AndroidApp.Context;
                if (context?.PackageManager == null)
                {
                    _logger.LogError("Android context or PackageManager not available");
                    return false;
                }

                _logger.LogDebug($"Attempting to launch {packageName}");

                // Method 1: Use PackageManager.GetLaunchIntentForPackage
                var intent = context.PackageManager.GetLaunchIntentForPackage(packageName);
                if (intent != null)
                {
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask |
                                   Android.Content.ActivityFlags.ClearTop |
                                   Android.Content.ActivityFlags.ResetTaskIfNeeded);

                    context.StartActivity(intent);
                    await Task.Delay(1500); // Give it time to launch

                    _logger.LogInformation($"Successfully launched {packageName} using launch intent");
                    return true;
                }

                _logger.LogDebug($"No launch intent found for {packageName}");

                // Method 2: Try to create intent manually
                var mainIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                mainIntent.AddCategory(Android.Content.Intent.CategoryLauncher);
                mainIntent.SetPackage(packageName);
                mainIntent.AddFlags(Android.Content.ActivityFlags.NewTask |
                                   Android.Content.ActivityFlags.ClearTop);

                var activities = context.PackageManager.QueryIntentActivities(mainIntent, 0);
                if (activities != null && activities.Count > 0)
                {
                    var activityInfo = activities[0].ActivityInfo;
                    mainIntent.SetClassName(packageName, activityInfo.Name);

                    context.StartActivity(mainIntent);
                    await Task.Delay(1500);

                    _logger.LogInformation($"Successfully launched {packageName} using activity intent");
                    return true;
                }

                _logger.LogWarning($"No launchable activities found for {packageName}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error launching app {packageName}: {ex.Message}");
                return false;
            }
        }
#endif
    }
}