using System;
using System.Collections.Generic;

namespace Archieve_App
{
    /// <summary>
    /// Represents the result of comparing two archive files.
    /// Categorizes entries as Added, Removed, Modified, or Identical.
    /// </summary>
    public class ArchiveComparisonResult
    {
        public string ArchiveA { get; set; } = string.Empty;
        public string ArchiveB { get; set; } = string.Empty;

        public List<ComparisonEntry> AddedInB { get; set; } = new();
        public List<ComparisonEntry> RemovedFromA { get; set; } = new();
        public List<ComparisonEntry> Modified { get; set; } = new();
        public List<ComparisonEntry> Identical { get; set; } = new();

        public int TotalDifferences => AddedInB.Count + RemovedFromA.Count + Modified.Count;
        public bool AreIdentical => TotalDifferences == 0;

        public string Summary =>
            $"Added: {AddedInB.Count} | Removed: {RemovedFromA.Count} | Modified: {Modified.Count} | Identical: {Identical.Count}";
    }

    public class ComparisonEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SizeA { get; set; }
        public long SizeB { get; set; }
        public DateTime? ModifiedA { get; set; }
        public DateTime? ModifiedB { get; set; }
        public string ChangeType { get; set; } = string.Empty; // "Added", "Removed", "Modified", "Identical"

        public string FormattedSizeA => ArchiveItemInfo.FormatBytes(SizeA);
        public string FormattedSizeB => ArchiveItemInfo.FormatBytes(SizeB);
        public string SizeDelta
        {
            get
            {
                long diff = SizeB - SizeA;
                if (diff == 0) return "—";
                string sign = diff > 0 ? "+" : "";
                return $"{sign}{ArchiveItemInfo.FormatBytes(Math.Abs(diff))}";
            }
        }
    }
}
