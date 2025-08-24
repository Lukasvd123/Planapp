using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace com.usagemeter.androidapp.Services
{
    public interface ISettingsService
    {
        Task<AppSettings> GetSettingsAsync();
        Task SaveSettingsAsync(AppSettings settings);
        event EventHandler<AppSettings>? SettingsChanged;
    }

    public class AppSettings
    {
        public string HomeAppPackage { get; set; } = "com.android.launcher3";
        public string HomeAppName { get; set; } = "Default Launcher";
        public bool AllRulesEnabled { get; set; } = true;
        public string ThemeColor { get; set; } = "#6200ea"; // Material Purple
        public string AccentColor { get; set; } = "#03dac6"; // Material Teal
        public int DefaultBlockDurationMinutes { get; set; } = 5;
        public int DefaultBlockDurationSeconds { get; set; } = 0;
        public bool ShowNotifications { get; set; } = true;
        public bool VibrationEnabled { get; set; } = true;
        public bool SoundEnabled { get; set; } = true;
        public string BlockingMode { get; set; } = "Timer";

        // Debug Settings
        public bool DebugMode { get; set; } = false;
        public bool ShowDebugNotifications { get; set; } = false;
        public bool VerboseLogging { get; set; } = false;
        public bool ShowAppLaunchNotifications { get; set; } = false;
        public bool ShowRuleCheckNotifications { get; set; } = false;
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private AppSettings? _cachedSettings;
        public event EventHandler<AppSettings>? SettingsChanged;

        public SettingsService()
        {
            var appDataPath = FileSystem.AppDataDirectory;
            _settingsFilePath = Path.Combine(appDataPath, "app_settings.json");
        }

        public async Task<AppSettings> GetSettingsAsync()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    _cachedSettings = new AppSettings();
                    await SaveSettingsAsync(_cachedSettings);
                }
            }
            catch
            {
                _cachedSettings = new AppSettings();
            }

            return _cachedSettings;
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                _cachedSettings = settings;
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_settingsFilePath, json);

                SettingsChanged?.Invoke(this, settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex}");
            }
        }
    }
}