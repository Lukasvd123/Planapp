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
        private Timer? _currentAppTimer;
        private CancellationTokenSource? _cancellationTokenSource;
        private string? _currentForegroundApp = null;
        private readonly Dictionary<string, DateTime> _lastRuleTriggerTime = new();
        private const int RULE_COOLDOWN_MINUTES = 5; // Prevent spam triggering

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
                _logger.LogInformation("🚀 Starting SMART rule monitoring service...");

                _cancellationTokenSource = new CancellationTokenSource();

#if ANDROID
                AndroidNotificationHelper.InitializeNotificationChannel();
                AndroidNotificationHelper.ClearAllNotifications();
#endif

                var _appLaunchMonitor = _serviceProvider.GetRequiredService<IAppLaunchMonitor>();
                _appLaunchMonitor.AppLaunched += OnAppLaunched;

                await _appLaunchMonitor.StartMonitoringAsync();

                // Monitor current foreground app every 3 seconds
                _currentAppTimer = new Timer(
                    async _ => await CheckCurrentForegroundApp(),
                    null,
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(3)
                );

                // Less frequent periodic checking (every 2 minutes for background monitoring)
                _periodicCheckTimer = new Timer(
                    async _ => await PeriodicRuleCheck(),
                    null,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(2)
                );

                _logger.LogInformation($"✅ SMART rule monitoring started - Launch monitor: {_appLaunchMonitor.IsMonitoring}");

#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Smart Rule Monitor Started",
                    "Monitoring with intelligent triggering and 1-min auto-cleanup notifications"
                );
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error starting rule monitoring service");
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
                _logger.LogInformation("🛑 Stopping rule monitoring service...");

                _cancellationTokenSource?.Cancel();
                _periodicCheckTimer?.Dispose();
                _currentAppTimer?.Dispose();

#if ANDROID
                AndroidNotificationHelper.ClearAllNotifications();
#endif

                _logger.LogInformation("✅ Rule monitoring service stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error stopping rule monitoring service");
            }
        }

        private async Task CheckCurrentForegroundApp()
        {
            if (!_isRunning) return;

            try
            {
#if ANDROID
                var currentApp = await GetCurrentForegroundApp();
                
                if (currentApp != _currentForegroundApp && !string.IsNullOrEmpty(currentApp))
                {
                    _currentForegroundApp = currentApp;
                    
                    // Check if this app is monitored by any rule
                    var isMonitored = await IsAppMonitoredByAnyRule(currentApp);
                    
                    // Show current app notification (debug)
                    AndroidNotificationHelper.ShowCurrentAppNotification(
                        UsageStatsHelper.GetAppName(currentApp), 
                        isMonitored
                    );
                    
                    _logger.LogInformation($"📱 Current foreground app: {UsageStatsHelper.GetAppName(currentApp)} (Monitored: {isMonitored})");
                }
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking current foreground app");
            }
        }

#if ANDROID
        private async Task<string?> GetCurrentForegroundApp()
        {
            try
            {
                var context = Platform.CurrentActivity?.ApplicationContext ?? global::Android.App.Application.Context;
                if (context == null) return null;

                var usageStatsManager = context.GetSystemService(Android.Content.Context.UsageStatsService)
                    as Android.App.Usage.UsageStatsManager;
                if (usageStatsManager == null) return null;

                var endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                var startTime = endTime - 5000; // Last 5 seconds

                var usageEvents = usageStatsManager.QueryEvents(startTime, endTime);
                string? mostRecentApp = null;
                long mostRecentTime = 0;

                while (usageEvents.HasNextEvent)
                {
                    var eventObj = new Android.App.Usage.UsageEvents.Event();
                    usageEvents.GetNextEvent(eventObj);

                    if (eventObj.EventType == Android.App.Usage.UsageEventType.ActivityResumed &&
                        eventObj.TimeStamp > mostRecentTime)
                    {
                        mostRecentApp = eventObj.PackageName;
                        mostRecentTime = eventObj.TimeStamp;
                    }
                }

                return mostRecentApp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting current foreground app");
                return null;
            }
        }
#endif

        private async Task<bool> IsAppMonitoredByAnyRule(string packageName)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                var settings = await settingsService.GetSettingsAsync();
                if (!settings.AllRulesEnabled) return false;

                var rules = await ruleService.GetRulesAsync();
                return rules.Any(r => r.IsEnabled && r.SelectedPackages.Contains(packageName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking if app is monitored");
                return false;
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
                    _logger.LogDebug("🚫 Rules disabled, skipping periodic check");
                    return;
                }

                if (blockService.IsBlocking)
                {
                    _logger.LogDebug("🔒 Already blocking, skipping periodic check");
                    return;
                }

                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                _logger.LogDebug($"🔍 Periodic check: {enabledRules.Count} enabled rules");

                foreach (var rule in enabledRules)
                {
                    // Only check rules during periodic check if no specific app is in foreground
                    // or if the current app is actually one of the monitored apps
                    var shouldCheck = string.IsNullOrEmpty(_currentForegroundApp) ||
                                     rule.SelectedPackages.Contains(_currentForegroundApp);

                    if (!shouldCheck) continue;

                    var currentUsage = await ruleService.GetCombinedUsageForAppsAsync(rule.SelectedPackages);

                    if (currentUsage >= rule.ThresholdInMilliseconds)
                    {
                        // Check cooldown to prevent spam
                        if (IsRuleInCooldown(rule.Id))
                        {
                            _logger.LogDebug($"⏰ Rule '{rule.Name}' in cooldown, skipping");
                            continue;
                        }

                        _logger.LogWarning($"⚠️ PERIODIC TRIGGER: Rule '{rule.Name}' exceeded threshold ({FormatTime(currentUsage)} >= {FormatTime(rule.ThresholdInMilliseconds)})");

#if ANDROID
                        AndroidNotificationHelper.ShowRuleTriggeredNotification(
                            rule.Name,
                            $"Periodic check - {FormatTime(currentUsage)} used"
                        );
#endif

                        SetRuleCooldown(rule.Id);
                        await blockService.TriggerRuleBlock(rule);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during periodic rule check");
            }
        }

        private async void OnAppLaunched(object? sender, AppLaunchEventArgs e)
        {
            try
            {
                _logger.LogInformation($"🚀 APP LAUNCHED: {e.AppName} ({e.PackageName}) at {e.LaunchedAt:HH:mm:ss}");

                // Skip our own app
                if (e.PackageName == "com.usagemeter.androidapp") return;

                using var scope = _serviceProvider.CreateScope();
                var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();
                var blockService = scope.ServiceProvider.GetRequiredService<IRuleBlockService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                var settings = await settingsService.GetSettingsAsync();
                if (!settings.AllRulesEnabled)
                {
                    _logger.LogInformation("🚫 Rules are globally disabled - skipping rule check");
                    return;
                }

                if (blockService.IsBlocking)
                {
                    _logger.LogInformation("🔒 Rule block already active, skipping new app launch");
                    return;
                }

                var rules = await ruleService.GetRulesAsync();
                var enabledRules = rules.Where(r => r.IsEnabled).ToList();

                var relevantRules = enabledRules
                    .Where(rule => rule.SelectedPackages.Contains(e.PackageName))
                    .ToList();

                if (!relevantRules.Any())
                {
                    _logger.LogInformation($"✅ No rules monitoring app: {e.AppName} - app launch allowed");
#if ANDROID
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        $"App Launch: {e.AppName}",
                        "✅ Not monitored - launch allowed"
                    );
#endif
                    return;
                }

                _logger.LogInformation($"⚠️ Found {relevantRules.Count} rules monitoring app: {e.AppName}");

#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    $"Monitored App Launch",
                    $"{relevantRules.Count} rules monitor {e.AppName} - checking limits NOW"
                );
#endif

                // IMMEDIATE CHECK ON LAUNCH - This is the key difference
                foreach (var rule in relevantRules)
                {
                    var shouldTrigger = await CheckAndTriggerRuleImmediately(rule, ruleService, blockService, e.AppName);
                    if (shouldTrigger)
                    {
                        _logger.LogWarning($"🔥 RULE TRIGGERED IMMEDIATELY ON LAUNCH: {rule.Name} for app {e.AppName}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error handling app launch: {e.AppName}");
#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Rule Monitor Error",
                    $"Error processing {e.AppName}: {ex.Message}"
                );
#endif
            }
        }

        private async Task<bool> CheckAndTriggerRuleImmediately(AppRule rule, IRuleService ruleService, IRuleBlockService blockService, string launchedAppName)
        {
            try
            {
                _logger.LogInformation($"🔍 IMMEDIATE CHECK for rule: '{rule.Name}' (threshold: {FormatTime(rule.ThresholdInMilliseconds)})");

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
                    // Check cooldown - but be more lenient for direct app launches
                    if (IsRuleInCooldown(rule.Id))
                    {
                        var cooldownRemaining = GetCooldownRemaining(rule.Id);
                        _logger.LogWarning($"⏰ Rule '{rule.Name}' in cooldown ({cooldownRemaining} remaining), but launching monitored app - triggering anyway");

                        // Still trigger on direct app launch even during cooldown
                        // but extend the cooldown to prevent immediate re-triggering
                        SetRuleCooldown(rule.Id);
                    }

                    _logger.LogWarning($"🚨 THRESHOLD EXCEEDED! Rule '{rule.Name}' triggered IMMEDIATELY for app launch: {launchedAppName}");
                    _logger.LogWarning($"🚨 Usage: {FormatTime(currentUsage)} >= Limit: {FormatTime(rule.ThresholdInMilliseconds)}");

#if ANDROID
                    AndroidNotificationHelper.ShowRuleTriggeredNotification(rule.Name, $"Launch blocked: {launchedAppName}");
#endif

                    SetRuleCooldown(rule.Id);
                    await blockService.TriggerRuleBlock(rule);

                    return true;
                }
                else
                {
                    var remainingTime = rule.ThresholdInMilliseconds - currentUsage;
                    _logger.LogInformation($"✅ Rule '{rule.Name}' not triggered. Remaining time: {FormatTime(remainingTime)}");

#if ANDROID
                    AndroidNotificationHelper.ShowAppLaunchNotification(
                        $"Launch Allowed: {rule.Name}",
                        $"✅ {FormatTime(remainingTime)} remaining - {launchedAppName} allowed"
                    );
#endif
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error checking rule '{rule.Name}'");
#if ANDROID
                AndroidNotificationHelper.ShowAppLaunchNotification(
                    "Rule Check Error",
                    $"Error checking rule {rule.Name}: {ex.Message}"
                );
#endif
                return false;
            }
        }

        private bool IsRuleInCooldown(string ruleId)
        {
            if (!_lastRuleTriggerTime.TryGetValue(ruleId, out var lastTrigger))
                return false;

            var cooldownEnd = lastTrigger.AddMinutes(RULE_COOLDOWN_MINUTES);
            return DateTime.Now < cooldownEnd;
        }

        private string GetCooldownRemaining(string ruleId)
        {
            if (!_lastRuleTriggerTime.TryGetValue(ruleId, out var lastTrigger))
                return "0m";

            var cooldownEnd = lastTrigger.AddMinutes(RULE_COOLDOWN_MINUTES);
            var remaining = cooldownEnd - DateTime.Now;

            if (remaining.TotalMinutes >= 1)
                return $"{(int)remaining.TotalMinutes}m {remaining.Seconds}s";
            else
                return $"{remaining.Seconds}s";
        }

        private void SetRuleCooldown(string ruleId)
        {
            _lastRuleTriggerTime[ruleId] = DateTime.Now;
            _logger.LogDebug($"⏰ Set {RULE_COOLDOWN_MINUTES}min cooldown for rule {ruleId}");
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