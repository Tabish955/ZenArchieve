using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Archieve_App
{
    public class AppSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZenArchieve");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
        private const string RegistrySubKey = @"Software\ZenArchieve";
        private const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ZenArchieveReviewPrompt";

        private static AppSettings? _instance;
        private static readonly object _lock = new();

        public static AppSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        public bool DontShowReviewDialog { get; set; } = false;
        public int LaunchCount { get; set; } = 0;
        public DateTime? FirstLaunchUtc { get; set; } = null;
        public DateTime? LastReviewPromptUtc { get; set; } = null;

        public static AppSettings Load()
        {
            AppSettings settings = new();

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        settings = loaded;
                    }
                }
            }
            catch
            {
                // Fallback to defaults if file is corrupt or unreadable
            }

            // Sync with HKCU registry fallback for 100% persistence across installs/cleanups
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
                if (key != null)
                {
                    object? regVal = key.GetValue("DontShowReviewDialog");
                    if (regVal is int intVal && intVal == 1)
                    {
                        settings.DontShowReviewDialog = true;
                    }
                }
            }
            catch
            {
                // Ignore registry access errors
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Ignore write errors to AppData if restricted
            }

            // Mirror to HKCU registry for reliability
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
                key?.SetValue("DontShowReviewDialog", DontShowReviewDialog ? 1 : 0, RegistryValueKind.DWord);
            }
            catch
            {
                // Ignore registry permission errors
            }

            if (DontShowReviewDialog)
            {
                UnregisterBootupReviewPrompt();
            }
        }

        public void SetDontShowAgain()
        {
            DontShowReviewDialog = true;
            Save();
            UnregisterBootupReviewPrompt();
        }

        public void RegisterBootupReviewPrompt()
        {
            if (DontShowReviewDialog)
            {
                UnregisterBootupReviewPrompt();
                return;
            }

            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistrySubKey);
                    runKey?.SetValue(RunValueName, $"\"{exePath}\" --bootup");
                }
            }
            catch
            {
                // Ignore registry write error in sandbox
            }
        }

        public void UnregisterBootupReviewPrompt()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
                runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            catch
            {
                // Ignore registry delete error
            }
        }
    }
}
