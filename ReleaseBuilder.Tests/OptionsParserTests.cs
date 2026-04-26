using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class OptionsParserTests
    {
        [Fact]
        public void GetStringArgument_LongForm()
        {
            var parser = new OptionsParser(new[] { "--target", "live" });
            Assert.Equal("live", parser.GetStringArgument("target", 't'));
        }

        [Fact]
        public void GetStringArgument_ShortForm()
        {
            var parser = new OptionsParser(new[] { "-t", "live" });
            Assert.Equal("live", parser.GetStringArgument("target", 't'));
        }

        [Fact]
        public void GetStringArgument_NotPresent_Returns_Null()
        {
            var parser = new OptionsParser(new[] { "--other", "value" });
            Assert.Null(parser.GetStringArgument("target", 't'));
        }

        [Fact]
        public void GetStringArgument_Consumed_From_Args()
        {
            var parser = new OptionsParser(new[] { "--target", "live" });
            parser.GetStringArgument("target", 't');
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void GetSwitchArgument_LongForm()
        {
            var parser = new OptionsParser(new[] { "--verbose" });
            Assert.True(parser.GetSwitchArgument("verbose", 'v'));
        }

        [Fact]
        public void GetSwitchArgument_ShortForm()
        {
            var parser = new OptionsParser(new[] { "-v" });
            Assert.True(parser.GetSwitchArgument("verbose", 'v'));
        }

        [Fact]
        public void GetSwitchArgument_NotPresent()
        {
            var parser = new OptionsParser(Array.Empty<string>());
            Assert.False(parser.GetSwitchArgument("verbose", 'v'));
        }

        [Fact]
        public void GetSwitchArgument_Consumed()
        {
            var parser = new OptionsParser(new[] { "--verbose" });
            parser.GetSwitchArgument("verbose", 'v');
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void IsInvalid_Empty_Args()
        {
            var parser = new OptionsParser(Array.Empty<string>());
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void IsInvalid_Unconsumed_Args()
        {
            var parser = new OptionsParser(new[] { "--unknown" });
            Assert.True(parser.IsInvalid());
        }

        [Fact]
        public void Multiple_Arguments()
        {
            var parser = new OptionsParser(new[] { "--target", "live", "--verbose", "--root", "." });
            Assert.Equal("live", parser.GetStringArgument("target", 't'));
            Assert.True(parser.GetSwitchArgument("verbose", 'v'));
            // root needs a real directory for GetDirectoryArgument, test via string
            Assert.Equal(".", parser.GetStringArgument("root", 'r'));
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void Duplicate_Switch_Both_Consumed()
        {
            var parser = new OptionsParser(new[] { "-v", "-v" });
            Assert.True(parser.GetSwitchArgument("verbose", 'v'));
            Assert.True(parser.GetSwitchArgument("verbose", 'v'));
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void DryRun_LongForm()
        {
            var parser = new OptionsParser(new[] { "--dry-run" });
            Assert.True(parser.GetSwitchArgument("dry-run", 'd'));
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void DryRun_ShortForm()
        {
            var parser = new OptionsParser(new[] { "-d" });
            Assert.True(parser.GetSwitchArgument("dry-run", 'd'));
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void OutputManifest_LongForm()
        {
            var parser = new OptionsParser(new[] { "--output-manifest", "manifest.json" });
            Assert.Equal("manifest.json", parser.GetStringArgument("output-manifest", 'o'));
            Assert.False(parser.IsInvalid());
        }

        [Fact]
        public void OutputManifest_ShortForm()
        {
            var parser = new OptionsParser(new[] { "-o", "-" });
            Assert.Equal("-", parser.GetStringArgument("output-manifest", 'o'));
            Assert.False(parser.IsInvalid());
        }
    }
}
