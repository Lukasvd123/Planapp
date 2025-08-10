using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Microsoft.Maui.Controls.Compatibility.Platform.Android;  // <-- for AppCompat support

namespace Planapp
{
    [Activity(
          Name = "com.usagemeter.androidapp.MainActivity",   // ← force this exact Java name
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Handle intent if app was opened to show blocking modal
            HandleIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);

            // Handle intent when app is already running
            HandleIntent(intent);
        }

        private void HandleIntent(Intent? intent)
        {
            if (intent == null) return;

            try
            {
                // Check if we need to show blocking modal
                var showBlock = intent.GetBooleanExtra("SHOW_BLOCK", false);
                var ruleId = intent.GetStringExtra("RULE_ID");

                if (showBlock && !string.IsNullOrEmpty(ruleId))
                {
                    // Signal to the app that we need to show blocking modal
                    Task.Run(async () =>
                    {
                        // Wait for app to be ready
                        await Task.Delay(1000);

                        // Get the rule service and trigger the block
                        var app = IPlatformApplication.Current;
                        if (app?.Services != null)
                        {
                            var ruleService = app.Services.GetService<Services.IRuleService>();
                            var blockService = app.Services.GetService<Services.IRuleBlockService>();

                            if (ruleService != null && blockService != null)
                            {
                                var rules = await ruleService.GetRulesAsync();
                                var rule = rules.FirstOrDefault(r => r.Id == ruleId);

                                if (rule != null)
                                {
                                    await blockService.TriggerRuleBlock(rule);
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling intent: {ex}");
            }
        }

        // Handle back button to prevent bypassing blocking modal
        public override void OnBackPressed()
        {
            var blockService = IPlatformApplication.Current?.Services?.GetService<Services.IRuleBlockService>();

            if (blockService?.IsBlocking == true)
            {
                // Don't allow back button when blocking modal is shown
                return;
            }

            base.OnBackPressed();
        }
    }
}