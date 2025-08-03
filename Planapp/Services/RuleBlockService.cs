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

            IsBlocking = false;
            var acknowledgedRule = CurrentBlockedRule;
            CurrentBlockedRule = null;

            RuleAcknowledged?.Invoke(this, EventArgs.Empty);

            await Task.CompletedTask;
        }

        public async Task OpenTargetApp(string packageName)
        {
            try
            {
                _logger.LogInformation($"Opening target app: {packageName}");

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
                    
                    // Acknowledge the block after opening target app
                    await AcknowledgeBlock();
                }
                else
                {
                    _logger.LogWarning($"Could not find launch intent for app: {packageName}");
                }
#else
                _logger.LogInformation($"App opening not supported on this platform: {packageName}");
                await AcknowledgeBlock();
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening target app: {packageName}");
            }
        }
    }
}