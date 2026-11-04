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
        private const string ReviewRunValueName = "ZenArchieveReviewPrompt";
        private const string UpdaterRunValueName = "ZenArchieveBootUpdater";

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
        public bool DisableAutoUpdateCheck { get; set; } = false;

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
                    object? updVal = key.GetValue("DisableAutoUpdateCheck");
                    if (updVal is int updInt && updInt == 1)
                    {
                        settings.DisableAutoUpdateCheck = true;
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
                key?.SetValue("DisableAutoUpdateCheck", DisableAutoUpdateCheck ? 1 : 0, RegistryValueKind.DWord);
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

        /// <summary>
        /// Resolves the actual application executable path, accounting for MSIX / Store packaging
        /// where Environment.ProcessPath can point into the WindowsApps restricted folder.
        /// Returns null if no reliable path can be determined.
        /// </summary>
        private static string? ResolveExecutablePath()
        {
            // Prefer the entry assembly location (works for both framework-dependent and self-contained)
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                return exePath;

            try
            {
                var asmLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(asmLoc))
                {
                    string? dir = Path.GetDirectoryName(asmLoc);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        // Try the .exe alongside the assembly
                        string candidate = Path.ChangeExtension(asmLoc, ".exe");
                        if (File.Exists(candidate)) return candidate;

                        // Also try appid.exe / native host name for self-contained builds
                        string exeInDir = Path.Combine(dir, "Archieve App.exe");
                        if (File.Exists(exeInDir)) return exeInDir;
                        string exeInDir2 = Path.Combine(dir, "ZenArchieve.exe");
                        if (File.Exists(exeInDir2)) return exeInDir2;
                    }
                }
            }
            catch { }

            return null;
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
                string? exePath = ResolveExecutablePath();
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistrySubKey);
                    runKey?.SetValue(ReviewRunValueName, $"\"{exePath}\" --bootup-review");
                }
            }
            catch
            {
                // Ignore registry write error in sandbox / restricted environments
            }
        }

        public void UnregisterBootupReviewPrompt()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
                runKey?.DeleteValue(ReviewRunValueName, throwOnMissingValue: false);
            }
            catch
            {
                // Ignore registry delete error
            }
        }

        public void RegisterBootupUpdater()
        {
            if (DisableAutoUpdateCheck)
            {
                UnregisterBootupUpdater();
                return;
            }

            try
            {
                string? exePath = ResolveExecutablePath();
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistrySubKey);
                    runKey?.SetValue(UpdaterRunValueName, $"\"{exePath}\" --bootup-updater");
                }
            }
            catch
            {
                // Ignore registry write error
            }
        }

        public void UnregisterBootupUpdater()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
                runKey?.DeleteValue(UpdaterRunValueName, throwOnMissingValue: false);
            }
            catch
            {
                // Ignore registry delete error
            }
        }

        /// <summary>
        /// Ensures both boot tasks are registered together (call on every normal app launch).
        /// </summary>
        public void EnsureBootTasksRegistered()
        {
            RegisterBootupUpdater();
            if (!DontShowReviewDialog)
                RegisterBootupReviewPrompt();
            else
                UnregisterBootupReviewPrompt();
        }
    }
}
