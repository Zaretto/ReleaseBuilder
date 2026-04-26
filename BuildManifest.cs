namespace ReleaseBuilder
{
    public class ManifestEntry
    {
        public string Path { get; set; } = "";
        public string ArchiveName { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public class BuildManifest
    {
        public string TargetName { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string Version { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public List<ManifestEntry> Artefacts { get; set; } = new();
        public int ErrorCount { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
