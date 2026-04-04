using ReleaseBuilder;
using rjtool;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class PackedIntegerVersionTests
    {
        [Fact]
        public void Pack_1_0_0()
        {
            var v = new PackedIntegerVersion(1, 0, 0);
            Assert.Equal(10000, v.Value);
        }

        [Fact]
        public void Pack_1_2_3()
        {
            var v = new PackedIntegerVersion(1, 2, 3);
            Assert.Equal(10203, v.Value);
        }

        [Fact]
        public void Unpack_Major()
        {
            var v = new PackedIntegerVersion(5, 3, 1);
            Assert.Equal(5, v.Major);
        }

        [Fact]
        public void Unpack_Minor()
        {
            var v = new PackedIntegerVersion(5, 3, 1);
            Assert.Equal(3, v.Minor);
        }

        [Fact]
        public void Unpack_Patch()
        {
            var v = new PackedIntegerVersion(5, 3, 1);
            Assert.Equal(1, v.Patch);
        }

        [Fact]
        public void Roundtrip()
        {
            var v = new PackedIntegerVersion(12, 34, 56);
            Assert.Equal(12, v.Major);
            Assert.Equal(34, v.Minor);
            Assert.Equal(56, v.Patch);
        }

        [Fact]
        public void ToString_Format()
        {
            var v = new PackedIntegerVersion(1, 2, 3);
            Assert.Equal("1.2.3", v.ToString());
        }

        [Fact]
        public void Zero_Version()
        {
            var v = new PackedIntegerVersion(0, 0, 0);
            Assert.Equal(0, v.Value);
            Assert.Equal("0.0.0", v.ToString());
        }

        [Fact]
        public void Max_Version()
        {
            var v = new PackedIntegerVersion(9999, 99, 99);
            Assert.Equal(99999999, v.Value);
            Assert.Equal("9999.99.99", v.ToString());
        }

        [Fact]
        public void Major_Overflow_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PackedIntegerVersion(10000, 0, 0));
        }

        [Fact]
        public void Minor_Overflow_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PackedIntegerVersion(1, 100, 0));
        }

        [Fact]
        public void Patch_Overflow_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PackedIntegerVersion(1, 0, 100));
        }

        [Fact]
        public void Negative_Major_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PackedIntegerVersion(-1, 0, 0));
        }

        [Fact]
        public void Scale_Factor_3()
        {
            var v = new PackedIntegerVersion(1, 2, 3, scale: 3);
            Assert.Equal(1000000, v.MajorFactor);
            Assert.Equal(1000, v.MinorFactor);
            Assert.Equal(1002003, v.Value);
            Assert.Equal(1, v.Major);
            Assert.Equal(2, v.Minor);
            Assert.Equal(3, v.Patch);
        }

        [Fact]
        public void Long_Constructor()
        {
            var v = new PackedIntegerVersion((long?)2, (long?)5, (long?)7);
            Assert.Equal(20507, v.Value);
        }

        [Fact]
        public void Long_Null_Defaults()
        {
            // null major defaults to 1, null minor/patch default to 0
            var v = new PackedIntegerVersion((long?)null, (long?)null, (long?)null);
            Assert.Equal(10000, v.Value);
        }

        [Fact]
        public void Combined_Value_Constructor()
        {
            var v = new PackedIntegerVersion(30201);
            Assert.Equal(3, v.Major);
            Assert.Equal(2, v.Minor);
            Assert.Equal(1, v.Patch);
        }

        [Fact]
        public void GitVersion_Constructor()
        {
            var info = new GitVersion { Major = 4, Minor = 5, Patch = 6 };
            var v = new PackedIntegerVersion(info);
            Assert.Equal(40506, v.Value);
            Assert.Equal("4.5.6", v.ToString());
        }
    }
}
