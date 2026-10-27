using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// The if-different attribute of the copy action: an existing destination that already holds the
    /// bytes the copy would write is left alone, so its timestamp survives.
    /// </summary>
    public class CopyIfDifferentTests : IDisposable
    {
        private static readonly DateTime Old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _tempDir;

        public CopyIfDifferentTests()
        {
            RLog.ResetErrorCount();
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_copydiff_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private string MakeFile(string content, params string[] parts)
        {
            var p = Path.Combine(new[] { _tempDir }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
            return p;
        }

        /// <summary>A destination file with the given content and an old timestamp.</summary>
        private string MakeOldDest(string content, string name = "a.txt")
        {
            var p = MakeFile(content, "staging", "dest", name);
            File.SetLastWriteTimeUtc(p, Old);
            return p;
        }

        private void Copy(string copyAttributes, string children = "")
        {
            var configFile = new FileInfo(Path.Combine(_tempDir, "ReleaseConfig.xml"));
            File.WriteAllText(configFile.FullName, $"""
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Target name="live" type="folder" path="." />
                  <Artefacts folder="staging">
                    <build>
                      <copy from="src" to="dest" match="*.txt" {copyAttributes}>{children}</copy>
                    </build>
                  </Artefacts>
                </ReleaseConfig>
                """);
            new ReleaseBuilder(
                new DirectoryInfo(_tempDir), configFile, "live",
                Enumerable.Empty<DirectoryInfo>(),
                nobuild: false, useShellExecute: false, dryRun: false).Build();
        }

        [Fact]
        public void Identical_destination_keeps_its_timestamp()
        {
            MakeFile("same", "staging", "src", "a.txt");
            var dest = MakeOldDest("same");

            Copy("""if-different="true" """);

            Assert.Equal(Old, File.GetLastWriteTimeUtc(dest));
            Assert.Equal("same", File.ReadAllText(dest));
        }

        [Fact]
        public void Different_destination_is_replaced()
        {
            MakeFile("new", "staging", "src", "a.txt");
            var dest = MakeOldDest("old");

            Copy("""if-different="true" """);

            Assert.Equal("new", File.ReadAllText(dest));
            Assert.NotEqual(Old, File.GetLastWriteTimeUtc(dest));
        }

        [Fact]
        public void Missing_destination_is_created()
        {
            MakeFile("new", "staging", "src", "a.txt");

            Copy("""if-different="true" """);

            Assert.Equal("new", File.ReadAllText(Path.Combine(_tempDir, "staging", "dest", "a.txt")));
        }

        [Fact]
        public void Default_still_rewrites_an_identical_destination()
        {
            MakeFile("same", "staging", "src", "a.txt");
            var dest = MakeOldDest("same");

            Copy("");

            Assert.NotEqual(Old, File.GetLastWriteTimeUtc(dest));
        }

        [Fact]
        public void Only_the_changed_file_of_several_is_replaced()
        {
            MakeFile("same", "staging", "src", "a.txt");
            MakeFile("new", "staging", "src", "b.txt");
            var a = MakeOldDest("same", "a.txt");
            var b = MakeOldDest("old", "b.txt");

            Copy("""if-different="true" """);

            Assert.Equal(Old, File.GetLastWriteTimeUtc(a));
            Assert.Equal("new", File.ReadAllText(b));
        }

        [Fact]
        public void Transformed_content_is_compared_not_the_source()
        {
            MakeFile("v={{V}}", "staging", "src", "a.txt");
            var dest = MakeOldDest("v=1.2");

            Copy("""if-different="true" """, """<transform-content transform="replace,{{V}},1.2" />""");

            Assert.Equal(Old, File.GetLastWriteTimeUtc(dest));
            Assert.Equal("v=1.2", File.ReadAllText(dest));
        }

        [Fact]
        public void No_temporary_file_is_left_behind()
        {
            MakeFile("new", "staging", "src", "a.txt");
            MakeOldDest("old");
            MakeFile("same", "staging", "src", "b.txt");
            MakeOldDest("same", "b.txt");

            Copy("""if-different="true" """);

            Assert.Empty(Directory.EnumerateFiles(Path.Combine(_tempDir, "staging", "dest"), "*.rb-new"));
        }
    }
}
