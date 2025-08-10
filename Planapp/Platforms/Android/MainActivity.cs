using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;

namespace com.usagemeter.androidapp
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        Exported = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // Set status bar color
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                {
                    Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#667eea"));
                }

                // Handle blocking intent if present
                HandleIntent(Intent);

                System.Diagnostics.Debug.WriteLine("MainActivity created successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in MainActivity.OnCreate: {ex}");
            }
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Intent = intent; // Important: Update the intent
            HandleIntent(intent);
        }

        protected override void OnResume()
        {
            base.OnResume();

            System.Diagnostics.Debug.WriteLine("MainActivity resumed");

            // Initialize services when app comes to foreground
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500); // Give UI time to load
                    await InitializeServicesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error initializing services: {ex}");
                }
            });
        }

        private void HandleIntent(Intent? intent)
        {
            if (intent == null) return;

            try
            {
                var showBlock = intent.GetBooleanExtra("SHOW_BLOCK", false);
                var ruleId = intent.GetStringExtra("RULE_ID");
                var forceForeground = intent.GetBooleanExtra("FORCE_FOREGROUND", false);

                System.Diagnostics.Debug.WriteLine($"HandleIntent - ShowBlock: {showBlock}, RuleId: {ruleId}, ForceForeground: {forceForeground}");

                if (showBlock && !string.IsNullOrEmpty(ruleId))
                {
                    // Trigger rule block with delay to ensure UI is ready
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        await TriggerRuleBlockAsync(ruleId);
                    });
                }

                if (forceForeground)
                {
                    // Make sure the activity stays in foreground
                    BringToForeground();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling intent: {ex}");
            }
        }

        private void BringToForeground()
        {
            try
            {
                // Make the activity visible and focused
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    // For API 26+, use different approach
                    Window?.AddFlags(Android.Views.WindowManagerFlags.ShowWhenLocked |
                                    Android.Views.WindowManagerFlags.TurnScreenOn);
                }

                // Bring to front
                Window?.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);

                System.Diagnostics.Debug.WriteLine("Activity brought to foreground");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error bringing to foreground: {ex}");
            }
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                var serviceProvider = IPlatformApplication.Current?.Services;
                if (serviceProvider == null) return;

                var settingsService = serviceProvider.GetService<Services.ISettingsService>();
                if (settingsService != null)
                {
                    var settings = await settingsService.GetSettingsAsync();
                    if (settings.AllRulesEnabled)
                    {
                        // Start foreground service
                        var foregroundService = new Platforms.Android.AndroidForegroundService();
                        await foregroundService.StartAsync();

                        System.Diagnostics.Debug.WriteLine("Services initialized successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in InitializeServicesAsync: {ex}");
            }
        }

        private async Task TriggerRuleBlockAsync(string ruleId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"TriggerRuleBlockAsync called for rule: {ruleId}");

                var serviceProvider = IPlatformApplication.Current?.Services;
                if (serviceProvider == null)
                {
                    System.Diagnostics.Debug.WriteLine("Service provider not available");
                    return;
                }

                var ruleService = serviceProvider.GetService<Services.IRuleService>();
                var blockService = serviceProvider.GetService<Services.IRuleBlockService>();

                if (ruleService != null && blockService != null)
                {
                    var rules = await ruleService.GetRulesAsync();
                    var rule = rules.FirstOrDefault(r => r.Id == ruleId);

                    if (rule != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Triggering rule block for: {rule.Name}");
                        await blockService.TriggerRuleBlock(rule);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Rule not found: {ruleId}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Required services not available");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error triggering rule block: {ex}");
            }
        }

        public override void OnBackPressed()
        {
            try
            {
                var serviceProvider = IPlatformApplication.Current?.Services;
                var blockService = serviceProvider?.GetService<Services.IRuleBlockService>();

                if (blockService?.IsBlocking == true)
                {
                    System.Diagnostics.Debug.WriteLine("Back navigation blocked during rule enforcement");
                    return;
                }

                base.OnBackPressed();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnBackPressed: {ex}");
                base.OnBackPressed();
            }
        }

        // Handle permission requests
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            try
            {
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

                if (requestCode == 1001 && permissions.Length > 0)
                {
                    var granted = grantResults[0] == Permission.Granted;
                    System.Diagnostics.Debug.WriteLine($"Permission result: {permissions[0]} = {granted}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling permission result: {ex}");
            }
        }

        protected override void OnDestroy()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("MainActivity destroying");
                base.OnDestroy();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDestroy: {ex}");
            }
        }
    }
}