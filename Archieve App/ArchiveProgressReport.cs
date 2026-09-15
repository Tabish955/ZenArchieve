namespace Archieve_App
{
    public class ArchiveProgressReport
    {
        public string CurrentFileName { get; set; } = string.Empty;
        public int ItemsExtracted { get; set; }
        public int TotalItems { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}
