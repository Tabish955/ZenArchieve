using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Archieve_App
{
    public partial class ExtractionProgressWindow : FluentWindow
    {
        private readonly ArchiveService _archiveService = new();
        private readonly string _archivePath;
        private readonly string _baseDestination;
        private readonly bool _smartExtract;
        private string? _currentPassword;

        private CancellationTokenSource? _cts;
        private readonly Stopwatch _stopwatch = new();
        private readonly DispatcherTimer _timer = new();
        private string? _resultDirectory;
        private bool _isCompleted = false;

        public ExtractionProgressWindow(string archivePath, bool smartExtract, string? customDestination = null)
        {
            InitializeComponent();

            _archivePath = archivePath;
            _smartExtract = smartExtract;

            if (!string.IsNullOrEmpty(customDestination))
            {
                _baseDestination = customDestination;
            }
            else
            {
                string? dir = Path.GetDirectoryName(archivePath);
                _baseDestination = string.IsNullOrEmpty(dir) 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) 
                    : dir;
            }

            try
            {
                var iconUri = new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch { }

            string fileName = Path.GetFileName(archivePath);
            Title = $"Extracting - {fileName}";
            TxtArchiveName.Text = fileName;
            TxtDestinationPath.Text = $"Destination: {_baseDestination}";
            TxtDestinationPath.ToolTip = _baseDestination;

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += (s, e) =>
            {
                if (_stopwatch.IsRunning)
                {
                    TxtElapsed.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
                }
            };

            Loaded += async (s, e) => await BeginExtractionAsync();
        }

        private async Task BeginExtractionAsync(string? password = null)
        {
            _currentPassword = password;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            PanelPasswordView.Visibility = Visibility.Collapsed;
            PanelCompletionView.Visibility = Visibility.Collapsed;
            PanelProgressView.Visibility = Visibility.Visible;
            TxtPasswordError.Visibility = Visibility.Collapsed;

            _stopwatch.Restart();
            _timer.Start();

            ProgressBarExtraction.IsIndeterminate = true;

            var progress = new Progress<ArchiveProgressReport>(report =>
            {
                if (report.Percentage > 0)
                {
                    ProgressBarExtraction.IsIndeterminate = false;
                    ProgressBarExtraction.Value = Math.Min(100.0, report.Percentage);
                    TxtPercent.Text = $"{report.Percentage:0}%";
                }
                else
                {
                    ProgressBarExtraction.IsIndeterminate = true;
                    TxtPercent.Text = "Extracting...";
                }

                if (!string.IsNullOrEmpty(report.CurrentFileName))
                {
                    TxtCurrentFile.Text = report.CurrentFileName;
                }

                if (report.TotalItems > 0)
                {
                    TxtItemCount.Text = $"{report.ItemsExtracted} / {report.TotalItems} files";
                }
                else if (report.ItemsExtracted > 0)
                {
                    TxtItemCount.Text = $"{report.ItemsExtracted} files";
                }

                double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
                if (elapsedSec > 0.3 && report.BytesProcessed > 0)
                {
                    double mbPerSec = (report.BytesProcessed / (1024.0 * 1024.0)) / elapsedSec;
                    TxtSpeed.Text = $"{mbPerSec:0.0} MB/s";
                }
            });

            try
            {
                _resultDirectory = await _archiveService.ExtractArchiveAsync(
                    _archivePath,
                    _baseDestination,
                    _smartExtract,
                    _currentPassword,
                    progress,
                    _cts.Token);

                _stopwatch.Stop();
                _timer.Stop();
                _isCompleted = true;

                if (ChkCloseWhenFinished.IsChecked == true)
                {
                    // 7-Zip standard: silently and cleanly close immediately on success
                    Close();
                }
                else
                {
                    // User requested window to stay open
                    PanelProgressView.Visibility = Visibility.Collapsed;
                    PanelPasswordView.Visibility = Visibility.Collapsed;
                    PanelCompletionView.Visibility = Visibility.Visible;

                    int count = Directory.Exists(_resultDirectory) 
                        ? Directory.GetFiles(_resultDirectory, "*", SearchOption.AllDirectories).Length 
                        : 0;

                    TxtCompletionSummary.Text = $"Extracted {count} files in {_stopwatch.Elapsed.TotalSeconds:0.1}s into {Path.GetFileName(_resultDirectory)}";
                    BtnCancel.Content = "Close";
                }
            }
            catch (ArchiveEncryptedException ex)
            {
                _stopwatch.Stop();
                _timer.Stop();

                // Show in-dialog password challenge
                PanelProgressView.Visibility = Visibility.Collapsed;
                PanelCompletionView.Visibility = Visibility.Collapsed;
                PanelPasswordView.Visibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(password))
                {
                    TxtPasswordError.Text = "Incorrect password. Please try again.";
                    TxtPasswordError.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtPasswordError.Text = ex.Message;
                    TxtPasswordError.Visibility = Visibility.Visible;
                }

                TxtPasswordInput.Password = string.Empty;
                TxtPasswordInput.Focus();
            }
            catch (OperationCanceledException)
            {
                _stopwatch.Stop();
                _timer.Stop();
                Close();
            }
            catch (Exception ex)
            {
                _stopwatch.Stop();
                _timer.Stop();
                MessageBox.Show($"Extraction failed:\n\n{ex.Message}", "ZenArchieve - Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private async void BtnSubmitPassword_Click(object sender, RoutedEventArgs e)
        {
            string password = TxtPasswordInput.Password;
            if (string.IsNullOrEmpty(password))
            {
                TxtPasswordError.Text = "Please enter a password.";
                TxtPasswordError.Visibility = Visibility.Visible;
                return;
            }

            await BeginExtractionAsync(password);
        }

        private void TxtPasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSubmitPassword_Click(sender, e);
            }
        }

        private void BtnBackground_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompleted)
            {
                Close();
                return;
            }

            _cts?.Cancel();
            Close();
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_resultDirectory) && Directory.Exists(_resultDirectory))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _resultDirectory,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _timer.Stop();
            _stopwatch.Stop();
            _cts?.Cancel();
            _cts?.Dispose();

            // If MainWindow is not open, ensure the application process exits completely
            if (Application.Current != null)
            {
                bool hasMainWindow = false;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win is MainWindow)
                    {
                        hasMainWindow = true;
                        break;
                    }
                }

                if (!hasMainWindow)
                {
                    Application.Current.Shutdown();
                }
            }
        }
    }
}
