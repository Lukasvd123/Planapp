using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Planapp.Models;

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

            IsBlocking = true;
            CurrentBlockedRule = rule;

            // Kill the current app first if on Android
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

                var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    _logger.LogWarning("Android context not available for killing app");
                    return;
                }

                // Create home intent to go back to launcher
                var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | 
                                  Android.Content.ActivityFlags.ClearTop |
                                  Android.Content.ActivityFlags.ResetTaskIfNeeded);

                context.StartActivity(homeIntent);
                _logger.LogInformation("Successfully sent user to home screen");
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
                var success = await TryMultipleLaunchMethods(packageName);
                
                if (!success)
                {
                    _logger.LogWarning($"All launch methods failed for {packageName}, going to home");
                    await GoToHome();
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
        private async Task<bool> TryMultipleLaunchMethods(string packageName)
        {
            var methods = new Func<string, Task<bool>>[]
            {
                TryLaunchWithPackageManager,
                TryLaunchWithMainIntent,
                TryLaunchWithCategoryLauncher,
                TryLaunchWithApplicationInfo
            };

            foreach (var method in methods)
            {
                try
                {
                    var success = await method(packageName);
                    if (success)
                    {
                        _logger.LogInformation($"Successfully launched {packageName} using method: {method.Method.Name}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Launch method {method.Method.Name} failed for {packageName}: {ex.Message}");
                }
            }

            return false;
        }

        private async Task<bool> TryLaunchWithPackageManager(string packageName)
        {
            var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context?.PackageManager == null) return false;

            var intent = context.PackageManager.GetLaunchIntentForPackage(packageName);
            if (intent == null) return false;

            intent.AddFlags(Android.Content.ActivityFlags.NewTask | 
                           Android.Content.ActivityFlags.ClearTop |
                           Android.Content.ActivityFlags.ResetTaskIfNeeded |
                           Android.Content.ActivityFlags.SingleTop);

            context.StartActivity(intent);
            await Task.Delay(500); // Give it time to launch
            return true;
        }

        private async Task<bool> TryLaunchWithMainIntent(string packageName)
        {
            var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context?.PackageManager == null) return false;

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
            intent.AddCategory(Android.Content.Intent.CategoryLauncher);
            intent.SetPackage(packageName);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask | 
                           Android.Content.ActivityFlags.ClearTop |
                           Android.Content.ActivityFlags.ResetTaskIfNeeded);

            var activities = context.PackageManager.QueryIntentActivities(intent, 0);
            if (activities == null || activities.Count == 0) return false;

            var activityInfo = activities[0].ActivityInfo;
            intent.SetClassName(packageName, activityInfo.Name);
            
            context.StartActivity(intent);
            await Task.Delay(500);
            return true;
        }

        private async Task<bool> TryLaunchWithCategoryLauncher(string packageName)
        {
            var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context?.PackageManager == null) return false;

            var intent = new Android.Content.Intent();
            intent.SetAction(Android.Content.Intent.ActionMain);
            intent.AddCategory(Android.Content.Intent.CategoryLauncher);
            intent.SetPackage(packageName);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask |
                           Android.Content.ActivityFlags.ResetTaskIfNeeded);

            try
            {
                context.StartActivity(intent);
                await Task.Delay(500);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryLaunchWithApplicationInfo(string packageName)
        {
            var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context?.PackageManager == null) return false;

            try
            {
                var appInfo = context.PackageManager.GetApplicationInfo(packageName, 0);
                var intent = context.PackageManager.GetLaunchIntentForPackage(packageName);
                
                if (intent == null)
                {
                    // Try to create intent manually
                    intent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                    intent.AddCategory(Android.Content.Intent.CategoryLauncher);
                    intent.SetPackage(packageName);
                }

                intent.AddFlags(Android.Content.ActivityFlags.NewTask |
                               Android.Content.ActivityFlags.ClearTop |
                               Android.Content.ActivityFlags.BroughtToFront);

                context.StartActivity(intent);
                await Task.Delay(500);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task GoToHome()
        {
            try
            {
                var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
                if (context == null) return;

                var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | 
                                   Android.Content.ActivityFlags.ClearTop |
                                   Android.Content.ActivityFlags.ResetTaskIfNeeded);

                context.StartActivity(homeIntent);
                _logger.LogInformation("Sent user to home screen as fallback");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Even home fallback failed");
            }

            await Task.CompletedTask;
        }
#endif
    }
}