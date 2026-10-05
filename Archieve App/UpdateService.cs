using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Archieve_App
{
    /// <summary>
    /// Handles automatic update checking and in-place upgrade via GitHub Releases API.
    /// Inno Setup uses the same AppId across versions, so running a new installer
    /// automatically upgrades in-place — no manual uninstall needed.
    /// </summary>
    public class UpdateService
    {
        private const string GitHubReleasesUrl = "https://api.github.com/repos/Tabish955/ZenArchieve/releases/latest";
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZenArchieve-UpdateChecker/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>
        /// Gets the current application version from the assembly.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var fileVersion = asm.GetName().Version;
            return fileVersion ?? new Version(2, 0, 0);
        }

        /// <summary>
        /// Checks GitHub Releases API for the latest release.
        /// Returns null if no update is available or if the check fails.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(GitHubReleasesUrl, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release == null) return null;

                // Parse version from tag_name (e.g. "v2.1.0" → 2.1.0)
                string versionStr = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(versionStr, out var latestVersion)) return null;

                var currentVersion = GetCurrentVersion();

                // Compare: only consider major.minor.build (ignore revision)
                var currentComparable = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build >= 0 ? currentVersion.Build : 0);
                var latestComparable = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build >= 0 ? latestVersion.Build : 0);

                if (latestComparable <= currentComparable)
                    return null; // Already up to date

                // Find the .exe asset (installer)
                string? downloadUrl = null;
                string? assetName = null;
                long assetSize = 0;

                if (release.Assets != null)
                {
                    // Prioritize ZenArchieve_Setup installer asset, fallback to any .exe asset
                    var preferredAsset = release.Assets.FirstOrDefault(a => 
                        a.Name != null && 
                        a.Name.StartsWith("ZenArchieve", StringComparison.OrdinalIgnoreCase) && 
                        a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        ?? release.Assets.FirstOrDefault(a => 
                        a.Name != null && 
                        a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                    if (preferredAsset != null)
                    {
                        downloadUrl = preferredAsset.BrowserDownloadUrl;
                        assetName = preferredAsset.Name;
                        assetSize = preferredAsset.Size;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return null;

                return new UpdateInfo
                {
                    CurrentVersion = currentComparable,
                    LatestVersion = latestComparable,
                    ReleaseName = release.Name ?? $"v{latestVersion}",
                    ReleaseNotes = release.Body ?? "No release notes available.",
                    DownloadUrl = downloadUrl,
                    AssetName = assetName ?? "ZenArchieve_Setup.exe",
                    AssetSizeBytes = assetSize,
                    ReleaseUrl = release.HtmlUrl ?? ""
                };
            }
            catch
            {
                return null; // Silently fail — don't disrupt the user
            }
        }

        /// <summary>
        /// Downloads the latest installer to a temp directory and launches it with /VERYSILENT
        /// for an in-place upgrade. Inno Setup's same AppId ensures no uninstall is needed.
        /// </summary>
        public static async Task<bool> DownloadAndInstallUpdateAsync(
            UpdateInfo updateInfo,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ZenArchieve_Update");
                Directory.CreateDirectory(tempDir);
                string installerPath = Path.Combine(tempDir, updateInfo.AssetName);

                // Delete any previous download
                if (File.Exists(installerPath))
                    File.Delete(installerPath);

                // Download with progress reporting
                using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? updateInfo.AssetSizeBytes;
                long bytesRead = 0;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024);

                byte[] buffer = new byte[256 * 1024];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                    bytesRead += read;
                    if (totalBytes > 0)
                    {
                        progress?.Report(Math.Min(100.0, (double)bytesRead / totalBytes * 100.0));
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                fileStream.Close();

                progress?.Report(100.0);

                // Launch installer with /VERYSILENT for seamless in-place upgrade
                // /SUPPRESSMSGBOXES avoids any prompts
                // /CLOSEAPPLICATIONS tells it to close ZenArchieve before upgrading
                // /RESTARTAPPLICATIONS=no avoids auto-relaunch
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                });

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class UpdateInfo
    {
        public Version CurrentVersion { get; set; } = new(0, 0, 0);
        public Version LatestVersion { get; set; } = new(0, 0, 0);
        public string ReleaseName { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";
        public long AssetSizeBytes { get; set; }
        public string ReleaseUrl { get; set; } = "";

        public string FormattedSize => AssetSizeBytes > 0
            ? $"{AssetSizeBytes / (1024.0 * 1024.0):0.1} MB"
            : "Unknown size";
    }

    // JSON deserialization models for GitHub Releases API
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
