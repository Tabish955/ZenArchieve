using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Archieve_App
{
    public partial class UpdateAvailableWindow : FluentWindow
    {
        private readonly UpdateInfo _update;
        private readonly CancellationTokenSource _cts = new();

        public UpdateAvailableWindow(UpdateInfo update)
        {
            InitializeComponent();
            _update = update;

            try
            {
                var iconUri = new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch { }

            TxtUpdateDetails.Text = $"v{update.CurrentVersion} → v{update.LatestVersion}  •  {update.FormattedSize}";
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            if (ChkDontRemind.IsChecked == true)
            {
                AppSettings.Instance.DisableAutoUpdateCheck = true;
                AppSettings.Instance.Save();
                AppSettings.Instance.UnregisterBootupUpdater();
            }
            Close();
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            BtnInstall.IsEnabled = false;
            BtnLater.IsEnabled = false;
            TxtUpdateTitle.Text = "Downloading update...";
            TxtUpdateDetails.Text = "Preparing installer...";

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ZenArchieve_Update");
                Directory.CreateDirectory(tempDir);
                string installerPath = Path.Combine(tempDir, _update.AssetName);
                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }

                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ZenArchieve-Updater/2.0");

                using var response = await http.GetAsync(_update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? _update.AssetSizeBytes;
                long bytesRead = 0;

                using var contentStream = await response.Content.ReadAsStreamAsync(_cts.Token);
                using var fileStream = new FileStream(installerPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024);

                byte[] buffer = new byte[256 * 1024];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, _cts.Token);
                    bytesRead += read;
                    if (totalBytes > 0)
                    {
                        double pct = Math.Min(100.0, (double)bytesRead / totalBytes * 100.0);
                        TxtUpdateDetails.Text = $"Downloading... {pct:0}%  ({ArchiveItemInfo.FormatBytes(bytesRead)} / {_update.FormattedSize})";
                    }
                    else
                    {
                        TxtUpdateDetails.Text = $"Downloading... {ArchiveItemInfo.FormatBytes(bytesRead)}";
                    }
                }

                await fileStream.FlushAsync(_cts.Token);
                fileStream.Close();

                TxtUpdateTitle.Text = "Starting installer...";
                TxtUpdateDetails.Text = "Please approve the UAC prompt to complete the update.";

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true,
                    Verb = "runas"
                });

                // Exit the app so the installer can replace files
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed:\n\n{ex.Message}\n\nYou can try again later from inside ZenArchieve.",
                    "ZenArchieve — Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { _cts.Cancel(); } catch { }
        }
    }
}
