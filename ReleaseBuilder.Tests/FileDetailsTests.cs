using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class FileDetailsTests
    {
        [Fact]
        public void GetArchiveName_NoRename()
        {
            var fd = new FileDetails(0, @"C:\project", @"C:\project\bin\app.exe", "");
            Assert.Equal(@"bin\app.exe", fd.GetArchiveName());
        }

        [Fact]
        public void GetArchiveName_WithRename()
        {
            var fd = new FileDetails(0, @"C:\project", @"C:\project\app.exe", "renamed.exe");
            Assert.Equal("renamed.exe", fd.GetArchiveName());
        }

        [Fact]
        public void GetArchiveName_TwoArgConstructor()
        {
            var fd = new FileDetails(@"C:\project", @"C:\project\output\file.txt");
            Assert.Equal(@"output\file.txt", fd.GetArchiveName());
        }

        [Fact]
        public void Equals_Same()
        {
            var a = new FileDetails(0, @"C:\root", @"C:\root\file.txt", "");
            var b = new FileDetails(0, @"C:\root", @"C:\root\file.txt", "");
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equals_Different()
        {
            var a = new FileDetails(0, @"C:\root", @"C:\root\file1.txt", "");
            var b = new FileDetails(0, @"C:\root", @"C:\root\file2.txt", "");
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Equals_Null()
        {
            var a = new FileDetails(0, @"C:\root", @"C:\root\file.txt", "");
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void GetHashCode_Consistent()
        {
            var a = new FileDetails(0, @"C:\root", @"C:\root\file.txt", "");
            var b = new FileDetails(0, @"C:\root", @"C:\root\file.txt", "");
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void SkipDirectories()
        {
            var fd = new FileDetails(1, @"C:\root", @"C:\root\sub\deep\file.txt", "");
            // With skip=1, the archive name should strip the first directory level
            var name = fd.GetArchiveName();
            Assert.DoesNotContain("root", name.ToLower());
        }
    }
}
