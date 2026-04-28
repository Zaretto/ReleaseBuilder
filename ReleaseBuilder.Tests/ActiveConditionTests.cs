using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Verifies that a non-matching active= condition on a ReleaseBuilder element
    /// prevents the child config from being loaded or parsed.
    /// </summary>
    public class ActiveConditionTests : IDisposable
    {
        private readonly string _tempDir;

        public ActiveConditionTests()
        {
            RLog.ResetErrorCount();
            // Create temp dirs inside the test output directory so GitVersion can resolve
            // the git repository root and provide a valid version.
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_active_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void NonMatching_active_on_ReleaseBuilder_skips_child_even_when_child_config_is_malformed()
        {
            // Arrange: parent config references a child folder whose ReleaseConfig.xml
            // is intentionally malformed XML. The active condition uses an impossible
            // platform name so it can never match on any real OS.
            var brokenChildDir = Path.Combine(_tempDir, "broken-child");
            Directory.CreateDirectory(brokenChildDir);
            File.WriteAllText(
                Path.Combine(brokenChildDir, "ReleaseConfig.xml"),
                "THIS IS NOT VALID XML <<< intentionally broken"
            );

            var parentConfigPath = Path.Combine(_tempDir, "ReleaseConfig.xml");
            File.WriteAllText(parentConfigPath, $"""
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Target name="Release" type="folder" path="." />
                  <ReleaseBuilder folder="{brokenChildDir.Replace("\\", "/")}"
                                  process="true"
                                  active="when,~OS~,==,IMPOSSIBLE_PLATFORM" />
                </ReleaseConfig>
                """);

            var root = new DirectoryInfo(_tempDir);
            var configFile = new FileInfo(parentConfigPath);
            var rb = new ReleaseBuilder(root, configFile, "Release", Enumerable.Empty<DirectoryInfo>(),
                                        nobuild: false, useShellExecute: false, dryRun: true);

            // Act
            rb.Build();

            // Assert: the broken child was never parsed so no XML errors were logged
            Assert.Equal(0, RLog.ErrorCount);
        }
    }
}
