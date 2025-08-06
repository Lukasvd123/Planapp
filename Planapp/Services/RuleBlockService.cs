using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Planapp.Models;
using System.Linq;
#if ANDROID
using AndroidApp = Android.App.Application;
#endif

namespace Planapp.Services
{
    public class RuleBlockService : IRuleBlockService
    {
        private readonly ILogger<RuleBlockService> _logger;

        public event EventHandler<RuleBlockEventArgs>? RuleTriggered;
        public event EventHandler? RuleAcknowledged;

        public bool IsBlocking { get; private set; }
        public AppRule? CurrentBlockedRule { get; private set; }

        public RuleBlockService(ILogger<RuleBlockService> logger)
        {
            _logger = logger;
        }

        public async Task TriggerRuleBlock(AppRule rule)
        {
            if (IsBlocking)
            {
                _logger.LogInformation($"Rule block already active, ignoring new trigger for rule: {rule.Name}");
                return;
            }

            _logger.LogInformation($"Triggering rule block for rule: {rule.Name}");

#if ANDROID
            // Show debug notification for rule trigger
            Planapp.Platforms.Android.AndroidNotificationHelper.ShowRuleTriggeredNotification(
                rule.Name, 
                string.Join(", ", rule.SelectedAppNames.Take(2))
            );
#endif

            IsBlocking = true;
            CurrentBlockedRule = rule;

            // Kill the current app first
            await KillCurrentForegroundApp();

            // Fire the rule triggered event to show modal
            RuleTriggered?.Invoke(this, new RuleBlockEventArgs { Rule = rule });

            await Task.CompletedTask;
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
            IsBlocking = false;
            CurrentBlockedRule = null;

            // If the rule action is to open an app, do it now
            if (acknowledgedRule.ActionType == "OpenApp" && !string.IsNullOrEmpty(acknowledgedRule.TargetPackage))
            {
                await OpenSpecificApp(acknowledgedRule.TargetPackage);
            }
            else
            {
                // Just go to home screen
                await GoToHome();
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
                _logger.LogInformation("Attempting to go to home screen (kill current app)");
                await GoToHome();
                
                // Give some time for the home screen to load
                await Task.Delay(1500);
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
                    
                    // Show debug notification about failed launch
                    Planapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                        "Launch Failed", 
                        $"Could not launch {packageName}"
                    );
                    
                    await GoToHome();
                }
                else
                {
                    _logger.LogInformation($"Successfully launched {packageName}");
                    
                    // Show debug notification about successful launch
                    var appName = Planapp.Platforms.Android.UsageStatsHelper.GetAppName(packageName);
                    Planapp.Platforms.Android.AndroidNotificationHelper.ShowAppLaunchNotification(
                        "App Launched", 
                        $"Successfully opened {appName}"
                    );
                }
#else
                _logger.LogInformation($"App opening not supported on this platform: {packageName}");
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening specific app: {packageName}");
                await GoToHome();
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

        private async Task GoToHome()
        {
            try
            {
                var context = Platform.CurrentActivity ?? AndroidApp.Context;
                if (context == null)
                {
                    _logger.LogError("Android context not available for home navigation"); 
                    return;
                }

                var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | 
                                   Android.Content.ActivityFlags.ClearTop |
                                   Android.Content.ActivityFlags.ResetTaskIfNeeded);

                context.StartActivity(homeIntent);
                _logger.LogInformation("Successfully navigated to home screen");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating to home screen");
            }

            await Task.CompletedTask;
        }
#endif
    }
}