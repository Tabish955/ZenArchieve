using System;
using System.Collections.Generic;

namespace Archieve_App
{
    public class ArchiveHealthReport
    {
        public int TotalFilesTested { get; set; }
        public int HealthyFiles { get; set; }
        public int CorruptFiles { get; set; }
        public long TotalBytesTested { get; set; }
        public TimeSpan Elapsed { get; set; }
        public double ThroughputMBps { get; set; }
        public bool IsHealthy => CorruptFiles == 0 && TotalFilesTested > 0;
        public List<string> Issues { get; set; } = new();

        public string FormattedSpeed => $"{ThroughputMBps:0.1} MB/s";
        public string FormattedBytes => ArchiveItemInfo.FormatBytes(TotalBytesTested);
    }
}
