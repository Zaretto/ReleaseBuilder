using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class ArtefactTests
    {
        [Fact]
        public void FileConstructor_Type()
        {
            var fi = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var a = new Artefact(fi, "");
            Assert.Equal(Artefact.ArtefactType.File, a.Type);
        }

        [Fact]
        public void DirectoryConstructor_Type()
        {
            var di = new DirectoryInfo(Path.GetTempPath());
            var a = new Artefact(di);
            Assert.Equal(Artefact.ArtefactType.Path, a.Type);
        }

        [Fact]
        public void MatchConstructor_Type()
        {
            var a = new Artefact(0, @"C:\root", @"C:\root\bin\*.dll");
            Assert.Equal(Artefact.ArtefactType.Match, a.Type);
        }

        [Fact]
        public void MatchConstructor_NormalizesSlashes()
        {
            var a = new Artefact(0, @"C:\root", "C:/root/bin/*.dll");
            Assert.Contains(@"\", a.PathName);
            Assert.DoesNotContain("/", a.PathName);
        }

        [Fact]
        public void Equals_Same_File()
        {
            var fi = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var a = new Artefact(fi, "");
            var b = new Artefact(fi, "");
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equals_Different_Files()
        {
            var fi1 = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var di = new DirectoryInfo(Path.GetTempPath());
            var a = new Artefact(fi1, "");
            var b = new Artefact(di);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Equals_Null()
        {
            var fi = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var a = new Artefact(fi, "");
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void GetHashCode_Consistent()
        {
            var fi = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var a = new Artefact(fi, "");
            var b = new Artefact(fi, "");
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToString_IncludesType()
        {
            var fi = new FileInfo(typeof(ArtefactTests).Assembly.Location);
            var a = new Artefact(fi, "");
            Assert.Contains("File", a.ToString());
        }
    }
}
