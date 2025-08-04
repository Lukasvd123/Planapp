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

                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    _logger.LogWarning("Android context not available for killing app");
                    return;
                }

                // Create home intent to go back to launcher
                var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);

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
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    _logger.LogWarning("Android context not available for opening app");
                    return;
                }

                var packageManager = context.PackageManager;
                var intent = packageManager?.GetLaunchIntentForPackage(packageName);

                if (intent != null)
                {
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);
                    context.StartActivity(intent);
                    _logger.LogInformation($"Successfully opened app: {packageName}");
                }
                else
                {
                    _logger.LogWarning($"Could not find launch intent for app: {packageName}");

                    // Fallback: try to open Nova Launcher specifically if packageName is not found
                    if (!packageName.Contains("nova"))
                    {
                        var novaIntent = packageManager?.GetLaunchIntentForPackage("com.teslacoilsw.launcher");
                        if (novaIntent != null)
                        {
                            novaIntent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);
                            context.StartActivity(novaIntent);
                            _logger.LogInformation("Opened Nova Launcher as fallback");
                        }
                        else
                        {
                            // Final fallback: go to home
                            var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                            homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                            homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);
                            context.StartActivity(homeIntent);
                            _logger.LogInformation("Opened default home as final fallback");
                        }
                    }
                }
#else
                _logger.LogInformation($"App opening not supported on this platform: {packageName}");
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening specific app: {packageName}");

                // Ultimate fallback - try to go home
                try
                {
#if ANDROID
                    var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                    if (context != null)
                    {
                        var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                        homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                        homeIntent.SetFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);
                        context.StartActivity(homeIntent);
                        _logger.LogInformation("Used home intent as ultimate fallback");
                    }
#endif
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Even home fallback failed");
                }
            }

            await Task.CompletedTask;
        }
    }
}