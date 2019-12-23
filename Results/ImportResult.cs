namespace SBA.Durable
{
    public class ImportResult
    {
        public string FileName { get; set; }

        public int ImportedRowCount { get; set; }

        public int TotalRowCount { get; set; }

        public string Messages { get; set; }
    }
}