using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Archieve_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Register global unhandled exception listeners immediately upon instantiation
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception ?? new Exception("Unknown AppDomain unhandled exception");
                LogAndShowCrash("AppDomain.CurrentDomain.UnhandledException", ex, isTerminating: args.IsTerminating);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogAndShowCrash("Application.DispatcherUnhandledException", args.Exception, isTerminating: false);
                args.Handled = true; // Prevent abrupt crash if possible
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogAndShowCrash("TaskScheduler.UnobservedTaskException", args.Exception, isTerminating: false);
                args.SetObserved();
            };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure Windows 11 classic full context menu is enabled so right-click actions are directly visible
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                key?.SetValue("", "");
            }
            catch { }

            // Ensure boot tasks (updater + review prompt) stay registered for future boots.
            try { AppSettings.Instance.EnsureBootTasksRegistered(); } catch { }

            if (e.Args.Length > 0)
            {
                string firstArg = e.Args[0];

                try
                {
                    string normArg = firstArg.TrimStart('-', '/');

                    // PC Bootup review prompt handler
                    if (string.Equals(normArg, "bootup-review", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(normArg, "bootup", StringComparison.OrdinalIgnoreCase))
                    {
                        // Show a hidden helper window so the WPF dispatcher/message loop stays alive
                        // while we do async work and show the dialog.
                        var hidden = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Visibility = Visibility.Hidden };
                        hidden.Show();
                        await HandleBootupReviewAsync();
                        return;
                    }

                    // PC Bootup silent update checker (runs in background at Windows startup)
                    if (string.Equals(normArg, "bootup-updater", StringComparison.OrdinalIgnoreCase))
                    {
                        var hidden = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Visibility = Visibility.Hidden };
                        hidden.Show();
                        await HandleBootupUpdaterAsync();
                        return;
                    }

                    bool isSmartExtract = string.Equals(normArg, "smart-extract", StringComparison.OrdinalIgnoreCase);
                    bool isExtractHere = string.Equals(normArg, "extract-here", StringComparison.OrdinalIgnoreCase);
                    bool isExtractFiles = string.Equals(normArg, "extract-files", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(normArg, "extract-to", StringComparison.OrdinalIgnoreCase);

                    if ((isSmartExtract || isExtractHere || isExtractFiles) && e.Args.Length > 1)
                    {
                        // Resolve archive path (handling both quoted and unquoted arguments with spaces)
                        string archivePath = e.Args[1];
                        if (!File.Exists(archivePath) && e.Args.Length > 2)
                        {
                            string combined = string.Join(" ", e.Args.Skip(1));
                            if (File.Exists(combined))
                            {
                                archivePath = combined;
                            }
                        }

                        if (File.Exists(archivePath))
                        {
                            string? customDest = null;
                            if (isExtractFiles)
                            {
                                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                                {
                                    Title = "Select Destination Folder for Extraction - ZenArchieve",
                                    InitialDirectory = Path.GetDirectoryName(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                                };
                                if (folderDialog.ShowDialog() == true)
                                {
                                    customDest = folderDialog.FolderName;
                                }
                                else
                                {
                                    // User cancelled destination selection
                                    Shutdown(0);
                                    return;
                                }
                            }

                            // 7-Zip behavior: Show sleek compact extraction dialog directly, NEVER opening MainWindow!
                            var extractWindow = new ExtractionProgressWindow(archivePath, smartExtract: isSmartExtract, customDestination: customDest);
                            extractWindow.Show();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogAndShowCrash("Error launching extraction window", ex, isTerminating: false);
                }
            }

            // Standard Application Launch (Browsing / Creating / Managing Archives)
            MainWindow mainWindow;
            try
            {
                mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                LogAndShowCrash("Fatal Error in MainWindow Initialization", ex, isTerminating: true);
                Shutdown(-1);
                return;
            }

            if (e.Args.Length > 0)
            {
                string firstArg = e.Args[0];

                try
                {
                    string normArg = firstArg.TrimStart('-', '/');
                    if (string.Equals(normArg, "compress", StringComparison.OrdinalIgnoreCase) && e.Args.Length > 1)
                    {
                        var targetPaths = e.Args.Skip(1).ToList();
                        mainWindow.HandleCliCompress(targetPaths);
                    }
                    else if (File.Exists(firstArg))
                    {
                        // Direct file open (e.g. archive double-clicked in Explorer)
                        await mainWindow.HandleCliOpenArchiveAsync(firstArg);
                    }
                }
                catch (Exception ex)
                {
                    LogAndShowCrash("Error executing CLI command line argument", ex, isTerminating: false);
                }
            }
        }

        /// <summary>
        /// Boot-time review prompt: if the user hasn't opted out, show the rating dialog then exit.
        /// Runs on the UI thread (called after await in OnStartup).
        /// </summary>
        private async Task HandleBootupReviewAsync()
        {
            // Small delay to let Windows finish logging in so the dialog doesn't fight other startup apps.
            await Task.Delay(3500);

            try
            {
                var settings = AppSettings.Instance;
                if (settings.DontShowReviewDialog)
                {
                    settings.UnregisterBootupReviewPrompt();
                    return;
                }

                var reviewWindow = new ReviewPromptWindow();
                reviewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogAndShowCrash("Error showing boot review prompt", ex, isTerminating: false);
            }
            finally
            {
                Shutdown(0);
            }
        }

        /// <summary>
        /// Boot-time background updater: runs hidden in the background at Windows logon.
        /// 1. Quietly checks GitHub Releases for a newer version.
        /// 2. If no internet connection, retries every 30 seconds (up to ~10 minutes).
        /// 3. If an update is found, shows a compact "Update Available" popup.
        /// 4. If no update is available OR the user dismisses, exits cleanly so no process lingers.
        /// </summary>
        private async Task HandleBootupUpdaterAsync()
        {
            var settings = AppSettings.Instance;
            if (settings.DisableAutoUpdateCheck)
            {
                Shutdown(0);
                return;
            }

            // Small delay after login so we don't compete with other startup apps for bandwidth/CPU.
            await Task.Delay(8000);

            const int maxRetries = 20; // ~10 minutes of retries with 30s interval
            const int retryIntervalMs = 30_000;

            UpdateInfo? availableUpdate = null;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    availableUpdate = await UpdateService.CheckForUpdateAsync();
                    break; // Either got an update or confirmed up-to-date (null) — break the retry loop
                }
                catch (HttpRequestException)
                {
                    // Network issue — wait and retry
                }
                catch (TaskCanceledException)
                {
                    // Timeout — retry
                }
                catch (Exception)
                {
                    // Unexpected error (DNS, etc.) — retry
                }

                if (attempt < maxRetries - 1)
                {
                    try { await Task.Delay(retryIntervalMs); } catch { break; }
                }
            }

            try
            {
                if (availableUpdate != null)
                {
                    var updateWindow = new UpdateAvailableWindow(availableUpdate);
                    updateWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                LogAndShowCrash("Error showing boot update prompt", ex, isTerminating: false);
            }
            finally
            {
                // Always exit cleanly — never leave a background process lingering
                Shutdown(0);
            }
        }

        private static void LogAndShowCrash(string context, Exception ex, bool isTerminating)
        {
            string logContent = "=====================================================\n" +
                                $"[ZENARCHIEVE CRASH LOG - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n" +
                                $"Context: {context}\n" +
                                $"Terminating: {isTerminating}\n" +
                                $"Exception: {ex.GetType().FullName}: {ex.Message}\n" +
                                $"Stack Trace:\n{ex.StackTrace}\n";

            Exception? inner = ex.InnerException;
            int level = 1;
            while (inner != null)
            {
                logContent += $"--- Inner Exception ({level}): {inner.GetType().FullName}: {inner.Message}\n" +
                              $"{inner.StackTrace}\n";
                inner = inner.InnerException;
                level++;
            }
            logContent += "=====================================================\n\n";

            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(logPath, logContent);
            }
            catch
            {
                // Fallback to temp if BaseDirectory is write-protected (e.g. Program Files without admin)
                try
                {
                    string fallbackLog = Path.Combine(Path.GetTempPath(), "ZenArchieve_crash.log");
                    File.AppendAllText(fallbackLog, logContent);
                }
                catch { }
            }

            string userMessage = "ZenArchieve encountered a startup or runtime error:\n\n" +
                                 $"{ex.GetType().Name}: {ex.Message}\n\n" +
                                 "A detailed diagnostic log has been written to 'crash.log'.";

            MessageBox.Show(userMessage, "ZenArchieve - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
