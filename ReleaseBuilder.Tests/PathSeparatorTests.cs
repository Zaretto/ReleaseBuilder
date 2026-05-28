using System.Xml.Linq;
using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Cross-platform separator handling for every path-bearing construct that appears
    /// in real ReleaseConfig.xml files. Each config supplies Windows-style backslash
    /// separators; the test asserts they resolve to real nested paths, which only holds
    /// when separators are normalised (on Unix '\' is a legal filename char, not a
    /// separator, so an unconverted path silently becomes a single oddly-named entry).
    /// </summary>
    public class PathSeparatorTests : IDisposable
    {
        private readonly string _tempDir;

        public PathSeparatorTests()
        {
            RLog.ResetErrorCount();
            // Temp dir lives under the test output dir so GitVersion finds the repo root.
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_pathsep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string Xml(string body) =>
            $"<?xml version=\"1.0\"?>\n<ReleaseConfig>\n{body}\n</ReleaseConfig>";

        /// <summary>Absolute path under the temp root, expressed with forward slashes so a
        /// backslash suffix in a config value is the only platform-specific separator.</summary>
        private string Fwd(params string[] parts) =>
            Path.Combine(new[] { _tempDir }.Concat(parts).ToArray()).Replace('\\', '/');

        private string MakeDir(params string[] parts)
        {
            var p = Path.Combine(new[] { _tempDir }.Concat(parts).ToArray());
            Directory.CreateDirectory(p);
            return p;
        }

        private string MakeFile(string content, params string[] parts)
        {
            var p = Path.Combine(new[] { _tempDir }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
            return p;
        }

        private ReleaseBuilder Build(string body, string target = "live")
        {
            var configFile = new FileInfo(Path.Combine(_tempDir, "ReleaseConfig.xml"));
            File.WriteAllText(configFile.FullName, Xml(body));
            var rb = new ReleaseBuilder(
                new DirectoryInfo(_tempDir), configFile, target,
                Enumerable.Empty<DirectoryInfo>(),
                nobuild: false, useShellExecute: false, dryRun: false);
            rb.Build();
            return rb;
        }

        // ------------------------------------------------------------------
        // PublishTarget (Target path="...")
        // ------------------------------------------------------------------

        [Fact]
        public void PublishTarget_normalises_backslash_path()
        {
            var target = new PublishTarget("t", $"{Fwd()}/a\\b\\c");
            var expected = Path.Combine(_tempDir, "a", "b", "c");
            Assert.True(Directory.Exists(expected), $"expected {expected}");
        }

        [Fact]
        public void Target_folder_path_with_backslashes_resolves_to_nested_folder()
        {
            MakeFile("payload", "seed.txt");
            var rb = Build($"""
                  <Target name="live" type="folder" path="{Fwd("deploy")}\out" />
                  <Artefacts>
                    <file>seed.txt</file>
                  </Artefacts>
                """);
            rb.Process();

            var outDir = Path.Combine(_tempDir, "deploy", "out");
            Assert.True(Directory.Exists(outDir), $"expected target dir {outDir}");
            Assert.NotEmpty(Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories));
        }

        // ------------------------------------------------------------------
        // Artefacts folder="..."
        // ------------------------------------------------------------------

        [Fact]
        public void Artefacts_folder_with_backslashes_resolves()
        {
            MakeDir("pkg", "sub");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts folder="pkg\sub">
                    <folder>.</folder>
                  </Artefacts>
                """);
            Assert.Equal(0, RLog.ErrorCount);
        }

        // ------------------------------------------------------------------
        // copy from="..." to="..."
        // ------------------------------------------------------------------

        [Fact]
        public void Copy_from_and_to_with_backslashes_creates_nested_directories()
        {
            MakeDir("staging");
            MakeFile("payload", "staging", "input", "seed.txt");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts folder="staging">
                    <build>
                      <copy from="input\seed.txt" to="dest\nested" />
                    </build>
                  </Artefacts>
                """);

            var expected = Path.Combine(_tempDir, "staging", "dest", "nested", "seed.txt");
            Assert.True(File.Exists(expected), $"expected copied file at {expected}");
        }

        // ------------------------------------------------------------------
        // clean folder="..."
        // ------------------------------------------------------------------

        [Fact]
        public void Clean_folder_with_backslashes_removes_files()
        {
            MakeFile("junk", "build", "artifacts", "old.txt");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <clean folder="build\artifacts" match="*.txt" />
                    </build>
                  </Artefacts>
                """);

            Assert.False(File.Exists(Path.Combine(_tempDir, "build", "artifacts", "old.txt")));
        }

        // ------------------------------------------------------------------
        // create file="..."
        // ------------------------------------------------------------------

        [Fact]
        public void Create_file_with_backslashes_writes_to_nested_path()
        {
            MakeDir("gen", "sub");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="gen\sub\version.txt">v1</create>
                    </build>
                  </Artefacts>
                """);

            var expected = Path.Combine(_tempDir, "gen", "sub", "version.txt");
            Assert.True(File.Exists(expected), $"expected created file at {expected}");
            Assert.Equal("v1", File.ReadAllText(expected));
        }

        // ------------------------------------------------------------------
        // xml-edit file="..."
        // ------------------------------------------------------------------

        [Fact]
        public void XmlEdit_file_with_backslashes_edits_nested_file()
        {
            MakeFile("<root><Version>0</Version></root>", "conf", "app", "settings.xml");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <xml-edit file="conf\app\settings.xml" omit-declaration="true">
                        <node path="//Version" action="set,9" />
                      </xml-edit>
                    </build>
                  </Artefacts>
                """);

            var doc = XDocument.Load(Path.Combine(_tempDir, "conf", "app", "settings.xml"));
            Assert.Equal("9", doc.Root!.Element("Version")!.Value);
        }

        [Fact]
        public void XmlEdit_xpath_selector_with_backslash_is_not_normalised()
        {
            // The XPath predicate matches an attribute value that contains a literal
            // backslash. If the selector were separator-normalised the predicate would
            // become '@key=a/b', match nothing, and the value would stay "old".
            MakeFile("<root><item key=\"a\\b\">old</item></root>", "config.xml");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <xml-edit file="config.xml" omit-declaration="true">
                        <node path="//item[@key='a\b']" action="set,MATCHED" />
                      </xml-edit>
                    </build>
                  </Artefacts>
                """);

            var doc = XDocument.Load(Path.Combine(_tempDir, "config.xml"));
            Assert.Equal("MATCHED", doc.Root!.Element("item")!.Value);
        }

        // ------------------------------------------------------------------
        // Folder name="..." path="..." (glob that defines a variable)
        // ------------------------------------------------------------------

        [Fact]
        public void Folder_glob_path_with_backslashes_resolves()
        {
            MakeDir("publish", "app-1.2.3");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Folder name="APPDIR" path="publish\app-*" version="latest" />
                """);
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Folder_glob_defines_usable_directory_variable()
        {
            // <Folder> resolves a glob (with backslashes) to the matched directory and
            // exposes it as a variable. Referencing ~APPDIR~ as a path proves the value
            // is the real directory and that the variable name was not mangled.
            MakeFile("payload", "publish", "app-2.5.0", "lib.dll");
            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <Folder name="APPDIR" path="publish\app-*" version="latest" />
                  <Artefacts folder=".">
                    <build>
                      <copy from="~APPDIR~\lib.dll" to="out" />
                    </build>
                  </Artefacts>
                """);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.True(File.Exists(Path.Combine(_tempDir, "out", "lib.dll")));
        }

        // ------------------------------------------------------------------
        // <Artefact> (singular) directory="..."
        // ------------------------------------------------------------------

        [Fact]
        public void Artefact_directory_with_backslashes_collects_and_publishes()
        {
            MakeFile("payload", "stage", "lib", "x.dll");
            var rb = Build($"""
                  <Target name="live" type="folder" path="{Fwd("output")}" />
                  <Artefact directory="stage\lib" />
                """);
            rb.Process();

            var outDir = Path.Combine(_tempDir, "output");
            Assert.True(Directory.Exists(outDir), $"expected target dir {outDir}");
            Assert.Contains(
                Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories),
                f => Path.GetFileName(f) == "x.dll");
        }

        // ------------------------------------------------------------------
        // ReleaseBuilder folder="..." file="..." (child build)
        // ------------------------------------------------------------------

        [Fact]
        public void ReleaseBuilder_child_folder_and_file_with_backslashes_resolve()
        {
            var childDir = MakeDir("modules", "child");
            MakeFile("data", "modules", "child", "data.dat");
            File.WriteAllText(Path.Combine(childDir, "Child.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <file>data.dat</file>
                  </Artefacts>
                """));

            var rb = Build("""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="modules\child" file="Child.xml" process="false" />
                """);

            Assert.Equal(0, RLog.ErrorCount);
        }
    }
}
