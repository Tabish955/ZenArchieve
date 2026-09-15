using System;
using System.IO;
using Wpf.Ui.Controls;

namespace Archieve_App
{
    public class ArchiveItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public long Size { get; set; }
        public long CompressedSize { get; set; }
        public DateTime? LastModified { get; set; }
        public bool IsDirectory { get; set; }

        public string Extension => IsDirectory 
            ? "Folder" 
            : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

        public string FormattedSize => IsDirectory 
            ? "-" 
            : FormatBytes(Size);

        public string FormattedCompressedSize => IsDirectory 
            ? "-" 
            : FormatBytes(CompressedSize);

        public string CompressionRatio
        {
            get
            {
                if (IsDirectory || Size <= 0) return "-";
                if (CompressedSize <= 0) return "0%";
                double ratio = (1.0 - ((double)CompressedSize / Size)) * 100.0;
                if (ratio < 0) ratio = 0;
                return $"{ratio:0.#}%";
            }
        }

        public string FormattedModified => LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

        public bool HasDisguisedExtension => !IsDirectory && CheckDisguisedExtension(Name).isDisguised;

        public string DisguisedWarning => HasDisguisedExtension
            ? "⚠️ Suspicious Disguised Extension: Executable masquerades as a document."
            : string.Empty;

        public static (bool isDisguised, string warning) CheckDisguisedExtension(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return (false, string.Empty);
            var parts = name.Split('.');
            if (parts.Length < 3) return (false, string.Empty);

            string outerExt = "." + parts[^1].ToLowerInvariant();
            string innerExt = "." + parts[^2].ToLowerInvariant();

            bool isOuterExecutable = outerExt is ".exe" or ".scr" or ".bat" or ".cmd" or ".vbs" or ".js" or ".ps1" or ".msi" or ".jar" or ".hta" or ".pif" or ".com";
            bool isInnerDocumentOrMedia = innerExt is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".mp3" or ".mp4" or ".zip" or ".rar";

            bool isDisguised = isOuterExecutable && isInnerDocumentOrMedia;
            string warning = isDisguised ? "⚠️ Suspicious Disguised Extension: Executable masquerades as a document." : string.Empty;
            return (isDisguised, warning);
        }

        public SymbolRegular IconSymbol
        {
            get
            {
                if (IsDirectory) return SymbolRegular.Folder24;
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                return ext switch
                {
                    ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => SymbolRegular.FolderZip24,
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => SymbolRegular.Image24,
                    ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => SymbolRegular.MusicNote224,
                    ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => SymbolRegular.Video24,
                    ".pdf" => SymbolRegular.DocumentPdf24,
                    ".doc" or ".docx" or ".rtf" or ".txt" or ".md" => SymbolRegular.DocumentText24,
                    ".xls" or ".xlsx" or ".csv" => SymbolRegular.Table24,
                    ".ppt" or ".pptx" => SymbolRegular.SlideAdd24,
                    ".cs" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".xml" or ".py" or ".cpp" or ".c" or ".h" => SymbolRegular.Code24,
                    ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => SymbolRegular.AppGeneric24,
                    _ => SymbolRegular.Document24
                };
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int index = 0;
            double size = bytes;

            while (size >= 1024 && index < suffixes.Length - 1)
            {
                size /= 1024.0;
                index++;
            }

            return index == 0 ? $"{size:0} {suffixes[index]}" : $"{size:0.##} {suffixes[index]}";
        }
    }
}
