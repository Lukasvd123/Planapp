using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using com.usagemeter.androidapp.Models;

#if ANDROID
using com.usagemeter.androidapp.Platforms.Android;
#endif

namespace com.usagemeter.androidapp.Services
{
    public class RuleMonitorService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RuleMonitorService> _logger;
        private bool _isRunning = false;
        private readonly object _lockObject = new object();
        private Timer? _periodicCheckTimer;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly Dictionary<string, DateTime> _ruleLastTriggered = new();

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
                _logger.LogInformation("Starting controlled rule monitoring service...");

                _cancellationTokenSource = new CancellationTokenSource();

                // Get app launch monitor from DI
                var _appLaunchMonitor = _serviceProvider.GetRequiredService<IAppLaunchMonitor>();

                // Subscribe to app launch events
                _appLaunchMonitor.AppLaunched += OnAppLaunched;

                // Start monitoring app launches
                await _appLaunchMonitor.StartMonitoringAsync();

                // Start less aggressive periodic checking (every 5 minutes for background usage)
                _periodicCheckTimer = new Timer(
                    async _ => await PeriodicRuleCheck(),
                    null,
                    TimeSpan.FromMinutes(2),  // Start after 2 minutes
                    TimeSpan.FromMinutes(5)   // Check every 5 minutes
                );

                _logger.LogInformation($"Controlled rule monitoring started - Launch monitor: {_appLaunchMonitor.IsMonitoring}");

#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Rule Monitor Started (Controlled)",
                    "Monitoring actual app launches with proper cooldowns"
                );
#endif
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

                _cancellationTokenSource?.Cancel();
                _periodicCheckTimer?.Dispose();

                _logger.LogInformation("Rule monitoring service stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping rule monitoring service");
            }
        }

        private async Task PeriodicRuleCheck()
        {
            if (!_isRunning) return;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var blockService = scope.ServiceProvider.GetRequiredService<IRuleBlockService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                var settings = await settingsService.GetSettingsAsync();
                if (!settings.AllRulesEnabled)
                {
                    _logger.LogDebug("Rules disabled, skipping periodic check");
                    return;
                }

                if (blockService.IsBlocking)
                {
                    _logger.LogDebug("Already blocking, skipping periodic check");
                    return;
                }

                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                _logger.LogDebug($"Periodic background check: {enabledRules.Count} enabled rules");

                foreach (var rule in enabledRules)
                {
                    // Check if rule was triggered recently (longer cooldown for periodic checks)
                    if (_ruleLastTriggered.TryGetValue(rule.Id, out var lastTriggered))
                    {
                        var timeSinceLastTrigger = DateTime.Now - lastTriggered;
                        if (timeSinceLastTrigger.TotalHours < 1) // 1 hour cooldown for periodic checks
                        {
                            _logger.LogDebug($"Skipping periodic check for rule '{rule.Name}' - triggered {timeSinceLastTrigger.TotalMinutes:F1} minutes ago");
                            continue;
                        }
                    }

                    var currentUsage = await ruleService.GetCombinedUsageForAppsAsync(rule.SelectedPackages);

                    // Only trigger on periodic check if usage is significantly over the limit (give some buffer)
                    var bufferThreshold = rule.ThresholdInMilliseconds + (10 * 60 * 1000); // 10 minute buffer

                    if (currentUsage >= bufferThreshold)
                    {
                        _logger.LogWarning($"⏰ PERIODIC TRIGGER: Rule '{rule.Name}' significantly exceeded threshold ({FormatTime(currentUsage)} >= {FormatTime(bufferThreshold)})");

#if ANDROID
                        AndroidNotificationHelper.ShowRuleTriggeredNotification(
                            rule.Name,
                            $"Background trigger - {FormatTime(currentUsage)} used"
                        );
#endif

                        _ruleLastTriggered[rule.Id] = DateTime.Now;
                        await blockService.TriggerRuleBlock(rule);
                        break; // Only trigger one rule at a time
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic rule check");
            }
        }

        private async void OnAppLaunched(object? sender, AppLaunchEventArgs e)
        {
            try
            {
                _logger.LogInformation($"🚀 APP LAUNCHED: {e.AppName} ({e.PackageName}) at {e.LaunchedAt:HH:mm:ss}");

                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var blockService = scope.ServiceProvider.GetRequiredService<IRuleBlockService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                var settings = await settingsService.GetSettingsAsync();
                if (!settings.AllRulesEnabled)
                {
                    _logger.LogInformation("Rules are globally disabled - skipping rule check");
                    return;
                }

                if (blockService.IsBlocking)
                {
                    _logger.LogInformation("Rule block already active, skipping new app launch");
                    return;
                }

                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                _logger.LogInformation($"Found {enabledRules.Count} enabled rules to check against {e.AppName}");

                var relevantRules = enabledRules
                    .Where(rule => rule.SelectedPackages.Contains(e.PackageName))
                    .ToList();

                if (!relevantRules.Any())
                {
                    _logger.LogInformation($"✅ No rules monitoring app: {e.AppName} - app launch allowed");
                    return;
                }

                _logger.LogInformation($"⚠️ Found {relevantRules.Count} rules monitoring app: {e.AppName}");

#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    $"Rule Match Found",
                    $"{relevantRules.Count} rules monitor {e.AppName} - checking limits"
                );
#endif

                foreach (var rule in relevantRules)
                {
                    var shouldTrigger = await CheckAndTriggerRule(rule, ruleService, blockService, e.AppName);
                    if (shouldTrigger)
                    {
                        _logger.LogWarning($"🔥 RULE TRIGGERED: {rule.Name} for app {e.AppName}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling app launch: {e.AppName}");
#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Rule Monitor Error",
                    $"Error processing {e.AppName}: {ex.Message}"
                );
#endif
            }
        }

        private async Task<bool> CheckAndTriggerRule(AppRule rule, IRuleService ruleService, IRuleBlockService blockService, string launchedAppName)
        {
            try
            {
                _logger.LogInformation($"🔍 Checking rule: '{rule.Name}' (threshold: {FormatTime(rule.ThresholdInMilliseconds)})");

                // Check if rule was triggered recently (30 minute cooldown for app launch triggers)
                if (_ruleLastTriggered.TryGetValue(rule.Id, out var lastTriggered))
                {
                    var timeSinceLastTrigger = DateTime.Now - lastTriggered;
                    if (timeSinceLastTrigger.TotalMinutes < 30)
                    {
                        _logger.LogInformation($"⏳ Rule '{rule.Name}' in cooldown - last triggered {timeSinceLastTrigger.TotalMinutes:F1} minutes ago");
                        return false;
                    }
                }

                var currentUsage = await ruleService.GetCombinedUsageForAppsAsync(rule.SelectedPackages);

                _logger.LogInformation($"📊 Rule '{rule.Name}': Current usage {FormatTime(currentUsage)}, Threshold: {FormatTime(rule.ThresholdInMilliseconds)}");

#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    $"Usage Check: {rule.Name}",
                    $"Used: {FormatTime(currentUsage)} / Limit: {FormatTime(rule.ThresholdInMilliseconds)}"
                );
#endif

                if (currentUsage >= rule.ThresholdInMilliseconds)
                {
                    _logger.LogWarning($"🚨 THRESHOLD EXCEEDED! Rule '{rule.Name}' triggered for app: {launchedAppName}");
                    _logger.LogWarning($"🚨 Usage: {FormatTime(currentUsage)} >= Limit: {FormatTime(rule.ThresholdInMilliseconds)}");

#if ANDROID
                    AndroidNotificationHelper.ShowRuleTriggeredNotification(rule.Name, launchedAppName);
#endif

                    _ruleLastTriggered[rule.Id] = DateTime.Now;
                    _logger.LogWarning($"🔒 Triggering block for rule: {rule.Name} (will cooldown for 30 minutes)");
                    await blockService.TriggerRuleBlock(rule);

                    return true;
                }
                else
                {
                    var remainingTime = rule.ThresholdInMilliseconds - currentUsage;
                    _logger.LogInformation($"✅ Rule '{rule.Name}' not triggered. Remaining time: {FormatTime(remainingTime)}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking rule '{rule.Name}'");
#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Rule Check Error",
                    $"Error checking rule {rule.Name}: {ex.Message}"
                );
#endif
                return false;
            }
        }

        private string FormatTime(long milliseconds)
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