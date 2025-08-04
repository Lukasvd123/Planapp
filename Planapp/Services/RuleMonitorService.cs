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
        private readonly object _lockObject = new object();

        public RuleMonitorService(IServiceProvider serviceProvider, ILogger<RuleMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_lockObject)
            {
                if (_isRunning)
                {
                    _logger.LogInformation("Rule monitoring already started");
                    return;
                }
                _isRunning = true;
            }

            try
            {
                _logger.LogInformation("Starting rule monitoring service...");

                // Get app launch monitor from DI
                _appLaunchMonitor = _serviceProvider.GetRequiredService<IAppLaunchMonitor>();

                // Subscribe to app launch events
                _appLaunchMonitor.AppLaunched += OnAppLaunched;

                // Start monitoring app launches
                await _appLaunchMonitor.StartMonitoringAsync();

                _logger.LogInformation("Rule monitoring service started successfully - listening for app launches");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting rule monitoring service");
                lock (_lockObject)
                {
                    _isRunning = false;
                }
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                {
                    _logger.LogInformation("Rule monitoring already stopped");
                    return;
                }
                _isRunning = false;
            }

            try
            {
                _logger.LogInformation("Stopping rule monitoring service...");

                if (_appLaunchMonitor != null)
                {
                    _appLaunchMonitor.AppLaunched -= OnAppLaunched;
                    await _appLaunchMonitor.StopMonitoringAsync();
                    _appLaunchMonitor = null;
                }

                _logger.LogInformation("Rule monitoring service stopped successfully");
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
                _logger.LogInformation($"Processing app launch: {e.AppName} ({e.PackageName})");

                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var blockService = scope.ServiceProvider.GetRequiredService<IRuleBlockService>();

                // Skip if already blocking
                if (blockService.IsBlocking)
                {
                    _logger.LogInformation("Rule block already active, skipping new app launch");
                    return;
                }

                // Get all enabled rules
                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                _logger.LogDebug($"Found {enabledRules.Count} enabled rules to check");

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
                    var shouldTrigger = await CheckAndTriggerRule(rule, ruleService, blockService, e.AppName);
                    if (shouldTrigger)
                    {
                        // Only trigger the first matching rule
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling app launch: {e.AppName}");
            }
        }

        private async Task<bool> CheckAndTriggerRule(AppRule rule, IRuleService ruleService, IRuleBlockService blockService, string launchedAppName)
        {
            try
            {
                _logger.LogDebug($"Checking rule: {rule.Name}");

                // Check if rule has already been triggered recently (within the last hour to prevent spam)
                var timeSinceLastTrigger = DateTime.Now - rule.LastTriggered;
                if (timeSinceLastTrigger.TotalMinutes < 60)
                {
                    _logger.LogDebug($"Rule '{rule.Name}' triggered recently ({timeSinceLastTrigger.TotalMinutes:F1} minutes ago), skipping");
                    return false;
                }

                // Get current usage for all apps in this rule
                var currentUsage = await ruleService.GetCombinedUsageForAppsAsync(rule.SelectedPackages);

                _logger.LogInformation($"Rule '{rule.Name}': Current usage {currentUsage}ms ({FormatMilliseconds(currentUsage)}), Threshold: {rule.ThresholdInMilliseconds}ms ({FormatMilliseconds(rule.ThresholdInMilliseconds)})");

                // Check if threshold is exceeded
                if (currentUsage >= rule.ThresholdInMilliseconds)
                {
                    _logger.LogWarning($"Rule '{rule.Name}' threshold exceeded! Triggering block for app: {launchedAppName}");

                    // Update last triggered time
                    rule.LastTriggered = DateTime.Now;
                    await ruleService.SaveRuleAsync(rule);

                    // Trigger the blocking action
                    await blockService.TriggerRuleBlock(rule);

                    return true; // Rule was triggered
                }
                else
                {
                    var remainingTime = rule.ThresholdInMilliseconds - currentUsage;
                    _logger.LogDebug($"Rule '{rule.Name}' not triggered. Remaining time: {FormatMilliseconds(remainingTime)}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking rule '{rule.Name}'");
                return false;
            }
        }

        private string FormatMilliseconds(long milliseconds)
        {
            var timeSpan = TimeSpan.FromMilliseconds(milliseconds);

            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            else
                return $"{timeSpan.Seconds}s";
        }
    }
}