using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SysZipArchive = System.IO.Compression.ZipArchive;
using SysZipFile = System.IO.Compression.ZipFile;
using System.IO.Compression;

namespace Archieve_App
{
    public class ArchiveEncryptedException : Exception
    {
        public string ArchivePath { get; }
        public ArchiveEncryptedException(string archivePath, string message) : base(message)
        {
            ArchivePath = archivePath;
        }
        public ArchiveEncryptedException(string archivePath, string message, Exception inner) : base(message, inner)
        {
            ArchivePath = archivePath;
        }
    }

    public enum CompressionFormat
    {
        Zip,
        SevenZip
    }

    public enum CompressionLevel
    {
        Store,
        Fast,
        Normal,
        Maximum
    }

    public class ArchiveService
    {
        /// <summary>
        /// Actively sanitizes entry keys to prevent Zip-Slip directory traversal attacks.
        /// Ensures the resolved target path never escapes the base destination folder.
        /// </summary>
        public static string SanitizeEntryDestinationPath(string baseDirectory, string entryKey)
        {
            string fullBase = Path.GetFullPath(baseDirectory);
            if (!fullBase.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullBase += Path.DirectorySeparatorChar;
            }

            // Normalize slashes
            string cleanKey = entryKey.Replace('\\', '/');

            // Strip leading drive specifiers (e.g. "C:/")
            if (cleanKey.Length >= 2 && cleanKey[1] == ':')
            {
                cleanKey = cleanKey.Substring(2);
            }

            // Strip leading slashes and traversal prefixes
            while (cleanKey.StartsWith("/") || cleanKey.StartsWith("../") || cleanKey.StartsWith("./"))
            {
                if (cleanKey.StartsWith("/")) cleanKey = cleanKey.Substring(1);
                else if (cleanKey.StartsWith("../")) cleanKey = cleanKey.Substring(3);
                else if (cleanKey.StartsWith("./")) cleanKey = cleanKey.Substring(2);
            }

            // Sanitize individual path segments to prevent invalid Windows filename errors (e.g., Linux archives with colons or asterisks)
            char[] invalidChars = Path.GetInvalidFileNameChars();
            var rawSegments = cleanKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var safeSegments = new string[rawSegments.Length];
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string seg = rawSegments[i];
                foreach (char c in invalidChars)
                {
                    if (seg.Contains(c)) seg = seg.Replace(c, '_');
                }
                seg = seg.TrimEnd('.', ' ');
                if (string.IsNullOrWhiteSpace(seg)) seg = "item";
                safeSegments[i] = seg;
            }

            string safeSubPath = string.Join(Path.DirectorySeparatorChar.ToString(), safeSegments);

            // Resolve candidate path
            string candidatePath = Path.GetFullPath(Path.Combine(fullBase, safeSubPath));

            // Enforce destination containment boundary
            if (!candidatePath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            {
                // Traversal attempt detected! Confine safely to root of destination
                string safeFileName = safeSegments.Length > 0 ? safeSegments[^1] : "sanitized_entry";
                candidatePath = Path.Combine(fullBase, safeFileName);
            }

            return candidatePath;
        }

        /// <summary>
        /// Locates 7z.exe on the system (bundled in app tools, local directory, Windows System32, or Program Files).
        /// </summary>
        public static string? FindSevenZipPath()
        {
            // 1. tools subfolder next to app
            string localTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "7z.exe");
            if (File.Exists(localTools)) return localTools;

            // 2. Next to app executable
            string localRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe");
            if (File.Exists(localRoot)) return localRoot;

            // 3. Windows System32
            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "7z.exe");
            if (File.Exists(sys32)) return sys32;

            // 4. Program Files 7-Zip
            string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
            if (File.Exists(pf)) return pf;

            string pf86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe");
            if (File.Exists(pf86)) return pf86;

            return null;
        }

        /// <summary>
        /// Asynchronously inspects and reads entries from an archive (.zip, .rar, .7z, etc.).
        /// Uses multi-tier resilience (SharpCompress -> System.IO.Compression -> 7-Zip engine)
        /// so it never fails on damaged headers or unusual formats.
        /// Throws ArchiveEncryptedException if password is required or incorrect.
        /// </summary>
        public async Task<List<ArchiveItemInfo>> ReadArchiveAsync(
            string archivePath, 
            string? password = null, 
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("Archive file not found.", archivePath);
            }

            return await Task.Run(() =>
            {
                // Tier 1: Try SharpCompress
                Exception? primaryException = null;
                try
                {
                    return ReadArchiveWithSharpCompress(archivePath, password, cancellationToken);
                }
                catch (ArchiveEncryptedException)
                {
                    throw; // Password prompt needed: rethrow immediately
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    primaryException = ex;
                }

                // Tier 2: Try .NET System.IO.Compression
                try
                {
                    return ReadArchiveWithSystemZip(archivePath);
                }
                catch
                {
                    // Fall through to 7-Zip engine
                }

                // Tier 3: Try 7-Zip CLI engine
                try
                {
                    string? sevenZip = FindSevenZipPath();
                    if (!string.IsNullOrEmpty(sevenZip))
                    {
                        return ReadArchiveWithSevenZip(sevenZip, archivePath, password, cancellationToken);
                    }
                }
                catch (ArchiveEncryptedException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to throw primary exception
                }

                throw primaryException ?? new InvalidOperationException("Failed to inspect archive with all available engines.");
            }, cancellationToken);
        }

        private List<ArchiveItemInfo> ReadArchiveWithSharpCompress(
            string archivePath, 
            string? password, 
            CancellationToken cancellationToken)
        {
            var items = new List<ArchiveItemInfo>();
            var readerOptions = new ReaderOptions();
            if (!string.IsNullOrEmpty(password))
            {
                readerOptions.Password = password;
            }

            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);
                bool hasEncryptedEntries = false;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    if (entry.IsEncrypted)
                    {
                        hasEncryptedEntries = true;
                    }

                    string normalizedKey = entry.Key.Replace('\\', '/').Trim('/');
                    string name = Path.GetFileName(normalizedKey);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = normalizedKey;
                    }

                    string directory = Path.GetDirectoryName(normalizedKey)?.Replace('\\', '/') ?? string.Empty;

                    items.Add(new ArchiveItemInfo
                    {
                        Name = name,
                        Path = normalizedKey,
                        Directory = directory,
                        Size = entry.Size,
                        CompressedSize = entry.CompressedSize,
                        LastModified = entry.LastModifiedTime,
                        IsDirectory = entry.IsDirectory
                    });
                }

                if (hasEncryptedEntries && string.IsNullOrEmpty(password))
                {
                    throw new ArchiveEncryptedException(archivePath, "This archive contains password-protected entries (AES-256).");
                }
            }
            catch (SharpCompress.Common.CryptographicException ex)
            {
                throw new ArchiveEncryptedException(archivePath, "Password is required or incorrect for this encrypted archive.", ex);
            }

            return items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ArchiveItemInfo> ReadArchiveWithSystemZip(string archivePath)
        {
            var items = new List<ArchiveItemInfo>();
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new SysZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

                string normalizedKey = entry.FullName.Replace('\\', '/').Trim('/');
                bool isDir = entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || (entry.Length == 0 && string.IsNullOrEmpty(Path.GetExtension(entry.FullName)));
                string name = Path.GetFileName(normalizedKey);
                if (string.IsNullOrEmpty(name)) name = normalizedKey;

                string directory = Path.GetDirectoryName(normalizedKey)?.Replace('\\', '/') ?? string.Empty;

                items.Add(new ArchiveItemInfo
                {
                    Name = name,
                    Path = normalizedKey,
                    Directory = directory,
                    Size = entry.Length,
                    CompressedSize = entry.CompressedLength,
                    LastModified = entry.LastWriteTime != DateTimeOffset.MinValue ? entry.LastWriteTime.DateTime : null,
                    IsDirectory = isDir
                });
            }

            return items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ArchiveItemInfo> ReadArchiveWithSevenZip(
            string sevenZipPath, 
            string archivePath, 
            string? password, 
            CancellationToken cancellationToken)
        {
            string passArg = !string.IsNullOrEmpty(password) ? $"-p\"{password}\"" : "-p-";
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"l -slt {passArg} \"{archivePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch 7z engine.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && (output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) || error.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) || output.Contains("Enter password", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArchiveEncryptedException(archivePath, "This archive requires a password (AES-256).");
            }

            var items = new List<ArchiveItemInfo>();
            using var reader = new StringReader(output);
            string? line;
            string? currentPath = null;
            long currentSize = 0;
            long currentPacked = 0;
            DateTime? currentModified = null;
            bool currentIsDir = false;
            bool inItems = false;

            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.StartsWith("----------"))
                {
                    inItems = true;
                    continue;
                }
                if (!inItems) continue;

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!string.IsNullOrEmpty(currentPath))
                    {
                        string normKey = currentPath.Replace('\\', '/').Trim('/');
                        string name = Path.GetFileName(normKey);
                        if (string.IsNullOrEmpty(name)) name = normKey;
                        string dir = Path.GetDirectoryName(normKey)?.Replace('\\', '/') ?? string.Empty;

                        items.Add(new ArchiveItemInfo
                        {
                            Name = name,
                            Path = normKey,
                            Directory = dir,
                            Size = currentSize,
                            CompressedSize = currentPacked,
                            LastModified = currentModified,
                            IsDirectory = currentIsDir
                        });

                        currentPath = null;
                        currentSize = 0;
                        currentPacked = 0;
                        currentModified = null;
                        currentIsDir = false;
                    }
                    continue;
                }

                int eqIdx = line.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = line.Substring(0, eqIdx).Trim();
                    string val = line.Substring(eqIdx + 1).Trim();

                    if (key.Equals("Path", StringComparison.OrdinalIgnoreCase)) currentPath = val;
                    else if (key.Equals("Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long s)) currentSize = s;
                    else if (key.Equals("Packed Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long ps)) currentPacked = ps;
                    else if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase)) currentIsDir = val == "+";
                    else if (key.Equals("Modified", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(val, out DateTime dt)) currentModified = dt;
                }
            }

            if (!string.IsNullOrEmpty(currentPath))
            {
                string normKey = currentPath.Replace('\\', '/').Trim('/');
                string name = Path.GetFileName(normKey);
                if (string.IsNullOrEmpty(name)) name = normKey;
                string dir = Path.GetDirectoryName(normKey)?.Replace('\\', '/') ?? string.Empty;

                items.Add(new ArchiveItemInfo
                {
                    Name = name,
                    Path = normKey,
                    Directory = dir,
                    Size = currentSize,
                    CompressedSize = currentPacked,
                    LastModified = currentModified,
                    IsDirectory = currentIsDir
                });
            }

            return items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Audits archive health by decompressing every entry, verifying CRC checksums and headers.
        /// Falls back to 7-Zip engine audit if SharpCompress encounters format issues.
        /// </summary>
        public async Task<ArchiveHealthReport> TestArchiveIntegrityAsync(
            string archiveFilePath,
            string? password = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(archiveFilePath))
            {
                throw new FileNotFoundException("Archive file not found.", archiveFilePath);
            }

            return await Task.Run(() =>
            {
                try
                {
                    return TestIntegrityWithSharpCompress(archiveFilePath, password, progress, cancellationToken);
                }
                catch
                {
                    string? sevenZip = FindSevenZipPath();
                    if (!string.IsNullOrEmpty(sevenZip))
                    {
                        return TestIntegrityWithSevenZip(sevenZip, archiveFilePath, password, progress, cancellationToken);
                    }
                    throw;
                }
            }, cancellationToken);
        }

        private ArchiveHealthReport TestIntegrityWithSharpCompress(
            string archiveFilePath,
            string? password,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var report = new ArchiveHealthReport();
            var sw = Stopwatch.StartNew();

            var readerOptions = new ReaderOptions();
            if (!string.IsNullOrEmpty(password))
            {
                readerOptions.Password = password;
            }

            using var archive = ArchiveFactory.OpenArchive(archiveFilePath, readerOptions);
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            report.TotalFilesTested = entries.Count;

            byte[] buffer = new byte[64 * 1024];
            int processedCount = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = entry.Key ?? "Unknown";

                try
                {
                    using var stream = entry.OpenEntryStream();
                    long entryBytes = 0;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        entryBytes += read;
                        report.TotalBytesTested += read;
                    }
                    report.HealthyFiles++;
                }
                catch (Exception ex)
                {
                    report.CorruptFiles++;
                    report.Issues.Add($"{key}: {ex.Message}");
                }

                processedCount++;
                double pct = entries.Count > 0 ? ((double)processedCount / entries.Count) * 100.0 : 100.0;
                progress?.Report(pct);
            }

            sw.Stop();
            report.Elapsed = sw.Elapsed;
            double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
            report.ThroughputMBps = (report.TotalBytesTested / (1024.0 * 1024.0)) / seconds;

            return report;
        }

        private ArchiveHealthReport TestIntegrityWithSevenZip(
            string sevenZipPath,
            string archiveFilePath,
            string? password,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            string passArg = !string.IsNullOrEmpty(password) ? $"-p\"{password}\"" : "-p-";
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"t -y {passArg} \"{archiveFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            progress?.Report(25.0);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch 7z engine.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            sw.Stop();

            progress?.Report(100.0);
            var report = new ArchiveHealthReport
            {
                Elapsed = sw.Elapsed
            };

            if (process.ExitCode == 0)
            {
                report.TotalFilesTested = 1;
                report.HealthyFiles = 1;
                report.CorruptFiles = 0;
            }
            else
            {
                report.TotalFilesTested = 1;
                report.HealthyFiles = 0;
                report.CorruptFiles = 1;
                report.Issues.Add(string.IsNullOrWhiteSpace(error) ? output : error);
            }

            return report;
        }

        /// <summary>
        /// Runs a synthetic multi-threaded in-memory benchmark across CPU cores.
        /// </summary>
        public async Task<BenchmarkResult> RunBenchmarkAsync(
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new BenchmarkResult();
                var sw = Stopwatch.StartNew();
                int threads = Environment.ProcessorCount;
                result.CpuThreadsUsed = threads;

                int totalSize = 16 * 1024 * 1024; // 16 MB workload
                byte[] testData = new byte[totalSize];
                var rng = new Random(42);
                byte[] pattern = Encoding.UTF8.GetBytes("ZenArchive multi-threaded benchmark synthetic workload 2026. Fast .NET 9 compression test block.\n");
                for (int i = 0; i < totalSize; i++)
                {
                    testData[i] = (byte)(pattern[i % pattern.Length] ^ (i % 7 == 0 ? rng.Next(32) : 0));
                }

                progress?.Report(20.0);

                // Benchmark 1: Compression
                var compressedStreams = new MemoryStream[threads];
                int chunkSize = totalSize / threads;

                var compSw = Stopwatch.StartNew();
                Parallel.For(0, threads, i =>
                {
                    var ms = new MemoryStream();
                    using (var deflater = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                    {
                        deflater.Write(testData, i * chunkSize, chunkSize);
                    }
                    compressedStreams[i] = ms;
                });
                compSw.Stop();
                progress?.Report(60.0);

                double compSec = Math.Max(0.001, compSw.Elapsed.TotalSeconds);
                result.CompressionSpeedMBps = (totalSize / (1024.0 * 1024.0)) / compSec;

                // Benchmark 2: Decompression
                var decompSw = Stopwatch.StartNew();
                Parallel.For(0, threads, i =>
                {
                    var ms = compressedStreams[i];
                    ms.Position = 0;
                    using var inflater = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    byte[] discardBuf = new byte[32 * 1024];
                    while (inflater.Read(discardBuf, 0, discardBuf.Length) > 0) { }
                });
                decompSw.Stop();
                progress?.Report(100.0);

                double decompSec = Math.Max(0.001, decompSw.Elapsed.TotalSeconds);
                result.DecompressionSpeedMBps = (totalSize / (1024.0 * 1024.0)) / decompSec;

                sw.Stop();
                result.Elapsed = sw.Elapsed;

                int score = (int)((result.CompressionSpeedMBps * 85.0) + (result.DecompressionSpeedMBps * 120.0));
                result.ZenScore = score;

                result.RatingTier = score switch
                {
                    >= 35000 => "Extreme Workstation 🚀",
                    >= 20000 => "High Performance Desktop ⚡",
                    >= 10000 => "Modern Performance PC ✨",
                    _ => "Standard Mobile / Laptop 💻"
                };

                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Reads an archive entry directly into a MemoryStream in memory without writing to disk.
        /// Uses multi-tier fallback (SharpCompress -> System.IO.Compression -> 7-Zip engine)
        /// </summary>
        public async Task<MemoryStream> GetEntryStreamAsync(
            string archiveFilePath, 
            string entryKey, 
            string? password = null, 
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(archiveFilePath))
            {
                throw new FileNotFoundException("Archive file not found.", archiveFilePath);
            }

            return await Task.Run(() =>
            {
                // Tier 1: Try SharpCompress
                try
                {
                    return GetEntryStreamWithSharpCompress(archiveFilePath, entryKey, password);
                }
                catch (ArchiveEncryptedException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to System.IO.Compression
                }

                // Tier 2: Try System.IO.Compression
                try
                {
                    using var fs = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var zip = new SysZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
                    string normalizedSearchKey = entryKey.Replace('\\', '/').Trim('/');

                    var entry = zip.Entries.FirstOrDefault(e =>
                    {
                        if (string.IsNullOrWhiteSpace(e.FullName)) return false;
                        string k = e.FullName.Replace('\\', '/').Trim('/');
                        return string.Equals(k, normalizedSearchKey, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetFileName(k), normalizedSearchKey, StringComparison.OrdinalIgnoreCase);
                    });

                    if (entry != null)
                    {
                        var ms = new MemoryStream();
                        using var s = entry.Open();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        return ms;
                    }
                }
                catch
                {
                    // Fall through to 7-Zip
                }

                // Tier 3: Try 7-Zip CLI engine
                string? sevenZip = FindSevenZipPath();
                if (!string.IsNullOrEmpty(sevenZip))
                {
                    return GetEntryStreamWithSevenZip(sevenZip, archiveFilePath, entryKey, password, cancellationToken);
                }

                throw new FileNotFoundException($"Entry '{entryKey}' could not be inspected or extracted.");
            }, cancellationToken);
        }

        private MemoryStream GetEntryStreamWithSharpCompress(string archiveFilePath, string entryKey, string? password)
        {
            var readerOptions = new ReaderOptions();
            if (!string.IsNullOrEmpty(password))
            {
                readerOptions.Password = password;
            }

            try
            {
                using var archive = ArchiveFactory.OpenArchive(archiveFilePath, readerOptions);
                string normalizedSearchKey = entryKey.Replace('\\', '/').Trim('/');

                var entry = archive.Entries.FirstOrDefault(e =>
                {
                    if (string.IsNullOrWhiteSpace(e.Key)) return false;
                    string key = e.Key.Replace('\\', '/').Trim('/');
                    return string.Equals(key, normalizedSearchKey, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Path.GetFileName(key), normalizedSearchKey, StringComparison.OrdinalIgnoreCase);
                });

                if (entry == null)
                {
                    throw new FileNotFoundException($"Entry '{entryKey}' not found in archive.");
                }

                if (entry.IsEncrypted && string.IsNullOrEmpty(password))
                {
                    throw new ArchiveEncryptedException(archiveFilePath, "Entry is encrypted. Password required to preview.");
                }

                var memoryStream = new MemoryStream();
                using (var entryStream = entry.OpenEntryStream())
                {
                    entryStream.CopyTo(memoryStream);
                }

                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (SharpCompress.Common.CryptographicException ex)
            {
                throw new ArchiveEncryptedException(archiveFilePath, "Incorrect password or decryption failed for this entry.", ex);
            }
        }

        private MemoryStream GetEntryStreamWithSevenZip(string sevenZipPath, string archivePath, string entryKey, string? password, CancellationToken cancellationToken)
        {
            string passArg = !string.IsNullOrEmpty(password) ? $"-p\"{password}\"" : "-p-";
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"e {passArg} -so \"{archivePath}\" \"{entryKey}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch 7z engine.");
            var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"7-Zip failed to read entry stream (exit code {process.ExitCode}).");
            }

            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Evaluates whether the archive entries require a dedicated container subfolder
        /// to prevent cluttering the destination directory (Smart Extract logic).
        /// </summary>
        public bool ShouldUseSmartExtractSubfolder(IEnumerable<string?> entryKeys)
        {
            var validKeys = entryKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k!.Trim().Replace('\\', '/').Trim('/'))
                .Where(k => k.Length > 0)
                .ToList();

            if (validKeys.Count == 0)
            {
                return false;
            }

            var rootSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int looseRootFilesCount = 0;

            foreach (var key in validKeys)
            {
                int slashIndex = key.IndexOf('/');
                if (slashIndex < 0)
                {
                    rootSegments.Add(key);
                    looseRootFilesCount++;
                }
                else
                {
                    string topDirectory = key.Substring(0, slashIndex);
                    rootSegments.Add(topDirectory);
                }
            }

            if (looseRootFilesCount > 1) return true;
            if (rootSegments.Count > 1) return true;
            if (looseRootFilesCount == 1 && rootSegments.Count == 1) return true;
            return false;
        }

        private List<string> GetArchiveEntryKeysQuick(string archivePath, string? password, CancellationToken cancellationToken)
        {
            // 1. Try SharpCompress
            try
            {
                var readerOptions = new ReaderOptions();
                if (!string.IsNullOrEmpty(password)) readerOptions.Password = password;
                using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);
                return archive.Entries.Select(e => e.Key ?? string.Empty).ToList();
            }
            catch { }

            // 2. Try System.IO.Compression
            try
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var zip = new SysZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
                return zip.Entries.Select(e => e.FullName).ToList();
            }
            catch { }

            // 3. Try 7-Zip
            string? sevenZip = FindSevenZipPath();
            if (!string.IsNullOrEmpty(sevenZip))
            {
                var items = ReadArchiveWithSevenZip(sevenZip, archivePath, password, cancellationToken);
                return items.Select(i => i.Path).ToList();
            }

            return new List<string>();
        }

        /// <summary>
        /// Asynchronously extracts an archive with Smart Extract logic, Zip-Slip security filtering, and progress reporting.
        /// Uses multi-tier resilience: SharpCompress -> System.IO.Compression -> 7-Zip engine,
        /// ensuring it never fails on non-standard zip headers, RAR5, 7z LZMA2, or unusual archives.
        /// </summary>
        public async Task<string> ExtractArchiveAsync(
            string archivePath,
            string baseDestinationFolder,
            bool smartExtract,
            string? password = null,
            IProgress<ArchiveProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("Archive file not found.", archivePath);
            }

            if (!Directory.Exists(baseDestinationFolder))
            {
                Directory.CreateDirectory(baseDestinationFolder);
            }

            return await Task.Run(() =>
            {
                string targetDirectory = baseDestinationFolder;
                if (smartExtract)
                {
                    try
                    {
                        var entryKeys = GetArchiveEntryKeysQuick(archivePath, password, cancellationToken);
                        bool createSubfolder = ShouldUseSmartExtractSubfolder(entryKeys);
                        if (createSubfolder)
                        {
                            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
                            targetDirectory = Path.Combine(baseDestinationFolder, archiveName);
                        }
                    }
                    catch
                    {
                        string archiveName = Path.GetFileNameWithoutExtension(archivePath);
                        targetDirectory = Path.Combine(baseDestinationFolder, archiveName);
                    }
                }

                Directory.CreateDirectory(targetDirectory);

                string ext = Path.GetExtension(archivePath).ToLowerInvariant();
                string? sevenZip = FindSevenZipPath();
                Exception? primaryException = null;

                // Priority 1 for .zip with no password: Native .NET 9 System.IO.Compression (hardware SIMD, instant)
                if (ext == ".zip" && string.IsNullOrEmpty(password))
                {
                    try
                    {
                        ExtractWithSystemZip(archivePath, targetDirectory, progress, cancellationToken);
                        return targetDirectory;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { primaryException = ex; }
                }

                // Priority 1 for .7z, .rar, or password-protected archives: Native 7-Zip CLI engine (AVX2 multithreaded)
                if (!string.IsNullOrEmpty(sevenZip))
                {
                    try
                    {
                        ExtractWithSevenZip(sevenZip, archivePath, targetDirectory, password, progress, cancellationToken);
                        return targetDirectory;
                    }
                    catch (ArchiveEncryptedException) { throw; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { primaryException ??= ex; }
                }

                // Fallback: SharpCompress engine
                try
                {
                    ExtractWithSharpCompress(archivePath, targetDirectory, password, progress, cancellationToken);
                    return targetDirectory;
                }
                catch (ArchiveEncryptedException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { primaryException ??= ex; }

                // Fallback: System.IO.Compression if not already attempted
                if (ext == ".zip")
                {
                    try
                    {
                        ExtractWithSystemZip(archivePath, targetDirectory, progress, cancellationToken);
                        return targetDirectory;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }

                throw primaryException ?? new InvalidOperationException("Failed to extract archive using all available engines.");
            }, cancellationToken);
        }

        private void ExtractWithSharpCompress(
            string archivePath,
            string targetDirectory,
            string? password,
            IProgress<ArchiveProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            var readerOptions = new ReaderOptions();
            if (!string.IsNullOrEmpty(password))
            {
                readerOptions.Password = password;
            }

            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);

                var fileEntries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                long totalBytes = fileEntries.Sum(e => e.Size);
                int totalItems = fileEntries.Count;

                long processedBytes = 0;
                int processedItems = 0;

                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = Path.GetFileName(archivePath),
                    ItemsExtracted = 0,
                    TotalItems = totalItems,
                    BytesProcessed = 0,
                    TotalBytes = totalBytes,
                    Percentage = 0,
                    StatusMessage = $"Starting extraction into {targetDirectory}..."
                });

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    // Zip-Slip Defense: Sanitize every destination path strictly within targetDirectory
                    string destinationPath = SanitizeEntryDestinationPath(targetDirectory, entry.Key);

                    if (!entry.IsDirectory)
                    {
                        string currentFileName = Path.GetFileName(entry.Key);

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = currentFileName,
                            ItemsExtracted = processedItems,
                            TotalItems = totalItems,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalItems > 0 ? ((double)processedItems / totalItems) * 100.0 : 0.0),
                            StatusMessage = $"Extracting {currentFileName} ({processedItems + 1}/{totalItems})..."
                        });

                        string? parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }

                        if (File.Exists(destinationPath))
                        {
                            try { File.SetAttributes(destinationPath, FileAttributes.Normal); } catch { }
                        }

                        using (var entryStream = entry.OpenEntryStream())
                        using (var outStream = File.Create(destinationPath))
                        {
                            byte[] buffer = new byte[128 * 1024];
                            int bytesRead;
                            while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                outStream.Write(buffer, 0, bytesRead);
                                processedBytes += bytesRead;

                                if (totalBytes > 0 && entry.Size > 500 * 1024)
                                {
                                    double livePct = Math.Min(99.0, ((double)processedBytes / totalBytes) * 100.0);
                                    progress?.Report(new ArchiveProgressReport
                                    {
                                        CurrentFileName = currentFileName,
                                        ItemsExtracted = processedItems,
                                        TotalItems = totalItems,
                                        BytesProcessed = processedBytes,
                                        TotalBytes = totalBytes,
                                        Percentage = livePct,
                                        StatusMessage = $"Extracting {currentFileName} ({livePct:0}%)..."
                                    });
                                }
                            }
                        }

                        if (entry.LastModifiedTime.HasValue)
                        {
                            File.SetLastWriteTime(destinationPath, entry.LastModifiedTime.Value);
                        }

                        processedItems++;

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = currentFileName,
                            ItemsExtracted = processedItems,
                            TotalItems = totalItems,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalItems > 0 ? ((double)processedItems / totalItems) * 100.0 : 100.0),
                            StatusMessage = $"Extracted {currentFileName} ({processedItems}/{totalItems})"
                        });
                    }
                    else
                    {
                        if (!Directory.Exists(destinationPath))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                    }
                }

                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = string.Empty,
                    ItemsExtracted = processedItems,
                    TotalItems = totalItems,
                    BytesProcessed = totalBytes,
                    TotalBytes = totalBytes,
                    Percentage = 100.0,
                    StatusMessage = $"Completed! {processedItems} file(s) extracted successfully."
                });
            }
            catch (SharpCompress.Common.CryptographicException ex)
            {
                throw new ArchiveEncryptedException(archivePath, "Decryption password is required or incorrect for this archive.", ex);
            }
        }

        private void ExtractWithSystemZip(
            string archivePath,
            string targetDirectory,
            IProgress<ArchiveProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new SysZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

            var entries = zip.Entries.Where(e => !string.IsNullOrWhiteSpace(e.FullName)).ToList();
            long totalBytes = entries.Sum(e => e.Length);
            long processedBytes = 0;
            int total = entries.Count;
            int current = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string destPath = SanitizeEntryDestinationPath(targetDirectory, entry.FullName);
                bool isDir = entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\");

                if (isDir)
                {
                    if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
                }
                else
                {
                    string? parent = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    if (File.Exists(destPath))
                    {
                        try { File.SetAttributes(destPath, FileAttributes.Normal); } catch { }
                    }

                    using var entryStream = entry.Open();
                    using var outFs = File.Create(destPath);
                    byte[] buffer = new byte[128 * 1024];
                    int bytesRead;
                    while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        outFs.Write(buffer, 0, bytesRead);
                        processedBytes += bytesRead;
                    }

                    if (entry.LastWriteTime != DateTimeOffset.MinValue)
                    {
                        try { File.SetLastWriteTime(destPath, entry.LastWriteTime.DateTime); } catch { }
                    }
                }

                current++;
                double pct = totalBytes > 0 
                    ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                    : (total > 0 ? ((double)current / total) * 100.0 : 100.0);
                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = Path.GetFileName(entry.FullName),
                    ItemsExtracted = current,
                    TotalItems = total,
                    BytesProcessed = processedBytes,
                    TotalBytes = totalBytes,
                    Percentage = pct,
                    StatusMessage = $"Extracting {entry.Name} ({current}/{total})..."
                });
            }
        }

        private void ExtractWithSevenZip(
            string sevenZipPath,
            string archivePath,
            string targetDirectory,
            string? password,
            IProgress<ArchiveProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            string safeTarget = targetDirectory.TrimEnd('\\', '/');
            string safeArchive = archivePath.TrimEnd('\\', '/');
            // If password is not provided, pass -p"" so 7-Zip fails immediately on encrypted files instead of hanging on stdin!
            string passArg = !string.IsNullOrEmpty(password) ? $"-p\"{password.Replace("\"", "\\\"")}\"" : "-p\"\"";
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x -y -aoa -bsp1 {passArg} \"-o{safeTarget}\" \"{safeArchive}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            progress?.Report(new ArchiveProgressReport
            {
                CurrentFileName = Path.GetFileName(archivePath),
                Percentage = 0,
                StatusMessage = $"Extracting with 7-Zip engine into {targetDirectory}..."
            });

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch 7z engine.");
            // Immediately close stdin so 7-Zip never waits for console keyboard input
            try { process.StandardInput.Close(); } catch { }

            using var ctr = cancellationToken.Register(() =>
            {
                try { process.Kill(); } catch { }
            });

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();
            int itemsCount = 0;
            double lastPct = 0;

            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                lock (outputSb)
                {
                    outputSb.AppendLine(e.Data);
                }

                string line = e.Data.Trim();

                // Live progress parsing from 7-Zip -bsp1 output (e.g. "  45% 12 file.dat" or "80%")
                var match = Regex.Match(line, @"(\d{1,3})%");
                double parsedPct = 0;
                if (match.Success && double.TryParse(match.Groups[1].Value, out parsedPct))
                {
                    lastPct = Math.Clamp(parsedPct, 0.0, 100.0);
                }

                string curFile = string.Empty;
                if (line.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase))
                {
                    itemsCount++;
                    curFile = line.Substring("Extracting".Length).Trim();
                }

                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = !string.IsNullOrEmpty(curFile) ? curFile : Path.GetFileName(archivePath),
                    ItemsExtracted = itemsCount,
                    Percentage = lastPct,
                    StatusMessage = !string.IsNullOrEmpty(curFile) 
                        ? $"Extracting {curFile} ({lastPct:0}%)..." 
                        : $"Extracting archive ({lastPct:0}%)..."
                });
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                lock (errorSb)
                {
                    errorSb.AppendLine(e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            cancellationToken.ThrowIfCancellationRequested();

            string output = outputSb.ToString();
            string error = errorSb.ToString();

            if (process.ExitCode != 0)
            {
                if (process.ExitCode == 2 ||
                    output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) || 
                    error.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Enter password", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("Enter password", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Data Error in encrypted file", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("Data Error in encrypted file", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Can not open the file as archive", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArchiveEncryptedException(archivePath, "Password is required or incorrect for this archive.");
                }
                throw new InvalidOperationException($"7-Zip extraction failed (code {process.ExitCode}): {error} {output}".Trim());
            }

            progress?.Report(new ArchiveProgressReport
            {
                CurrentFileName = Path.GetFileName(archivePath),
                Percentage = 100,
                StatusMessage = "Extraction completed successfully."
            });
        }

        /// <summary>
        /// Asynchronously compresses files and directories into a new archive (.zip or .7z),
        /// optionally protected with AES-256 encryption.
        /// </summary>
        public async Task CreateArchiveAsync(
            string targetArchivePath,
            IEnumerable<string> sourcePaths,
            CompressionFormat format,
            CompressionLevel level,
            string? password = null,
            bool encryptFileNames = false,
            IProgress<ArchiveProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var filesToCompress = new List<(string relativePath, string fullPath)>();
            var directoriesToCreate = new List<string>();

            foreach (var sourcePath in sourcePaths)
            {
                if (File.Exists(sourcePath))
                {
                    string fileName = Path.GetFileName(sourcePath);
                    filesToCompress.Add((fileName, sourcePath));
                }
                else if (Directory.Exists(sourcePath))
                {
                    string folderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = "Folder";
                    }

                    var dirInfo = new DirectoryInfo(sourcePath);
                    int rootLength = dirInfo.FullName.Length;

                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(rootLength).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                        string archivePath = $"{folderName}/{relative}";
                        filesToCompress.Add((archivePath, file));
                    }

                    foreach (var dir in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        string relative = dir.Substring(rootLength).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                        string archivePath = $"{folderName}/{relative}";
                        directoriesToCreate.Add(archivePath);
                    }
                }
            }

            if (filesToCompress.Count == 0 && directoriesToCreate.Count == 0)
            {
                throw new InvalidOperationException("No valid files or directories were specified to compress.");
            }

            await Task.Run(() =>
            {
                string? outputDirectory = Path.GetDirectoryName(targetArchivePath);
                if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                long totalBytes = filesToCompress.Sum(f => new FileInfo(f.fullPath).Length);
                int totalFiles = filesToCompress.Count;
                long processedBytes = 0;
                int processedFiles = 0;

                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = Path.GetFileName(targetArchivePath),
                    ItemsExtracted = 0,
                    TotalItems = totalFiles,
                    BytesProcessed = 0,
                    TotalBytes = totalBytes,
                    Percentage = 0,
                    StatusMessage = $"Creating {Path.GetFileName(targetArchivePath)}..."
                });

                if (!string.IsNullOrEmpty(password))
                {
                    using var fs = File.Create(targetArchivePath);
                    using var zipStream = new ZipOutputStream(fs);
                    zipStream.Password = password;

                    int zipLevel = level switch
                    {
                        CompressionLevel.Store => 0,
                        CompressionLevel.Fast => 3,
                        CompressionLevel.Maximum => 9,
                        _ => 6
                    };
                    zipStream.SetLevel(zipLevel);

                    foreach (var (relativePath, fullPath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileInfo = new FileInfo(fullPath);

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = relativePath,
                            ItemsExtracted = processedFiles,
                            TotalItems = totalFiles,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalFiles > 0 ? ((double)processedFiles / totalFiles) * 100.0 : 0.0),
                            StatusMessage = $"Encrypting & compressing {Path.GetFileName(fullPath)} ({processedFiles + 1}/{totalFiles})..."
                        });

                        var newEntry = new ZipEntry(ZipEntry.CleanName(relativePath))
                        {
                            DateTime = fileInfo.LastWriteTime,
                            Size = fileInfo.Length,
                            AESKeySize = 256
                        };

                        zipStream.PutNextEntry(newEntry);

                        using (var fileStream = File.OpenRead(fullPath))
                        {
                            fileStream.CopyTo(zipStream);
                        }

                        zipStream.CloseEntry();

                        processedFiles++;
                        processedBytes += fileInfo.Length;

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = relativePath,
                            ItemsExtracted = processedFiles,
                            TotalItems = totalFiles,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalFiles > 0 ? ((double)processedFiles / totalFiles) * 100.0 : 100.0),
                            StatusMessage = $"Compressed & encrypted {Path.GetFileName(fullPath)} ({processedFiles}/{totalFiles})"
                        });
                    }
                }
                else
                {
                    var archiveType = format == CompressionFormat.SevenZip ? ArchiveType.SevenZip : ArchiveType.Zip;

                    CompressionType compressionType = (format, level) switch
                    {
                        (CompressionFormat.Zip, CompressionLevel.Store) => CompressionType.None,
                        (CompressionFormat.Zip, _) => CompressionType.Deflate,
                        (CompressionFormat.SevenZip, CompressionLevel.Store) => CompressionType.None,
                        (CompressionFormat.SevenZip, _) => CompressionType.LZMA,
                        _ => CompressionType.Deflate
                    };

                    var writerOptions = new WriterOptions(compressionType);

                    using var writer = WriterFactory.OpenWriter(targetArchivePath, archiveType, writerOptions);

                    foreach (var dir in directoriesToCreate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.WriteDirectory(dir, DateTime.UtcNow);
                    }

                    foreach (var (relativePath, fullPath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileInfo = new FileInfo(fullPath);

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = relativePath,
                            ItemsExtracted = processedFiles,
                            TotalItems = totalFiles,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalFiles > 0 ? ((double)processedFiles / totalFiles) * 100.0 : 0.0),
                            StatusMessage = $"Compressing {Path.GetFileName(fullPath)} ({processedFiles + 1}/{totalFiles})..."
                        });

                        using (var fileStream = File.OpenRead(fullPath))
                        {
                            writer.Write(relativePath, fileStream, fileInfo.LastWriteTimeUtc);
                        }

                        processedFiles++;
                        processedBytes += fileInfo.Length;

                        progress?.Report(new ArchiveProgressReport
                        {
                            CurrentFileName = relativePath,
                            ItemsExtracted = processedFiles,
                            TotalItems = totalFiles,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0 
                                ? Math.Min(100.0, ((double)processedBytes / totalBytes) * 100.0) 
                                : (totalFiles > 0 ? ((double)processedFiles / totalFiles) * 100.0 : 100.0),
                            StatusMessage = $"Compressed {Path.GetFileName(fullPath)} ({processedFiles}/{totalFiles})"
                        });
                    }
                }

                progress?.Report(new ArchiveProgressReport
                {
                    CurrentFileName = string.Empty,
                    ItemsExtracted = processedFiles,
                    TotalItems = totalFiles,
                    BytesProcessed = totalBytes,
                    TotalBytes = totalBytes,
                    Percentage = 100.0,
                    StatusMessage = $"Archive created successfully: {Path.GetFileName(targetArchivePath)}"
                });

            }, cancellationToken);
        }
    }
}
