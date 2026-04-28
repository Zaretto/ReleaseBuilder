using System.Runtime.InteropServices;
using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class PlatformVariableTests
    {
        [Fact]
        public void OS_is_one_of_known_platforms()
        {
            var (os, _, _) = ReleaseBuilder.GetPlatformInfo();
            Assert.Contains(os, new[] { "windows", "osx", "linux" });
        }

        [Fact]
        public void OS_matches_RuntimeInformation()
        {
            var (os, _, _) = ReleaseBuilder.GetPlatformInfo();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Equal("windows", os);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Assert.Equal("osx", os);
            else
                Assert.Equal("linux", os);
        }

        [Fact]
        public void ARCH_is_non_empty_lowercase()
        {
            var (_, arch, _) = ReleaseBuilder.GetPlatformInfo();
            Assert.NotEmpty(arch);
            Assert.Equal(arch.ToLowerInvariant(), arch);
        }

        [Fact]
        public void RUNTIME_is_os_prefix_dash_arch()
        {
            var (os, arch, runtime) = ReleaseBuilder.GetPlatformInfo();
            var expectedPrefix = os == "windows" ? "win" : os;
            Assert.Equal($"{expectedPrefix}-{arch}", runtime);
        }

        [Fact]
        public void RUNTIME_uses_win_prefix_not_windows_on_Windows()
        {
            var (_, _, runtime) = ReleaseBuilder.GetPlatformInfo();
            Assert.DoesNotContain("windows-", runtime);
        }
    }
}
