using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Archieve_App
{
    public partial class MainWindow : FluentWindow
    {
        private readonly ArchiveService _archiveService = new();
        private string? _currentArchivePath;
        private string? _currentArchivePassword;
        private List<ArchiveItemInfo> _allItems = new();
        private CancellationTokenSource? _operationCts;
        private string? _lastOutputDirectory;

        // Archive Creation state
        private readonly List<string> _createArchiveSources = new();

        // Batch Queue state
        private readonly List<string> _batchQueueArchives = new();
        private CancellationTokenSource? _batchCts;
        private CancellationTokenSource? _benchmarkCts;

        // Password prompt resumption action
        private Func<string, Task>? _pendingPasswordAction;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                var iconUri = new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch
            {
                // Fallback: icon not found or unparseable
            }

            try
            {
                WindowBackdropType = WindowBackdropType.Mica;
            }
            catch
            {
                WindowBackdropType = WindowBackdropType.None;
            }

            // Fire-and-forget auto-update check and review prompt (non-blocking)
            Loaded += async (s, e) =>
            {
                await CheckForUpdatesAsync();
                await CheckReviewPromptAsync();
            };
        }

        #region Rating & Review

        private void BtnRateReview_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new ReviewPromptWindow();
            prompt.Owner = this;
            prompt.ShowDialog();
        }

        private async Task CheckReviewPromptAsync()
        {
            try
            {
                var settings = AppSettings.Instance;
                settings.LaunchCount++;
                settings.Save();

                if (!settings.DontShowReviewDialog)
                {
                    settings.RegisterBootupReviewPrompt();

                    // Short delay so MainWindow renders completely and smoothly
                    await Task.Delay(1500);

                    if (!settings.DontShowReviewDialog && IsLoaded)
                    {
                        var prompt = new ReviewPromptWindow();
                        prompt.Owner = this;
                        prompt.ShowDialog();
                    }
                }
            }
            catch
            {
                // Non-critical, do not disturb user workflow
            }
        }

        #endregion

        #region Auto-Update

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(isManualCheck: true);
        }

        private async Task CheckForUpdatesAsync(bool isManualCheck = false)
        {
            try
            {
                if (isManualCheck)
                {
                    ShowStatus("Checking for updates online...", SymbolRegular.ArrowSync24);
                }

                var update = await UpdateService.CheckForUpdateAsync();
                if (update == null)
                {
                    if (isManualCheck)
                    {
                        var cur = UpdateService.GetCurrentVersion();
                        ShowStatus($"ZenArchieve is up to date (v{cur.Major}.{cur.Minor}.{cur.Build}).", SymbolRegular.CheckmarkCircle24);
                        MessageBox.Show(
                            $"You are running the latest version of ZenArchieve!\n\n" +
                            $"Installed Version: v{cur.Major}.{cur.Minor}.{cur.Build}\n" +
                            $"Status: Up to date",
                            "ZenArchieve — Check for Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return; // Already up to date or check failed
                }

                var result = MessageBox.Show(
                    $"🚀 A new version of ZenArchieve is available!\n\n" +
                    $"Current Version: v{update.CurrentVersion}\n" +
                    $"Latest Version: v{update.LatestVersion}\n" +
                    $"Release: {update.ReleaseName}\n" +
                    $"Download Size: {update.FormattedSize}\n\n" +
                    $"Would you like to update now?\n\n" +
                    $"(The update installs automatically — no need to uninstall first)",
                    "ZenArchieve — Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes) return;

                ShowStatus("Downloading update...", SymbolRegular.ArrowDownload24);
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Value = 0;
                ProgressBarOperation.Visibility = Visibility.Visible;

                var progress = new Progress<double>(p =>
                {
                    ProgressBarOperation.Value = p;
                    TxtStatus.Text = $"Downloading update: {p:0}%";
                });

                bool success = await UpdateService.DownloadAndInstallUpdateAsync(update, progress);

                if (success)
                {
                    MessageBox.Show(
                        "Update downloaded successfully!\n\n" +
                        "The installer will now run. ZenArchieve will close to complete the update.",
                        "ZenArchieve — Updating",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Application.Current.Shutdown();
                }
                else
                {
                    ShowStatus("Update download failed. Please try again later.", SymbolRegular.ErrorCircle24, isError: true);
                    ProgressBarOperation.Visibility = Visibility.Hidden;
                }
            }
            catch (Exception ex)
            {
                if (isManualCheck)
                {
                    ShowStatus($"Update check failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                    MessageBox.Show($"Could not check for updates:\n\n{ex.Message}", "ZenArchieve — Update Check Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        #endregion

        #region Archive Loading & Inspecting

        private async void BtnOpenArchive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Archive",
                Filter = "All Supported Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|" +
                         "ZIP Archives (*.zip)|*.zip|" +
                         "RAR Archives (*.rar)|*.rar|" +
                         "7-Zip Archives (*.7z)|*.7z|" +
                         "TAR / GZ Archives (*.tar;*.gz)|*.tar;*.gz|" +
                         "All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                _currentArchivePassword = null;
                await LoadArchiveAsync(dialog.FileName);
            }
        }

        public async Task LoadArchiveAsync(string filePath, string? password = null)
        {
            if (!File.Exists(filePath))
            {
                ShowStatus("File does not exist.", SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            try
            {
                _currentArchivePath = filePath;
                _currentArchivePassword = password;
                SetUiBusy(true, $"Loading {Path.GetFileName(filePath)}...");
                ProgressBarOperation.IsIndeterminate = true;
                ProgressBarOperation.Visibility = Visibility.Visible;

                ClosePreviewPanel();

                var items = await _archiveService.ReadArchiveAsync(filePath, password);
                _allItems = items;

                // Update UI elements
                TxtArchiveName.Text = Path.GetFileName(filePath);
                TxtArchivePath.Text = filePath;
                TxtArchivePath.ToolTip = filePath;

                // Evaluate Smart Extract mode
                bool needsSubfolder = _archiveService.ShouldUseSmartExtractSubfolder(items.Select(i => i.Path));
                TxtSmartExtractMode.Text = needsSubfolder
                    ? "Smart Extract: Creates container folder (prevents loose file clutter)"
                    : "Smart Extract: Direct folder extract (archive already grouped in folder)";

                // Stats
                int fileCount = items.Count(i => !i.IsDirectory);
                int folderCount = items.Count(i => i.IsDirectory);
                long totalSize = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
                long packedSize = items.Where(i => !i.IsDirectory).Sum(i => i.CompressedSize);

                TxtStatFiles.Text = $"{fileCount:N0} files";
                TxtStatFolders.Text = $"{folderCount:N0} folders";
                TxtStatSize.Text = $"{ArchiveItemInfo.FormatBytes(totalSize)} uncompressed";
                TxtStatPacked.Text = $"{ArchiveItemInfo.FormatBytes(packedSize)} compressed";

                // Filter & Display
                ApplyFilter();

                // Toggle visibility
                GridEmptyState.Visibility = Visibility.Collapsed;
                GridArchiveContent.Visibility = Visibility.Visible;
                BtnCloseArchive.Visibility = Visibility.Visible;
                BtnExtractHere.IsEnabled = true;
                BtnExtractTo.IsEnabled = true;
                BtnSmartExtract.IsEnabled = true;
                BtnTestArchive.IsEnabled = true;
                BtnAnalyzeSize.IsEnabled = true;
                BtnConvertFormat.IsEnabled = true;
                BtnChecksum.IsEnabled = true;
                BtnFindDuplicates.IsEnabled = true;

                ShowStatus($"Ready • {fileCount} files loaded from {Path.GetFileName(filePath)}", SymbolRegular.CheckmarkCircle24);
            }
            catch (ArchiveEncryptedException ex)
            {
                // Archive is encrypted with password -> show modern password prompt
                ShowPasswordPrompt(ex.Message, async enteredPassword =>
                {
                    await LoadArchiveAsync(filePath, enteredPassword);
                });
            }
            catch (Exception ex)
            {
                ShowStatus($"Error reading archive: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Failed to inspect archive:\n\n{ex.Message}", "Error Opening Archive", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Visibility = Visibility.Hidden;
                SetUiBusy(false);
            }
        }

        private void CloseCurrentArchive()
        {
            _currentArchivePath = null;
            _currentArchivePassword = null;
            _allItems.Clear();
            DataGridFiles.ItemsSource = null;

            ClosePreviewPanel();

            TxtArchiveName.Text = "No archive loaded";
            TxtArchivePath.Text = "Drag and drop files or click Open";
            TxtArchivePath.ToolTip = null;

            GridArchiveContent.Visibility = Visibility.Collapsed;
            GridEmptyState.Visibility = Visibility.Visible;
            BtnCloseArchive.Visibility = Visibility.Collapsed;
            BtnExtractHere.IsEnabled = false;
            BtnExtractTo.IsEnabled = false;
            BtnSmartExtract.IsEnabled = false;
            BtnTestArchive.IsEnabled = false;
            BtnAnalyzeSize.IsEnabled = false;
            BtnConvertFormat.IsEnabled = false;
            BtnChecksum.IsEnabled = false;
            BtnFindDuplicates.IsEnabled = false;
            BtnOpenDestinationFolder.Visibility = Visibility.Collapsed;

            ShowStatus("Ready", SymbolRegular.CheckmarkCircle24);
        }

        private void BtnCloseArchive_Click(object sender, RoutedEventArgs e)
        {
            CloseCurrentArchive();
        }

        #endregion

        #region In-Archive Safety Preview & Inspector

        private async void DataGridFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataGridFiles.SelectedItem is not ArchiveItemInfo item)
            {
                ClosePreviewPanel();
                return;
            }

            if (item.IsDirectory)
            {
                ClosePreviewPanel();
                return;
            }

            await ShowPreviewForItemAsync(item);
        }

        private async Task ShowPreviewForItemAsync(ArchiveItemInfo item)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            PanelInspector.Visibility = Visibility.Visible;
            TxtPreviewName.Text = item.Name;
            TxtPreviewSize.Text = $"{item.FormattedSize} • {item.Extension}";
            IconPreviewType.Symbol = item.IconSymbol;

            // Reset sub-panels
            PanelPreviewLoading.Visibility = Visibility.Visible;
            PanelPreviewImage.Visibility = Visibility.Collapsed;
            PanelPreviewText.Visibility = Visibility.Collapsed;
            PanelPreviewSecurity.Visibility = Visibility.Collapsed;
            PanelPreviewGeneric.Visibility = Visibility.Collapsed;
            TxtPreviewHash.Text = "Calculating SHA-256...";

            string ext = Path.GetExtension(item.Name).ToLowerInvariant();
            bool isExecutable = ext is ".exe" or ".bat" or ".cmd" or ".dll" or ".msi" or ".ps1" or ".vbs" or ".sys" or ".com" or ".scr";

            if (isExecutable)
            {
                PanelPreviewLoading.Visibility = Visibility.Collapsed;
                PanelPreviewSecurity.Visibility = Visibility.Visible;
            }

            try
            {
                using var stream = await _archiveService.GetEntryStreamAsync(_currentArchivePath, item.Path, _currentArchivePassword);

                // Compute SHA-256 Hash
                byte[] hashBytes = SHA256.HashData(stream.ToArray());
                string sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
                TxtPreviewHash.Text = sha256Hex;

                PanelPreviewLoading.Visibility = Visibility.Collapsed;

                if (isExecutable)
                {
                    // Already showing security shield
                    return;
                }

                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".ico" or ".gif")
                {
                    // Image Viewer
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    ImgPreview.Source = bitmap;
                    TxtImageDimensions.Text = $"Dimensions: {bitmap.PixelWidth} × {bitmap.PixelHeight} px";
                    PanelPreviewImage.Visibility = Visibility.Visible;
                }
                else if (ext is ".txt" or ".json" or ".xml" or ".cs" or ".md" or ".log" or ".csv" or ".ini" 
                             or ".cfg" or ".config" or ".yml" or ".yaml" or ".html" or ".css" or ".js" or ".ts" or ".py")
                {
                    // Text / Code Viewer (Preview up to 256 KB)
                    stream.Position = 0;
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    char[] buffer = new char[256 * 1024];
                    int readChars = await reader.ReadAsync(buffer, 0, buffer.Length);
                    string text = new string(buffer, 0, readChars);

                    if (!reader.EndOfStream)
                    {
                        text += "\n\n... [Content truncated: preview limited to first 256 KB] ...";
                        TxtTextStats.Text = "Truncated preview (first 256 KB shown)";
                    }
                    else
                    {
                        TxtTextStats.Text = $"{readChars:N0} characters loaded";
                    }

                    TxtPreviewContent.Text = text;
                    PanelPreviewText.Visibility = Visibility.Visible;
                }
                else
                {
                    // Generic File Info
                    PanelPreviewGeneric.Visibility = Visibility.Visible;
                }
            }
            catch (ArchiveEncryptedException)
            {
                PanelPreviewLoading.Visibility = Visibility.Collapsed;
                ShowPasswordPrompt("Password required to decrypt and preview this file:", async enteredPassword =>
                {
                    _currentArchivePassword = enteredPassword;
                    await ShowPreviewForItemAsync(item);
                });
            }
            catch (Exception ex)
            {
                PanelPreviewLoading.Visibility = Visibility.Collapsed;
                PanelPreviewGeneric.Visibility = Visibility.Visible;
                TxtPreviewHash.Text = $"Error inspecting: {ex.Message}";
            }
        }

        private void BtnClosePreview_Click(object sender, RoutedEventArgs e)
        {
            ClosePreviewPanel();
        }

        private void ClosePreviewPanel()
        {
            PanelInspector.Visibility = Visibility.Collapsed;
            DataGridFiles.SelectedItem = null;
        }

        private void BtnCopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtPreviewHash.Text) && !TxtPreviewHash.Text.StartsWith("Calculating"))
            {
                Clipboard.SetText(TxtPreviewHash.Text);
                ShowStatus("SHA-256 hash copied to clipboard!", SymbolRegular.CheckmarkCircle24);
            }
        }

        #endregion

        #region Extraction

        private async void BtnExtractHere_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            string? destination = Path.GetDirectoryName(_currentArchivePath);
            if (string.IsNullOrEmpty(destination))
            {
                destination = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            await ExecuteExtractionAsync(destination, smartExtract: false);
        }

        private async void BtnSmartExtract_Click(object sender, RoutedEventArgs? e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            string? destination = Path.GetDirectoryName(_currentArchivePath);
            if (string.IsNullOrEmpty(destination))
            {
                destination = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            await ExecuteExtractionAsync(destination, smartExtract: true);
        }

        private async void BtnExtractTo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Extraction",
                Multiselect = false,
                InitialDirectory = Path.GetDirectoryName(_currentArchivePath) ?? string.Empty
            };

            if (dialog.ShowDialog(this) == true)
            {
                await ExecuteExtractionAsync(dialog.FolderName, smartExtract: false);
            }
        }

        private async Task ExecuteExtractionAsync(string baseDestination, bool smartExtract)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            _operationCts = new CancellationTokenSource();
            _lastOutputDirectory = null;

            SetOperationUiActive(true, "Extracting...");
            var stopwatch = Stopwatch.StartNew();

            var progress = new Progress<ArchiveProgressReport>(report =>
            {
                ProgressBarOperation.Value = report.Percentage;
                TxtProgressPercent.Text = $"{report.Percentage:0}%";
                TxtStatus.Text = report.StatusMessage;
            });

            try
            {
                string resultDirectory = await _archiveService.ExtractArchiveAsync(
                    _currentArchivePath,
                    baseDestination,
                    smartExtract,
                    _currentArchivePassword,
                    progress,
                    _operationCts.Token);

                stopwatch.Stop();
                _lastOutputDirectory = resultDirectory;

                int extractedCount = _allItems.Count > 0
                    ? _allItems.Count(i => !i.IsDirectory)
                    : (Directory.Exists(resultDirectory) ? Directory.GetFiles(resultDirectory, "*", SearchOption.AllDirectories).Length : 0);
                string opLabel = smartExtract ? "Smart Extracted" : "Extracted";
                ShowStatus($"{opLabel} {extractedCount} files in {stopwatch.Elapsed.TotalSeconds:0.1}s! Location: {resultDirectory}", SymbolRegular.CheckmarkCircle24);
                BtnOpenDestinationFolder.Visibility = Visibility.Visible;

                var promptRes = MessageBox.Show(
                    $"Extraction completed in {stopwatch.Elapsed.TotalSeconds:0.1}s!\n\n" +
                    $"Files: {extractedCount}\n" +
                    $"Destination:\n{resultDirectory}\n\n" +
                    "Would you like to open the destination folder now?",
                    "Extraction Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (promptRes == MessageBoxResult.Yes)
                {
                    BtnOpenDestinationFolder_Click(this, new RoutedEventArgs());
                }
            }
            catch (ArchiveEncryptedException ex)
            {
                stopwatch.Stop();
                ShowPasswordPrompt(ex.Message, async enteredPassword =>
                {
                    _currentArchivePassword = enteredPassword;
                    await ExecuteExtractionAsync(baseDestination, smartExtract);
                });
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                ShowStatus("Extraction cancelled by user.", SymbolRegular.DismissCircle24, isError: true);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                ShowStatus($"Extraction error: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Failed during extraction:\n\n{ex.Message}", "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetOperationUiActive(false);
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        #endregion

        #region Password Prompt Modal

        private void ShowPasswordPrompt(string message, Func<string, Task> onPasswordSubmitted)
        {
            _pendingPasswordAction = onPasswordSubmitted;
            TxtPasswordPromptMessage.Text = message;
            TxtDecryptPassword.Password = string.Empty;
            ModalPasswordPrompt.Visibility = Visibility.Visible;
            TxtDecryptPassword.Focus();
        }

        private async void BtnSubmitPassword_Click(object sender, RoutedEventArgs e)
        {
            string password = TxtDecryptPassword.Password;
            ModalPasswordPrompt.Visibility = Visibility.Collapsed;

            if (_pendingPasswordAction != null)
            {
                var action = _pendingPasswordAction;
                _pendingPasswordAction = null;
                await action(password);
            }
        }

        private void BtnCancelPasswordPrompt_Click(object sender, RoutedEventArgs e)
        {
            ModalPasswordPrompt.Visibility = Visibility.Collapsed;
            _pendingPasswordAction = null;
            ShowStatus("Password entry cancelled.", SymbolRegular.DismissCircle24);
        }

        private void TxtDecryptPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSubmitPassword_Click(sender, e);
            }
        }

        #endregion

        #region Archive Creation (Compress Files)

        private void BtnNewArchive_Click(object sender, RoutedEventArgs e)
        {
            OpenCreateArchiveModal();
        }

        public void OpenCreateArchiveModal(IEnumerable<string>? initialSources = null)
        {
            _createArchiveSources.Clear();

            if (initialSources != null)
            {
                _createArchiveSources.AddRange(initialSources);
            }

            RefreshSourceItemsList();

            // Default output path suggestion
            string defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string baseName = "NewArchive";

            if (_createArchiveSources.Count > 0)
            {
                string first = _createArchiveSources[0];
                string? parent = Path.GetDirectoryName(first);
                if (!string.IsNullOrEmpty(parent))
                {
                    defaultFolder = parent;
                }
                baseName = Path.GetFileNameWithoutExtension(first);
            }

            string ext = GetSelectedExtension();
            TxtCreateArchivePath.Text = Path.Combine(defaultFolder, $"{baseName}{ext}");

            // Reset password inputs
            ChkEnablePassword.IsChecked = false;
            PanelPasswordFields.Visibility = Visibility.Collapsed;
            TxtCreatePassword.Password = string.Empty;
            TxtCreatePasswordConfirm.Password = string.Empty;
            BarPasswordStrength.Value = 0;
            TxtPasswordStrength.Text = "Strength: None";

            ModalCreateArchive.Visibility = Visibility.Visible;
        }

        private void RefreshSourceItemsList()
        {
            ListSourceItems.ItemsSource = null;
            ListSourceItems.ItemsSource = _createArchiveSources.Select(p =>
            {
                bool isDir = Directory.Exists(p);
                return $"{(isDir ? "📁 " : "📄 ")}{Path.GetFileName(p)} ({p})";
            }).ToList();
        }

        private string GetSelectedExtension()
        {
            if (CmbCreateFormat.SelectedItem is ComboBoxItem item && (string)item.Tag == "SevenZip")
            {
                return ".7z";
            }
            return ".zip";
        }

        private void CmbCreateFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtCreateArchivePath == null) return;

            string currentPath = TxtCreateArchivePath.Text;
            if (string.IsNullOrWhiteSpace(currentPath)) return;

            string ext = GetSelectedExtension();
            string newPath = Path.ChangeExtension(currentPath, ext);
            TxtCreateArchivePath.Text = newPath;

            // Enable header encryption option for 7z format
            if (ChkEncryptFileNames != null)
            {
                ChkEncryptFileNames.IsEnabled = ext == ".7z";
                if (ext != ".7z")
                {
                    ChkEncryptFileNames.IsChecked = false;
                }
            }
        }

        private void ChkEnablePassword_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isChecked = ChkEnablePassword.IsChecked == true;
            PanelPasswordFields.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtCreatePassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string pwd = TxtCreatePassword.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                BarPasswordStrength.Value = 0;
                TxtPasswordStrength.Text = "Strength: None";
                TxtPasswordStrength.Foreground = (Brush)FindResource("TextFillColorTertiaryBrush");
                return;
            }

            int score = 0;
            if (pwd.Length >= 6) score += 25;
            if (pwd.Length >= 10) score += 25;
            if (pwd.Any(char.IsUpper) && pwd.Any(char.IsLower)) score += 25;
            if (pwd.Any(char.IsDigit) && pwd.Any(ch => !char.IsLetterOrDigit(ch))) score += 25;

            BarPasswordStrength.Value = score;
            if (score <= 25)
            {
                TxtPasswordStrength.Text = "Strength: Weak";
                TxtPasswordStrength.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
            }
            else if (score <= 50)
            {
                TxtPasswordStrength.Text = "Strength: Moderate";
                TxtPasswordStrength.Foreground = (Brush)FindResource("SystemFillColorCautionBrush");
            }
            else if (score <= 75)
            {
                TxtPasswordStrength.Text = "Strength: Strong (AES-256)";
                TxtPasswordStrength.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                TxtPasswordStrength.Text = "Strength: Very Strong (AES-256)";
                TxtPasswordStrength.Foreground = (Brush)FindResource("AccentTextFillColorPrimaryBrush");
            }
        }

        private void BtnBrowseCreateOutput_Click(object sender, RoutedEventArgs e)
        {
            string ext = GetSelectedExtension();
            var dialog = new SaveFileDialog
            {
                Title = "Save Archive As",
                Filter = ext == ".7z" ? "7-Zip Archive (*.7z)|*.7z" : "ZIP Archive (*.zip)|*.zip",
                DefaultExt = ext,
                FileName = Path.GetFileName(TxtCreateArchivePath.Text)
            };

            if (dialog.ShowDialog(this) == true)
            {
                TxtCreateArchivePath.Text = dialog.FileName;
            }
        }

        private void BtnAddFilesToCreate_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Files to Compress",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!_createArchiveSources.Contains(file))
                    {
                        _createArchiveSources.Add(file);
                    }
                }
                RefreshSourceItemsList();
            }
        }

        private void BtnAddFolderToCreate_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Folder to Compress",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                if (!_createArchiveSources.Contains(dialog.FolderName))
                {
                    _createArchiveSources.Add(dialog.FolderName);
                }
                RefreshSourceItemsList();
            }
        }

        private void BtnCancelCreateModal_Click(object sender, RoutedEventArgs e)
        {
            ModalCreateArchive.Visibility = Visibility.Collapsed;
        }

        private async void BtnStartCreateArchive_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = TxtCreateArchivePath.Text.Trim();
            if (string.IsNullOrEmpty(targetPath))
            {
                MessageBox.Show("Please enter a valid target archive file path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_createArchiveSources.Count == 0)
            {
                MessageBox.Show("Please add at least one file or folder to compress.", "No Sources", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? password = null;
            bool encryptFileNames = false;

            if (ChkEnablePassword.IsChecked == true)
            {
                string pwd = TxtCreatePassword.Password;
                string confirm = TxtCreatePasswordConfirm.Password;

                if (string.IsNullOrEmpty(pwd))
                {
                    MessageBox.Show("Please enter an encryption password or uncheck Password Protection.", "Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (pwd != confirm)
                {
                    MessageBox.Show("Password and Confirmation do not match. Please re-enter.", "Password Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                password = pwd;
                encryptFileNames = ChkEncryptFileNames.IsChecked == true;
            }

            var format = (CmbCreateFormat.SelectedItem is ComboBoxItem item && (string)item.Tag == "SevenZip")
                ? CompressionFormat.SevenZip
                : CompressionFormat.Zip;

            var level = CmbCreateLevel.SelectedIndex switch
            {
                0 => CompressionLevel.Store,
                1 => CompressionLevel.Fast,
                3 => CompressionLevel.Maximum,
                _ => CompressionLevel.Normal
            };

            ModalCreateArchive.Visibility = Visibility.Collapsed;

            _operationCts = new CancellationTokenSource();
            _lastOutputDirectory = Path.GetDirectoryName(targetPath);

            SetOperationUiActive(true, "Compressing archive...");
            var stopwatch = Stopwatch.StartNew();

            var progress = new Progress<ArchiveProgressReport>(report =>
            {
                ProgressBarOperation.Value = report.Percentage;
                TxtProgressPercent.Text = $"{report.Percentage:0}%";
                TxtStatus.Text = report.StatusMessage;
            });

            try
            {
                await _archiveService.CreateArchiveAsync(
                    targetPath,
                    _createArchiveSources,
                    format,
                    level,
                    password,
                    encryptFileNames,
                    progress,
                    _operationCts.Token);

                stopwatch.Stop();

                ShowStatus($"Created {Path.GetFileName(targetPath)} in {stopwatch.Elapsed.TotalSeconds:0.1}s!", SymbolRegular.CheckmarkCircle24);
                BtnOpenDestinationFolder.Visibility = Visibility.Visible;

                var result = MessageBox.Show(
                    $"Archive created successfully:\n\n{targetPath}\n\nWould you like to open and inspect it in ZenArchieve now?",
                    "Archive Created",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await LoadArchiveAsync(targetPath, password);
                }
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                ShowStatus("Archive creation cancelled.", SymbolRegular.DismissCircle24, isError: true);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                ShowStatus($"Creation failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Failed to create archive:\n\n{ex.Message}", "Compression Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetOperationUiActive(false);
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        #endregion

        #region CLI & Shell Integration Handlers

        public async Task HandleCliOpenArchiveAsync(string archivePath)
        {
            await LoadArchiveAsync(archivePath);
        }

        public async Task HandleCliSmartExtractAsync(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                ShowStatus("Archive file not found: " + archivePath, SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            string? destination = Path.GetDirectoryName(archivePath);
            if (string.IsNullOrEmpty(destination))
            {
                destination = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            _currentArchivePath = archivePath;
            TxtArchiveName.Text = Path.GetFileName(archivePath);
            TxtArchivePath.Text = archivePath;

            await ExecuteExtractionAsync(destination, smartExtract: true);
        }

        public async Task HandleCliExtractHereAsync(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                ShowStatus("Archive file not found: " + archivePath, SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            string? destination = Path.GetDirectoryName(archivePath);
            if (string.IsNullOrEmpty(destination))
            {
                destination = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            _currentArchivePath = archivePath;
            TxtArchiveName.Text = Path.GetFileName(archivePath);
            TxtArchivePath.Text = archivePath;

            await ExecuteExtractionAsync(destination, smartExtract: false);
        }

        public void HandleCliCompress(IEnumerable<string> targetPaths)
        {
            OpenCreateArchiveModal(targetPaths);
        }

        #endregion

        #region Archive Integrity Audit

        private async void BtnTestArchive_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            SetOperationUiActive(true, "Auditing archive integrity...");
            _operationCts = new CancellationTokenSource();

            var progress = new Progress<double>(percent =>
            {
                ProgressBarOperation.Value = percent;
                TxtProgressPercent.Text = $"{percent:0}%";
                TxtStatus.Text = $"Auditing entries: {percent:0}% complete";
            });

            try
            {
                var report = await _archiveService.TestArchiveIntegrityAsync(_currentArchivePath, _currentArchivePassword, progress, _operationCts.Token);
                ShowHealthReportModal(report);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("Integrity audit cancelled.", SymbolRegular.DismissCircle24, isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Audit error: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Failed to complete audit:\n\n{ex.Message}", "Audit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetOperationUiActive(false);
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        private void ShowHealthReportModal(ArchiveHealthReport report)
        {
            TxtHealthTestedFiles.Text = $"{report.TotalFilesTested:N0} files";
            TxtHealthSpeed.Text = report.FormattedSpeed;
            TxtHealthElapsed.Text = $"{report.Elapsed.TotalSeconds:0.1}s";

            if (report.IsHealthy)
            {
                BorderHealthStatus.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x10, 0x7C, 0x41));
                BorderHealthStatus.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0x7C, 0x41));
                IconHealthStatus.Symbol = SymbolRegular.CheckmarkCircle24;
                IconHealthStatus.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
                TxtHealthStatusTitle.Text = "100% HEALTHY - ALL CHECKS PASSED";
                TxtHealthStatusTitle.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
                TxtHealthStatusDesc.Text = "All archive entries and headers verified cleanly with 0 CRC corruptions.";
                PanelHealthIssues.Visibility = Visibility.Collapsed;
                ListHealthIssues.ItemsSource = null;
            }
            else
            {
                BorderHealthStatus.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xE8, 0x11, 0x23));
                BorderHealthStatus.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0x11, 0x23));
                IconHealthStatus.Symbol = SymbolRegular.DismissCircle24;
                IconHealthStatus.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
                TxtHealthStatusTitle.Text = $"CORRUPTION DETECTED ({report.CorruptFiles} ISSUES)";
                TxtHealthStatusTitle.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
                TxtHealthStatusDesc.Text = "One or more entries failed header or CRC verification. Review details below:";
                PanelHealthIssues.Visibility = Visibility.Visible;
                ListHealthIssues.ItemsSource = report.Issues;
            }

            ModalHealthReport.Visibility = Visibility.Visible;
        }

        private void BtnCloseHealthReport_Click(object sender, RoutedEventArgs e)
        {
            ModalHealthReport.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Hardware Benchmark

        private void BtnBenchmark_Click(object sender, RoutedEventArgs e)
        {
            ModalBenchmark.Visibility = Visibility.Visible;
        }

        private void BtnCloseBenchmark_Click(object sender, RoutedEventArgs e)
        {
            _benchmarkCts?.Cancel();
            ModalBenchmark.Visibility = Visibility.Collapsed;
        }

        private async void BtnRunBenchmarkAction_Click(object sender, RoutedEventArgs e)
        {
            BtnRunBenchmarkAction.IsEnabled = false;
            ProgressBenchmark.Visibility = Visibility.Visible;
            ProgressBenchmark.Value = 0;
            TxtBenchmarkScore.Text = "Testing...";
            TxtBenchmarkTier.Text = "Running multi-threaded stress workload across all cores...";
            TxtBenchCompression.Text = "Measuring...";
            TxtBenchDecompression.Text = "Waiting...";
            TxtBenchCores.Text = $"{Environment.ProcessorCount} Threads";

            _benchmarkCts = new CancellationTokenSource();

            var progress = new Progress<double>(pct =>
            {
                ProgressBenchmark.Value = pct;
            });

            try
            {
                var result = await _archiveService.RunBenchmarkAsync(progress, _benchmarkCts.Token);

                TxtBenchmarkScore.Text = $"{result.ZenScore:N0}";
                TxtBenchmarkTier.Text = $"Rating: {result.RatingTier}";
                TxtBenchCompression.Text = result.FormattedCompression;
                TxtBenchDecompression.Text = result.FormattedDecompression;
                TxtBenchCores.Text = $"{result.CpuThreadsUsed} Cores";
                ProgressBenchmark.Value = 100;
            }
            catch (OperationCanceledException)
            {
                TxtBenchmarkScore.Text = "Cancelled";
                TxtBenchmarkTier.Text = "Benchmark was cancelled.";
            }
            catch (Exception ex)
            {
                TxtBenchmarkScore.Text = "Error";
                TxtBenchmarkTier.Text = ex.Message;
                MessageBox.Show($"Benchmark failed: {ex.Message}", "Benchmark Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRunBenchmarkAction.IsEnabled = true;
                _benchmarkCts?.Dispose();
                _benchmarkCts = null;
            }
        }

        #endregion

        #region Batch Processing Queue

        private void BtnBatchQueue_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtBatchDestination.Text))
            {
                if (!string.IsNullOrEmpty(_currentArchivePath))
                {
                    TxtBatchDestination.Text = Path.GetDirectoryName(_currentArchivePath) ?? string.Empty;
                }
                else
                {
                    TxtBatchDestination.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
            }
            ListBatchArchives.ItemsSource = null;
            ListBatchArchives.ItemsSource = _batchQueueArchives;
            ModalBatchQueue.Visibility = Visibility.Visible;
        }

        private void BtnCloseBatchQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_batchCts != null)
            {
                _batchCts.Cancel();
            }
            ModalBatchQueue.Visibility = Visibility.Collapsed;
        }

        private void BtnAddArchivesToBatch_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Add Archives to Queue",
                Multiselect = true,
                Filter = "Archive Files (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!_batchQueueArchives.Contains(file))
                    {
                        _batchQueueArchives.Add(file);
                    }
                }
                ListBatchArchives.ItemsSource = null;
                ListBatchArchives.ItemsSource = _batchQueueArchives;

                if (string.IsNullOrEmpty(TxtBatchDestination.Text) && _batchQueueArchives.Count > 0)
                {
                    TxtBatchDestination.Text = Path.GetDirectoryName(_batchQueueArchives[0]) ?? string.Empty;
                }
            }
        }

        private void BtnClearBatchQueue_Click(object sender, RoutedEventArgs e)
        {
            _batchQueueArchives.Clear();
            ListBatchArchives.ItemsSource = null;
        }

        private void BtnBrowseBatchDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Root Folder",
                Multiselect = false,
                InitialDirectory = !string.IsNullOrEmpty(TxtBatchDestination.Text) && Directory.Exists(TxtBatchDestination.Text)
                    ? TxtBatchDestination.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog(this) == true)
            {
                TxtBatchDestination.Text = dialog.FolderName;
            }
        }

        private async void BtnStartBatchAction_Click(object sender, RoutedEventArgs e)
        {
            if (_batchQueueArchives.Count == 0)
            {
                MessageBox.Show("Please add at least one archive to the batch queue.", "Queue Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destFolder = TxtBatchDestination.Text.Trim();
            if (string.IsNullOrEmpty(destFolder))
            {
                destFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                TxtBatchDestination.Text = destFolder;
            }

            if (!Directory.Exists(destFolder))
            {
                try
                {
                    Directory.CreateDirectory(destFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not create destination folder:\n\n{ex.Message}", "Invalid Destination", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            int operationIndex = CmbBatchOperation.SelectedIndex; // 0 = Smart Extract, 1 = Convert ZIP, 2 = Convert 7z

            BtnStartBatchAction.IsEnabled = false;
            PanelBatchProgress.Visibility = Visibility.Visible;
            _batchCts = new CancellationTokenSource();

            var sw = Stopwatch.StartNew();
            int succeeded = 0;
            int failed = 0;
            var failures = new List<string>();

            try
            {
                for (int i = 0; i < _batchQueueArchives.Count; i++)
                {
                    _batchCts.Token.ThrowIfCancellationRequested();

                    string archivePath = _batchQueueArchives[i];
                    string fileName = Path.GetFileName(archivePath);
                    int currentNumber = i + 1;

                    double overallBase = ((double)i / _batchQueueArchives.Count) * 100.0;
                    BarBatchOverall.Value = overallBase;
                    TxtBatchOverallPercent.Text = $"{overallBase:0}%";
                    TxtBatchOverallStatus.Text = $"Processing {currentNumber}/{_batchQueueArchives.Count}: {fileName}";

                    var itemProgress = new Progress<ArchiveProgressReport>(report =>
                    {
                        BarBatchItem.Value = report.Percentage;
                        TxtBatchItemPercent.Text = $"{report.Percentage:0}%";
                        TxtBatchItemStatus.Text = report.StatusMessage;

                        double weighted = (report.Percentage / 100.0) / _batchQueueArchives.Count * 100.0;
                        double liveTotal = Math.Min(100.0, overallBase + weighted);
                        BarBatchOverall.Value = liveTotal;
                        TxtBatchOverallPercent.Text = $"{liveTotal:0}%";
                    });

                    try
                    {
                        if (operationIndex == 0)
                        {
                            // Smart Extract All
                            await _archiveService.ExtractArchiveAsync(
                                archivePath,
                                destFolder,
                                smartExtract: true,
                                password: null,
                                progress: itemProgress,
                                cancellationToken: _batchCts.Token);
                        }
                        else
                        {
                            // Convert to ZIP (1) or 7Z (2)
                            var targetFormat = operationIndex == 1 ? CompressionFormat.Zip : CompressionFormat.SevenZip;
                            string targetExt = operationIndex == 1 ? ".zip" : ".7z";
                            string nameNoExt = Path.GetFileNameWithoutExtension(archivePath);
                            string targetFile = Path.Combine(destFolder, nameNoExt + targetExt);

                            string tempDir = Path.Combine(Path.GetTempPath(), "ZenBatch_" + Guid.NewGuid().ToString("N"));
                            try
                            {
                                Directory.CreateDirectory(tempDir);
                                await _archiveService.ExtractArchiveAsync(
                                    archivePath,
                                    tempDir,
                                    smartExtract: false,
                                    password: null,
                                    progress: itemProgress,
                                    cancellationToken: _batchCts.Token);

                                var sources = Directory.GetFileSystemEntries(tempDir);
                                await _archiveService.CreateArchiveAsync(
                                    targetFile,
                                    sources,
                                    targetFormat,
                                    CompressionLevel.Normal,
                                    password: null,
                                    encryptFileNames: false,
                                    progress: itemProgress,
                                    cancellationToken: _batchCts.Token);
                            }
                            finally
                            {
                                if (Directory.Exists(tempDir))
                                {
                                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                                }
                            }
                        }

                        succeeded++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add($"{fileName}: {ex.Message}");
                    }
                }

                sw.Stop();
                BarBatchOverall.Value = 100;
                TxtBatchOverallPercent.Text = "100%";
                BarBatchItem.Value = 100;
                TxtBatchItemPercent.Text = "100%";
                TxtBatchOverallStatus.Text = $"Batch completed in {sw.Elapsed.TotalSeconds:0.1}s! ({succeeded} succeeded, {failed} failed)";
                TxtBatchItemStatus.Text = "Ready";

                string summary = $"Batch Queue Completed in {sw.Elapsed.TotalSeconds:0.1}s.\n\nSucceeded: {succeeded}\nFailed: {failed}";
                if (failures.Count > 0)
                {
                    summary += "\n\nFailures:\n" + string.Join("\n", failures);
                }

                _lastOutputDirectory = destFolder;
                BtnOpenDestinationFolder.Visibility = Visibility.Visible;

                MessageBox.Show(summary, "Batch Processing Complete", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                TxtBatchOverallStatus.Text = "Batch processing cancelled.";
                TxtBatchItemStatus.Text = "Cancelled by user";
            }
            finally
            {
                BtnStartBatchAction.IsEnabled = true;
                _batchCts?.Dispose();
                _batchCts = null;
            }
        }

        #endregion

        #region Drag & Drop

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var archiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz" };
                    int archiveCount = files.Count(f => archiveExtensions.Contains(Path.GetExtension(f)));

                    if (archiveCount > 1)
                    {
                        TxtDragDropPrompt.Text = $"Drop {archiveCount} archives to add to Batch Processing Queue";
                    }
                    else if (archiveCount == 1 && files.Length == 1)
                    {
                        TxtDragDropPrompt.Text = "Drop archive to inspect in ZenArchieve";
                    }
                    else
                    {
                        TxtDragDropPrompt.Text = "Drop files to compress into a new archive";
                    }
                }

                BorderDragOverlay.Visibility = Visibility.Visible;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            BorderDragOverlay.Visibility = Visibility.Collapsed;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            BorderDragOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var archiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz" };
                    var archiveFiles = files.Where(f => archiveExtensions.Contains(Path.GetExtension(f))).ToList();

                    if (archiveFiles.Count > 1)
                    {
                        // Multi-archive drop -> Open Batch Queue modal
                        _batchQueueArchives.Clear();
                        _batchQueueArchives.AddRange(archiveFiles);
                        ListBatchArchives.ItemsSource = null;
                        ListBatchArchives.ItemsSource = _batchQueueArchives;
                        TxtBatchDestination.Text = Path.GetDirectoryName(archiveFiles[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        ModalBatchQueue.Visibility = Visibility.Visible;
                    }
                    else if (archiveFiles.Count == 1 && files.Length == 1)
                    {
                        // Open archive
                        _currentArchivePassword = null;
                        await LoadArchiveAsync(archiveFiles[0]);
                    }
                    else
                    {
                        // Uncompressed files or folders dropped -> prompt archive creation modal!
                        OpenCreateArchiveModal(files);
                    }
                }
            }
        }

        #endregion

        #region Search & Filtering

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_allItems == null || _allItems.Count == 0)
            {
                DataGridFiles.ItemsSource = null;
                return;
            }

            string filter = TxtFilter.Text.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                DataGridFiles.ItemsSource = _allItems;
            }
            else
            {
                DataGridFiles.ItemsSource = _allItems
                    .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                i.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                i.Extension.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        #endregion

        #region Helpers & UI Control

        private void BtnCancelOperation_Click(object sender, RoutedEventArgs e)
        {
            _operationCts?.Cancel();
            ShowStatus("Cancelling operation...", SymbolRegular.ArrowClockwise24);
        }

        private void BtnOpenDestinationFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastOutputDirectory) && Directory.Exists(_lastOutputDirectory))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _lastOutputDirectory,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void SetUiBusy(bool isBusy, string? message = null)
        {
            BtnOpenArchive.IsEnabled = !isBusy;
            BtnNewArchive.IsEnabled = !isBusy;
            BtnExtractHere.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnExtractTo.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnSmartExtract.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnTestArchive.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnAnalyzeSize.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnConvertFormat.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnChecksum.IsEnabled = !isBusy && _currentArchivePath != null;
            BtnCompareArchives.IsEnabled = !isBusy;
            BtnBatchQueue.IsEnabled = !isBusy;
            BtnBenchmark.IsEnabled = !isBusy;
            if (message != null)
            {
                ShowStatus(message, SymbolRegular.ArrowClockwise24);
            }
        }

        private void SetOperationUiActive(bool active, string? initialMessage = null)
        {
            SetUiBusy(active, initialMessage);
            ProgressBarOperation.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            ProgressBarOperation.IsIndeterminate = false;
            ProgressBarOperation.Value = 0;
            TxtProgressPercent.Text = active ? "0%" : string.Empty;
            BtnCancelOperation.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active)
            {
                BtnOpenDestinationFolder.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowStatus(string message, SymbolRegular symbol, bool isError = false)
        {
            TxtStatus.Text = message;
            IconStatus.Symbol = symbol;

            if (isError)
            {
                IconStatus.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
            }
            else if (symbol == SymbolRegular.CheckmarkCircle24)
            {
                IconStatus.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                IconStatus.Foreground = (Brush)FindResource("AccentTextFillColorPrimaryBrush");
            }
        }

        #endregion

        #region New Features (v2.0) — Compare, Analyze, Convert, Checksum

        /// <summary>
        /// Compare two archives side-by-side (Feature not available in WinRAR or 7-Zip).
        /// </summary>
        private async void BtnCompareArchives_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Pick first archive (or use currently loaded one)
                string? archiveA = _currentArchivePath;
                if (string.IsNullOrEmpty(archiveA))
                {
                    var dlgA = new OpenFileDialog
                    {
                        Title = "Select First Archive (A)",
                        Filter = "Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|All Files (*.*)|*.*"
                    };
                    if (dlgA.ShowDialog(this) != true) return;
                    archiveA = dlgA.FileName;
                }

                // Pick second archive
                var dlgB = new OpenFileDialog
                {
                    Title = "Select Second Archive (B) to Compare Against",
                    Filter = "Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|All Files (*.*)|*.*"
                };
                if (dlgB.ShowDialog(this) != true) return;
                string archiveB = dlgB.FileName;

                SetUiBusy(true, $"Comparing archives...");
                ProgressBarOperation.IsIndeterminate = true;
                ProgressBarOperation.Visibility = Visibility.Visible;

                var result = await _archiveService.CompareArchivesAsync(
                    archiveA, archiveB,
                    _currentArchivePassword, null,
                    CancellationToken.None);

                // Build report message
                var sb = new StringBuilder();
                sb.AppendLine($"📊 Archive Comparison Report");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"Archive A: {result.ArchiveA}");
                sb.AppendLine($"Archive B: {result.ArchiveB}");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"{result.Summary}");
                sb.AppendLine();

                if (result.AreIdentical)
                {
                    sb.AppendLine("✅ Archives are IDENTICAL — no differences found.");
                }
                else
                {
                    if (result.AddedInB.Count > 0)
                    {
                        sb.AppendLine($"\n➕ ADDED in B ({result.AddedInB.Count}):");
                        foreach (var f in result.AddedInB.Take(50))
                            sb.AppendLine($"  + {f.Path} ({f.FormattedSizeB})");
                        if (result.AddedInB.Count > 50) sb.AppendLine($"  ... and {result.AddedInB.Count - 50} more");
                    }
                    if (result.RemovedFromA.Count > 0)
                    {
                        sb.AppendLine($"\n➖ REMOVED from A ({result.RemovedFromA.Count}):");
                        foreach (var f in result.RemovedFromA.Take(50))
                            sb.AppendLine($"  - {f.Path} ({f.FormattedSizeA})");
                        if (result.RemovedFromA.Count > 50) sb.AppendLine($"  ... and {result.RemovedFromA.Count - 50} more");
                    }
                    if (result.Modified.Count > 0)
                    {
                        sb.AppendLine($"\n✏️ MODIFIED ({result.Modified.Count}):");
                        foreach (var f in result.Modified.Take(50))
                            sb.AppendLine($"  ~ {f.Path} ({f.FormattedSizeA} → {f.FormattedSizeB}, {f.SizeDelta})");
                        if (result.Modified.Count > 50) sb.AppendLine($"  ... and {result.Modified.Count - 50} more");
                    }
                }

                ShowStatus($"Comparison complete: {result.Summary}", SymbolRegular.CheckmarkCircle24);
                MessageBox.Show(sb.ToString(), "ZenArchieve — Archive Comparison", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatus($"Comparison failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Archive comparison failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Visibility = Visibility.Hidden;
                SetUiBusy(false);
            }
        }

        /// <summary>
        /// Visual archive size analyzer (Feature not available in WinRAR or 7-Zip).
        /// </summary>
        private void BtnAnalyzeSize_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems == null || _allItems.Count == 0 || string.IsNullOrEmpty(_currentArchivePath))
            {
                ShowStatus("No archive loaded to analyze.", SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            try
            {
                var files = _allItems.Where(i => !i.IsDirectory).ToList();
                long totalSize = files.Sum(i => i.Size);

                // Group by extension
                var byExtension = files
                    .GroupBy(f => string.IsNullOrEmpty(f.Extension) ? "(no ext)" : f.Extension.ToUpperInvariant())
                    .Select(g => new { Extension = g.Key, Size = g.Sum(f => f.Size), Count = g.Count() })
                    .OrderByDescending(g => g.Size)
                    .Take(15)
                    .ToList();

                // Group by top-level folder
                var byFolder = files
                    .GroupBy(f =>
                    {
                        int slash = f.Path.IndexOf('/');
                        return slash > 0 ? f.Path.Substring(0, slash) : "(root)";
                    })
                    .Select(g => new { Folder = g.Key, Size = g.Sum(f => f.Size), Count = g.Count() })
                    .OrderByDescending(g => g.Size)
                    .Take(15)
                    .ToList();

                // Find largest files
                var largestFiles = files
                    .OrderByDescending(f => f.Size)
                    .Take(10)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"📊 Archive Size Analysis — {Path.GetFileName(_currentArchivePath)}");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"Total: {ArchiveItemInfo.FormatBytes(totalSize)} across {files.Count} files");
                sb.AppendLine();

                sb.AppendLine($"📁 BY FOLDER (Top {byFolder.Count}):");
                foreach (var g in byFolder)
                {
                    double pct = totalSize > 0 ? ((double)g.Size / totalSize) * 100 : 0;
                    int barLen = (int)Math.Max(1, pct / 2);
                    string bar = new string('█', barLen) + new string('░', 50 - barLen);
                    sb.AppendLine($"  {bar} {pct:0.1}% {g.Folder} ({ArchiveItemInfo.FormatBytes(g.Size)}, {g.Count} files)");
                }

                sb.AppendLine();
                sb.AppendLine($"📄 BY FILE TYPE (Top {byExtension.Count}):");
                foreach (var g in byExtension)
                {
                    double pct = totalSize > 0 ? ((double)g.Size / totalSize) * 100 : 0;
                    int barLen = (int)Math.Max(1, pct / 2);
                    string bar = new string('█', barLen) + new string('░', 50 - barLen);
                    sb.AppendLine($"  {bar} {pct:0.1}% .{g.Extension} ({ArchiveItemInfo.FormatBytes(g.Size)}, {g.Count} files)");
                }

                sb.AppendLine();
                sb.AppendLine($"🏆 LARGEST FILES (Top {largestFiles.Count}):");
                int rank = 1;
                foreach (var f in largestFiles)
                {
                    double pct = totalSize > 0 ? ((double)f.Size / totalSize) * 100 : 0;
                    sb.AppendLine($"  {rank}. {f.Path} — {f.FormattedSize} ({pct:0.1}%)");
                    rank++;
                }

                ShowStatus($"Size analysis complete for {Path.GetFileName(_currentArchivePath)}", SymbolRegular.CheckmarkCircle24);
                MessageBox.Show(sb.ToString(), "ZenArchieve — Size Analyzer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatus($"Analysis failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Size analysis failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// One-click archive format conversion (Feature not available in WinRAR or 7-Zip).
        /// </summary>
        private async void BtnConvertFormat_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath) || !File.Exists(_currentArchivePath))
            {
                ShowStatus("No archive loaded to convert.", SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            try
            {
                string ext = Path.GetExtension(_currentArchivePath).ToLowerInvariant();
                CompressionFormat targetFormat;
                string targetExt;

                // Auto-detect: if ZIP → convert to 7Z, otherwise → convert to ZIP
                if (ext == ".zip")
                {
                    targetFormat = CompressionFormat.SevenZip;
                    targetExt = ".7z";
                }
                else
                {
                    targetFormat = CompressionFormat.Zip;
                    targetExt = ".zip";
                }

                string defaultName = Path.GetFileNameWithoutExtension(_currentArchivePath) + targetExt;
                string? outputDir = Path.GetDirectoryName(_currentArchivePath);
                string targetPath = Path.Combine(outputDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), defaultName);

                // Confirm with user
                var confirmResult = MessageBox.Show(
                    $"Convert '{Path.GetFileName(_currentArchivePath)}' to {targetFormat} format?\n\n" +
                    $"Output: {targetPath}\n\n" +
                    $"This will extract the archive contents and re-compress them in the new format.",
                    "ZenArchieve — Convert Format",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes) return;

                _operationCts = new CancellationTokenSource();
                SetOperationUiActive(true, $"Converting to {targetFormat}...");

                var progress = new Progress<ArchiveProgressReport>(report =>
                {
                    ProgressBarOperation.Value = report.Percentage;
                    TxtProgressPercent.Text = $"{report.Percentage:0}%";
                    ShowStatus(report.StatusMessage, SymbolRegular.ArrowClockwise24);
                });

                await _archiveService.ConvertArchiveAsync(
                    _currentArchivePath, targetPath, targetFormat,
                    CompressionLevel.Normal, _currentArchivePassword,
                    progress, _operationCts.Token);

                ShowStatus($"Conversion complete: {Path.GetFileName(targetPath)}", SymbolRegular.CheckmarkCircle24);
                MessageBox.Show(
                    $"Archive successfully converted!\n\n" +
                    $"Source: {Path.GetFileName(_currentArchivePath)}\n" +
                    $"Output: {targetPath}",
                    "ZenArchieve — Conversion Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Optionally open the converted archive
                await LoadArchiveAsync(targetPath);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("Conversion cancelled.", SymbolRegular.ErrorCircle24, isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Conversion failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Format conversion failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetOperationUiActive(false);
            }
        }

        /// <summary>
        /// Full-file checksum calculator & verifier (Feature not available in WinRAR or 7-Zip).
        /// </summary>
        private async void BtnChecksum_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath) || !File.Exists(_currentArchivePath))
            {
                ShowStatus("No archive loaded for checksum.", SymbolRegular.ErrorCircle24, isError: true);
                return;
            }

            try
            {
                SetUiBusy(true, "Calculating checksums...");
                ProgressBarOperation.IsIndeterminate = true;
                ProgressBarOperation.Visibility = Visibility.Visible;

                var progressSha256 = new Progress<double>(p => { });

                // Calculate all three hashes
                string sha256 = await _archiveService.CalculateFileHashAsync(_currentArchivePath, "SHA256", progressSha256);
                string md5 = await _archiveService.CalculateFileHashAsync(_currentArchivePath, "MD5");
                string sha1 = await _archiveService.CalculateFileHashAsync(_currentArchivePath, "SHA1");

                var fileInfo = new FileInfo(_currentArchivePath);

                var sb = new StringBuilder();
                sb.AppendLine($"🔐 Checksum Report — {Path.GetFileName(_currentArchivePath)}");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"File Size: {ArchiveItemInfo.FormatBytes(fileInfo.Length)}");
                sb.AppendLine();
                sb.AppendLine($"SHA-256:");
                sb.AppendLine($"  {sha256}");
                sb.AppendLine();
                sb.AppendLine($"MD5:");
                sb.AppendLine($"  {md5}");
                sb.AppendLine();
                sb.AppendLine($"SHA-1:");
                sb.AppendLine($"  {sha1}");
                sb.AppendLine();
                sb.AppendLine($"💡 Tip: Copy a hash above and paste an expected hash\n    to verify file integrity (e.g., from a download page).");

                ShowStatus($"Checksums calculated for {Path.GetFileName(_currentArchivePath)}", SymbolRegular.CheckmarkCircle24);

                // Copy SHA-256 to clipboard automatically
                try { Clipboard.SetText(sha256); } catch { }

                MessageBox.Show(
                    sb.ToString() + "\n\n✅ SHA-256 hash has been copied to your clipboard.",
                    "ZenArchieve — Checksum Verifier",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatus($"Checksum failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Checksum calculation failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Visibility = Visibility.Hidden;
                SetUiBusy(false);
            }
        }

        private void ToolbarScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scv)
            {
                scv.ScrollToHorizontalOffset(scv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        #endregion

        #region Duplicate Finder & Merge Archives (v2.1 Exclusive Features)

        private async void BtnFindDuplicates_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArchivePath)) return;

            try
            {
                SetUiBusy(true, "Scanning for duplicate files...");
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Value = 0;
                ProgressBarOperation.Visibility = Visibility.Visible;

                var progress = new Progress<double>(p =>
                {
                    ProgressBarOperation.Value = p;
                    TxtStatus.Text = $"Scanning for duplicates: {p:0}% complete";
                });

                var report = await _archiveService.FindDuplicatesAsync(
                    _currentArchivePath, _currentArchivePassword, progress);

                if (!report.HasDuplicates)
                {
                    ShowStatus("No duplicates found — archive is clean!", SymbolRegular.CheckmarkCircle24);
                    MessageBox.Show(
                        $"✅ No duplicate files found!\n\n" +
                        $"Scanned: {report.TotalFilesScanned} files\n" +
                        $"Every file in this archive is unique.",
                        "ZenArchieve — Duplicate Finder",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Build detailed report
                var sb = new StringBuilder();
                sb.AppendLine($"📋 Duplicate File Report — {Path.GetFileName(_currentArchivePath)}");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"Files Scanned: {report.TotalFilesScanned}");
                sb.AppendLine($"Duplicate Files Found: {report.TotalDuplicateFiles}");
                sb.AppendLine($"Wasted Space: {report.FormattedWastedSpace}");
                sb.AppendLine($"Duplicate Groups: {report.Groups.Count}");
                sb.AppendLine();

                int groupNum = 1;
                foreach (var group in report.Groups.OrderByDescending(g => g.FileSize))
                {
                    sb.AppendLine($"── Group {groupNum} ({group.FormattedSize} each, {group.DuplicateCount} duplicates) ──");
                    foreach (var file in group.Files)
                    {
                        sb.AppendLine($"   • {file}");
                    }
                    sb.AppendLine();
                    groupNum++;
                    if (groupNum > 25) // Limit display to 25 groups
                    {
                        sb.AppendLine($"   ... and {report.Groups.Count - 25} more groups");
                        break;
                    }
                }

                ShowStatus($"Found {report.TotalDuplicateFiles} duplicates wasting {report.FormattedWastedSpace}", SymbolRegular.DocumentCopy24);

                MessageBox.Show(
                    sb.ToString(),
                    "ZenArchieve — Duplicate Finder Report",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatus($"Duplicate scan failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Duplicate scan failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Visibility = Visibility.Hidden;
                SetUiBusy(false);
            }
        }

        private async void BtnMergeArchives_Click(object sender, RoutedEventArgs e)
        {
            // Step 1: Select Archive A
            var dialogA = new OpenFileDialog
            {
                Title = "Select First Archive to Merge (Archive A)",
                Filter = "All Supported Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialogA.ShowDialog(this) != true) return;

            // Step 2: Select Archive B
            var dialogB = new OpenFileDialog
            {
                Title = "Select Second Archive to Merge (Archive B)",
                Filter = "All Supported Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialogB.ShowDialog(this) != true) return;

            // Step 3: Select output path
            var saveDialog = new SaveFileDialog
            {
                Title = "Save Merged Archive As",
                Filter = "ZIP Archive (*.zip)|*.zip|7-Zip Archive (*.7z)|*.7z",
                DefaultExt = ".zip",
                FileName = "Merged_Archive.zip"
            };
            if (saveDialog.ShowDialog(this) != true) return;

            string outputPath = saveDialog.FileName;
            var format = Path.GetExtension(outputPath).Equals(".7z", StringComparison.OrdinalIgnoreCase)
                ? CompressionFormat.SevenZip
                : CompressionFormat.Zip;

            try
            {
                SetUiBusy(true, "Merging archives...");
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Value = 0;
                ProgressBarOperation.Visibility = Visibility.Visible;

                var progress = new Progress<ArchiveProgressReport>(report =>
                {
                    ProgressBarOperation.Value = report.Percentage;
                    TxtStatus.Text = report.StatusMessage;
                });

                var report = await _archiveService.MergeArchivesAsync(
                    dialogA.FileName, dialogB.FileName, outputPath, format,
                    progress: progress);

                ShowStatus($"Merged into {Path.GetFileName(outputPath)} ({report.FormattedOutputSize})", SymbolRegular.CheckmarkCircle24);

                var sb = new StringBuilder();
                sb.AppendLine($"✅ Archives Merged Successfully!");
                sb.AppendLine();
                sb.AppendLine($"Archive A: {Path.GetFileName(dialogA.FileName)}");
                sb.AppendLine($"Archive B: {Path.GetFileName(dialogB.FileName)}");
                sb.AppendLine();
                sb.AppendLine($"━━━ Merge Results ━━━");
                sb.AppendLine($"Files from A: {report.FilesFromA}");
                sb.AppendLine($"New files from B: {report.FilesFromB}");
                sb.AppendLine($"Files updated by B: {report.FilesOverridden}");
                sb.AppendLine($"Identical duplicates skipped: {report.DuplicatesSkipped}");
                sb.AppendLine();
                sb.AppendLine($"Total files in output: {report.TotalFilesInOutput}");
                sb.AppendLine($"Output size: {report.FormattedOutputSize}");
                sb.AppendLine($"Saved to: {outputPath}");

                var result = MessageBox.Show(
                    sb.ToString() + "\n\nWould you like to open the merged archive now?",
                    "ZenArchieve — Merge Complete",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await LoadArchiveAsync(outputPath);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Merge failed: {ex.Message}", SymbolRegular.ErrorCircle24, isError: true);
                MessageBox.Show($"Archive merge failed:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarOperation.IsIndeterminate = false;
                ProgressBarOperation.Visibility = Visibility.Hidden;
                SetUiBusy(false);
            }
        }

        #endregion
    }
}