using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class PathFinderTests
    {
        // --- NormalizeSeparators ---

        [Fact]
        public void NormalizeSeparators_Backslashes_Converted()
        {
            var wrongSep = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
            var result = PathFinder.NormalizeSeparators(@"publish\release\win-x64");
            Assert.DoesNotContain(wrongSep.ToString(), result);
        }

        [Fact]
        public void NormalizeSeparators_ForwardSlashes_Converted()
        {
            var wrongSep = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
            var result = PathFinder.NormalizeSeparators("publish/release/linux-x64");
            Assert.DoesNotContain(wrongSep.ToString(), result);
        }

        [Fact]
        public void NormalizeSeparators_Mixed_Converted()
        {
            var wrongSep = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
            var result = PathFinder.NormalizeSeparators(@"publish/release\osx-arm64");
            Assert.DoesNotContain(wrongSep.ToString(), result);
        }

        [Fact]
        public void NormalizeSeparators_UsesplatformSeparator()
        {
            var result = PathFinder.NormalizeSeparators(@"a\b/c");
            var expected = string.Join(Path.DirectorySeparatorChar.ToString(), new[] { "a", "b", "c" });
            Assert.Equal(expected, result);
        }

        // --- FindDirectory required ---

        [Fact]
        public void FindDirectory_NotFound_Required_Throws()
        {
            var paths = new[] { new System.Collections.Generic.List<string> { Path.GetTempPath() } };
            Assert.Throws<Exception>(() =>
                PathFinder.FindDirectory("__nonexistent_dir_xyzzy__", paths, required: true));
        }

        [Fact]
        public void FindDirectory_NotFound_NotRequired_ReturnsNull()
        {
            var paths = new[] { new System.Collections.Generic.List<string> { Path.GetTempPath() } };
            var result = PathFinder.FindDirectory("__nonexistent_dir_xyzzy__", paths, required: false);
            Assert.Null(result);
        }

        [Fact]
        public void FindDirectory_Found_ReturnsPath()
        {
            var tempDir = Path.GetTempPath();
            var subDir = Path.Combine(tempDir, "pathfinder_test_dir");
            Directory.CreateDirectory(subDir);
            try
            {
                var paths = new[] { new System.Collections.Generic.List<string> { tempDir } };
                var result = PathFinder.FindDirectory("pathfinder_test_dir", paths);
                Assert.NotNull(result);
                Assert.True(Directory.Exists(result));
            }
            finally
            {
                Directory.Delete(subDir);
            }
        }

        [Fact]
        public void FindDirectory_NormalizesInputSeparators()
        {
            var tempDir = Path.GetTempPath();
            var subDir = Path.Combine(tempDir, "sep_test");
            Directory.CreateDirectory(subDir);
            try
            {
                var paths = new[] { new System.Collections.Generic.List<string> { tempDir } };
                // Pass with wrong separator — should still find it
                var wrongSepPath = "sep_test".Replace('/', '\\');
                var result = PathFinder.FindDirectory(wrongSepPath, paths, required: false);
                Assert.NotNull(result);
            }
            finally
            {
                Directory.Delete(subDir);
            }
        }
    }
}
