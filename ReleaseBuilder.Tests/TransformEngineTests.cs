using ReleaseBuilder;
using System.Xml.Linq;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class TransformEngineTests
    {
        private static (VariableStore vars, TransformEngine engine) Create(params (string key, string value)[] variables)
        {
            var vars = new VariableStore();
            foreach (var (key, value) in variables)
                vars.Set(key, value);
            return (vars, new TransformEngine(vars));
        }

        // --- Transform ---

        [Fact]
        public void Transform_Null_Returns_Input()
        {
            var (_, engine) = Create();
            Assert.Equal("hello", engine.Transform(null, "hello"));
        }

        [Fact]
        public void Transform_Empty_Returns_Input()
        {
            var (_, engine) = Create();
            Assert.Equal("hello", engine.Transform("", "hello"));
        }

        [Fact]
        public void Transform_Set()
        {
            var (_, engine) = Create();
            Assert.Equal("newval", engine.Transform("set,newval", "ignored"));
        }

        [Fact]
        public void Transform_Set_With_Variable()
        {
            var (_, engine) = Create(("VER", "1.2.3"));
            Assert.Equal("1.2.3", engine.Transform("set,~VER~", "ignored"));
        }

        [Fact]
        public void Transform_Replace()
        {
            var (_, engine) = Create();
            Assert.Equal("hello world", engine.Transform("replace,foo,world", "hello foo"));
        }

        [Fact]
        public void Transform_Replace_NoMatch()
        {
            var (_, engine) = Create();
            Assert.Equal("hello", engine.Transform("replace,xyz,abc", "hello"));
        }

        [Fact]
        public void Transform_Replace_SameStrings_Returns_Input()
        {
            var (_, engine) = Create();
            Assert.Equal("hello same", engine.Transform("replace,same,same", "hello same"));
        }

        [Fact]
        public void Transform_RegexReplace()
        {
            var (_, engine) = Create();
            Assert.Equal("v2.2", engine.Transform(@"regex-replace,\d+,2", "v1.0"));
        }

        [Fact]
        public void Transform_GetVersion()
        {
            var (_, engine) = Create();
            Assert.Equal("1.2.3", engine.Transform(@"getversion,path\to\MyApp_1.2.3", "ignored"));
        }

        [Fact]
        public void Transform_GetVersion_ForwardSlashes()
        {
            var (_, engine) = Create();
            Assert.Equal("1.2.3", engine.Transform("getversion,path/to/MyApp_1.2.3", "ignored"));
        }

        [Fact]
        public void Transform_GetVersion_With_Variable()
        {
            var (_, engine) = Create(("APPPATH", @"C:\builds\MyApp_4.5.6"));
            Assert.Equal("4.5.6", engine.Transform("getversion,~APPPATH~", "ignored"));
        }

        [Fact]
        public void Transform_GetVersion_With_Variable_ForwardSlashes()
        {
            var (_, engine) = Create(("APPPATH", "builds/MyApp_4.5.6"));
            Assert.Equal("4.5.6", engine.Transform("getversion,~APPPATH~", "ignored"));
        }

        // --- random-key ---

        [Fact]
        public void Transform_RandomKey_Default_Is_32_Alphanumeric()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key", "ignored");
            Assert.Equal(32, key.Length);
            Assert.All(key, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"unexpected character '{c}'"));
        }

        [Fact]
        public void Transform_RandomKey_Explicit_Length()
        {
            var (_, engine) = Create();
            Assert.Equal(64, engine.Transform("random-key,64", "ignored").Length);
        }

        [Fact]
        public void Transform_RandomKey_Length_From_Variable()
        {
            var (_, engine) = Create(("KEYLEN", "16"));
            Assert.Equal(16, engine.Transform("random-key,~KEYLEN~", "ignored").Length);
        }

        [Fact]
        public void Transform_RandomKey_Values_Differ()
        {
            var (_, engine) = Create();
            Assert.NotEqual(engine.Transform("random-key", "ignored"), engine.Transform("random-key", "ignored"));
        }

        [Fact]
        public void Transform_RandomKey_Hex_Charset()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key,40,hex", "ignored");
            Assert.Equal(40, key.Length);
            Assert.All(key, c => Assert.Contains(c, "0123456789abcdef"));
        }

        [Fact]
        public void Transform_RandomKey_Base64Url_Charset()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key,32,base64url", "ignored");
            Assert.All(key, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_', $"unexpected character '{c}'"));
        }

        [Fact]
        public void Transform_RandomKey_Ascii_Charset_Is_Printable()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key,50,ascii", "ignored");
            Assert.Equal(50, key.Length);
            Assert.All(key, c => Assert.InRange(c, '!', '~'));
        }

        [Fact]
        public void Transform_RandomKey_Literal_Charset()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key,20,ABC", "ignored");
            Assert.Equal(20, key.Length);
            Assert.All(key, c => Assert.Contains(c, "ABC"));
        }

        [Fact]
        public void Transform_RandomKey_Escaped_Comma_In_Charset()
        {
            var (_, engine) = Create();
            var key = engine.Transform("random-key,20,AB\\,", "ignored");
            Assert.Equal(20, key.Length);
            Assert.All(key, c => Assert.Contains(c, "AB,"));
        }

        [Fact]
        public void Transform_RandomKey_Bad_Length_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.Transform("random-key,abc", "ignored"));
            Assert.Throws<Exception>(() => engine.Transform("random-key,0", "ignored"));
            Assert.Throws<Exception>(() => engine.Transform("random-key,-4", "ignored"));
        }

        [Fact]
        public void Transform_RandomKey_TooManyArgs_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.Transform("random-key,32,hex,extra", "ignored"));
        }

        [Fact]
        public void Transform_RandomKey_Single_Character_Charset_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.Transform("random-key,10,AAAA", "ignored"));
        }

        [Fact]
        public void RandomKey_Uses_Whole_Charset()
        {
            // A long key from a 2-character set must contain both characters.
            var key = TransformEngine.RandomKey(200, "XY");
            Assert.Contains('X', key);
            Assert.Contains('Y', key);
        }

        [Fact]
        public void Transform_When_Equal_True()
        {
            var (_, engine) = Create(("TYPE", "live"));
            Assert.Equal("1", engine.Transform("when,~TYPE~,==,live", "ignored"));
        }

        [Fact]
        public void Transform_When_Equal_False()
        {
            var (_, engine) = Create(("TYPE", "test"));
            Assert.Equal("", engine.Transform("when,~TYPE~,==,live", "ignored"));
        }

        [Fact]
        public void Transform_When_NotEqual()
        {
            var (_, engine) = Create(("TYPE", "test"));
            Assert.Equal("1", engine.Transform("when,~TYPE~,!=,live", "ignored"));
        }

        [Fact]
        public void Transform_When_Eq_Alias()
        {
            var (_, engine) = Create(("X", "y"));
            Assert.Equal("1", engine.Transform("when,~X~,eq,y", "ignored"));
        }

        [Fact]
        public void Transform_When_Ne_Alias()
        {
            var (_, engine) = Create(("X", "y"));
            Assert.Equal("1", engine.Transform("when,~X~,ne,z", "ignored"));
        }

        [Fact]
        public void Transform_Unknown_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.Transform("bogus,1,2", "val"));
        }

        [Fact]
        public void Transform_WrongArgCount_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.Transform("replace,onlyone", "val"));
        }

        // --- Compare ---

        [Fact]
        public void Compare_Equal_Match()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare("a", "==", "a"));
        }

        [Fact]
        public void Compare_Equal_NoMatch()
        {
            var (_, engine) = Create();
            Assert.Equal("", engine.Compare("a", "==", "b"));
        }

        [Fact]
        public void Compare_NotEqual_Match()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare("a", "!=", "b"));
        }

        [Fact]
        public void Compare_Eq_Alias()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare("x", "eq", "x"));
        }

        [Fact]
        public void Compare_SingleEquals_Alias()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare("x", "=", "x"));
        }

        [Fact]
        public void Compare_DiamondNotEqual_Alias()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare("a", "<>", "b"));
        }

        [Fact]
        public void Compare_Null_Equals_Null()
        {
            var (_, engine) = Create();
            Assert.Equal("1", engine.Compare(null, "==", null));
        }

        [Fact]
        public void Compare_Unknown_Operator_Returns_Empty()
        {
            var (_, engine) = Create();
            Assert.Equal("", engine.Compare("a", "???", "b"));
        }

        // --- ExpandVars ---

        [Fact]
        public void ExpandVars_Null_Returns_Empty()
        {
            var (_, engine) = Create();
            Assert.Equal("", engine.ExpandVars((string?)null));
        }

        [Fact]
        public void ExpandVars_NoVars_Passthrough()
        {
            var (_, engine) = Create();
            Assert.Equal("plain text", engine.ExpandVars("plain text"));
        }

        [Fact]
        public void ExpandVars_TildeVar()
        {
            var (_, engine) = Create(("VER", "1.0"));
            Assert.Equal("v1.0", engine.ExpandVars("v~VER~"));
        }

        [Fact]
        public void ExpandVars_MultipleTildeVars()
        {
            var (_, engine) = Create(("A", "hello"), ("B", "world"));
            Assert.Equal("hello-world", engine.ExpandVars("~A~-~B~"));
        }

        [Fact]
        public void ExpandVars_EnvVar()
        {
            var (_, engine) = Create();
            var result = engine.ExpandVars("$PATH");
            Assert.False(string.IsNullOrEmpty(result));
            Assert.DoesNotContain("$", result);
        }

        [Fact]
        public void ExpandVars_Unknown_TildeVar_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.ExpandVars("~NOPE~"));
        }

        [Fact]
        public void ExpandVars_Unknown_EnvVar_Throws()
        {
            var (_, engine) = Create();
            Assert.Throws<Exception>(() => engine.ExpandVars("$DEFINITELY_NOT_A_REAL_ENV_VAR_XYZ123"));
        }

        [Fact]
        public void ExpandVars_Mixed_Tilde_And_Env()
        {
            var (_, engine) = Create(("NAME", "test"));
            var result = engine.ExpandVars("~NAME~-$PATH");
            Assert.StartsWith("test-", result);
        }

        [Fact]
        public void ExpandVars_XAttribute_Null_Returns_Empty()
        {
            var (_, engine) = Create();
            Assert.Equal("", engine.ExpandVars((XAttribute?)null));
        }

        [Fact]
        public void ExpandVars_XAttribute_With_Value()
        {
            var (_, engine) = Create(("X", "42"));
            var attr = new XAttribute("test", "~X~");
            Assert.Equal("42", engine.ExpandVars(attr));
        }

        [Fact]
        public void Constructor_Null_Vars_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new TransformEngine(null!));
        }
    }
}
