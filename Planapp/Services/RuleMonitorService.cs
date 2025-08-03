using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planapp.Models;

namespace Planapp.Services
{
    public class RuleMonitorService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RuleMonitorService> _logger;
        private IAppLaunchMonitor? _appLaunchMonitor;
        private bool _isRunning = false;

        public RuleMonitorService(IServiceProvider serviceProvider, ILogger<RuleMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                _logger.LogInformation("Rule monitoring already started");
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                _appLaunchMonitor = scope.ServiceProvider.GetRequiredService<IAppLaunchMonitor>();

                // Subscribe to app launch events
                _appLaunchMonitor.AppLaunched += OnAppLaunched;

                // Start monitoring app launches
                await _appLaunchMonitor.StartMonitoringAsync();

                _logger.LogInformation("Rule monitoring started - listening for app launches");
                _isRunning = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting rule monitoring service");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning)
            {
                _logger.LogInformation("Rule monitoring already stopped");
                return;
            }

            try
            {
                if (_appLaunchMonitor != null)
                {
                    _appLaunchMonitor.AppLaunched -= OnAppLaunched;
                    await _appLaunchMonitor.StopMonitoringAsync();
                }

                _logger.LogInformation("Rule monitoring stopped");
                _isRunning = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping rule monitoring service");
            }
        }

        private async void OnAppLaunched(object? sender, AppLaunchEventArgs e)
        {
            try
            {
                _logger.LogInformation($"App launched: {e.AppName} ({e.PackageName})");

                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var blockService = scope.ServiceProvider.GetRequiredService<IRuleBlockService>();

                // Get all enabled rules
                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                // Check if the launched app is monitored by any rule
                var relevantRules = enabledRules
                    .Where(rule => rule.SelectedPackages.Contains(e.PackageName))
                    .ToList();

                if (!relevantRules.Any())
                {
                    _logger.LogDebug($"No rules monitoring app: {e.AppName}");
                    return;
                }

                _logger.LogInformation($"Found {relevantRules.Count} rules monitoring app: {e.AppName}");

                // Check each relevant rule
                foreach (var rule in relevantRules)
                {
                    await CheckAndTriggerRule(rule, ruleService, blockService, e.AppName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling app launch: {e.AppName}");
            }
        }

        private async Task CheckAndTriggerRule(AppRule rule, IRuleService ruleService, IRuleBlockService blockService, string launchedAppName)
        {
            try
            {
                // Check if rule has already been triggered today
                if (rule.LastTriggered.Date == DateTime.Today)
                {
                    _logger.LogDebug($"Rule '{rule.Name}' already triggered today, skipping");
                    return;
                }

                // Get current usage for all apps in this rule
                var currentUsage = await ruleService.GetCombinedUsageForAppsAsync(rule.SelectedPackages);

                _logger.LogInformation($"Rule '{rule.Name}': Current usage {currentUsage}ms, Threshold: {rule.ThresholdInMilliseconds}ms");

                // Check if threshold is exceeded
                if (currentUsage >= rule.ThresholdInMilliseconds)
                {
                    _logger.LogInformation($"Rule '{rule.Name}' threshold exceeded! Triggering block for app: {launchedAppName}");

                    // Update last triggered time
                    rule.LastTriggered = DateTime.Now;
                    await ruleService.SaveRuleAsync(rule);

                    // Trigger the blocking modal
                    await blockService.TriggerRuleBlock(rule);
                }
                else
                {
                    var remainingTime = rule.ThresholdInMilliseconds - currentUsage;
                    _logger.LogDebug($"Rule '{rule.Name}' not triggered. Remaining time: {remainingTime}ms");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking rule '{rule.Name}'");
            }
        }
    }
}