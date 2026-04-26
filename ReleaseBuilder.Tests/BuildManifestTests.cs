using System.Text.Json;
using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class BuildManifestTests
    {
        [Fact]
        public void Manifest_serialises_and_deserialises()
        {
            var manifest = new BuildManifest
            {
                TargetName = "MyApp",
                TargetType = "ZipFile",
                Version = "1.2.3",
                OutputPath = "/releases",
                ErrorCount = 0,
                Timestamp = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero),
                Artefacts =
                [
                    new ManifestEntry { Path = "/releases/MyApp-live-1.2.3.zip", ArchiveName = "MyApp-live-1.2.3.zip", SizeBytes = 98765 }
                ]
            };

            var json = JsonSerializer.Serialize(manifest);
            var roundTripped = JsonSerializer.Deserialize<BuildManifest>(json)!;

            Assert.Equal(manifest.TargetName, roundTripped.TargetName);
            Assert.Equal(manifest.TargetType, roundTripped.TargetType);
            Assert.Equal(manifest.Version, roundTripped.Version);
            Assert.Equal(manifest.OutputPath, roundTripped.OutputPath);
            Assert.Equal(manifest.ErrorCount, roundTripped.ErrorCount);
            Assert.Equal(manifest.Timestamp, roundTripped.Timestamp);
            Assert.Single(roundTripped.Artefacts);
            Assert.Equal("MyApp-live-1.2.3.zip", roundTripped.Artefacts[0].ArchiveName);
            Assert.Equal(98765L, roundTripped.Artefacts[0].SizeBytes);
        }

        [Fact]
        public void Manifest_empty_artefacts_serialises()
        {
            var manifest = new BuildManifest { TargetName = "Test" };
            var json = JsonSerializer.Serialize(manifest);
            var roundTripped = JsonSerializer.Deserialize<BuildManifest>(json)!;
            Assert.Empty(roundTripped.Artefacts);
        }

        [Fact]
        public void ManifestEntry_defaults_are_empty_strings()
        {
            var entry = new ManifestEntry();
            Assert.Equal("", entry.Path);
            Assert.Equal("", entry.ArchiveName);
            Assert.Equal(0L, entry.SizeBytes);
        }
    }
}
