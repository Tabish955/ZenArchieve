using System;

namespace Archieve_App
{
    public class BenchmarkResult
    {
        public double CompressionSpeedMBps { get; set; }
        public double DecompressionSpeedMBps { get; set; }
        public int CpuThreadsUsed { get; set; }
        public int ZenScore { get; set; }
        public string RatingTier { get; set; } = string.Empty;
        public TimeSpan Elapsed { get; set; }

        public string FormattedCompression => $"{CompressionSpeedMBps:0.1} MB/s";
        public string FormattedDecompression => $"{DecompressionSpeedMBps:0.1} MB/s";
    }
}
