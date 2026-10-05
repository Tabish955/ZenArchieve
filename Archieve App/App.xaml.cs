using System;
using System.IO;
using System.Linq;
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

            if (e.Args.Length > 0)
            {
                string firstArg = e.Args[0];

                try
                {
                    bool isSmartExtract = string.Equals(firstArg, "-smart-extract", StringComparison.OrdinalIgnoreCase);
                    bool isExtractHere = string.Equals(firstArg, "-extract-here", StringComparison.OrdinalIgnoreCase);
                    bool isExtractFiles = string.Equals(firstArg, "-extract-files", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(firstArg, "-extract-to", StringComparison.OrdinalIgnoreCase);

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
                    if (string.Equals(firstArg, "-compress", StringComparison.OrdinalIgnoreCase) && e.Args.Length > 1)
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

        private static void LogAndShowCrash(string context, Exception ex, bool isTerminating)
        {
            string logContent = $"=====================================================\n" +
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
            logContent += $"=====================================================\n\n";

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

            string userMessage = $"ZenArchieve encountered a startup or runtime error:\n\n" +
                                 $"{ex.GetType().Name}: {ex.Message}\n\n" +
                                 $"A detailed diagnostic log has been written to 'crash.log'.";

            MessageBox.Show(userMessage, "ZenArchieve - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
