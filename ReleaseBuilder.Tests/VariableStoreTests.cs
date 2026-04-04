using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class VariableStoreTests
    {
        [Fact]
        public void Set_And_Get()
        {
            var store = new VariableStore();
            store.Set("key", "value");
            Assert.Equal("value", store.Get("key"));
        }

        [Fact]
        public void Get_Missing_Throws()
        {
            var store = new VariableStore();
            Assert.Throws<KeyNotFoundException>(() => store.Get("nope"));
        }

        [Fact]
        public void TryGet_Found()
        {
            var store = new VariableStore();
            store.Set("key", "value");
            Assert.True(store.TryGet("key", out var val));
            Assert.Equal("value", val);
        }

        [Fact]
        public void TryGet_NotFound()
        {
            var store = new VariableStore();
            Assert.False(store.TryGet("nope", out _));
        }

        [Fact]
        public void Overwrite()
        {
            var store = new VariableStore();
            store.Set("key", "first");
            store.Set("key", "second");
            Assert.Equal("second", store.Get("key"));
        }

        [Fact]
        public void ContainsKey_True()
        {
            var store = new VariableStore();
            store.Set("key", "value");
            Assert.True(store.ContainsKey("key"));
        }

        [Fact]
        public void ContainsKey_False()
        {
            var store = new VariableStore();
            Assert.False(store.ContainsKey("nope"));
        }

        [Fact]
        public void Indexer_Get()
        {
            var store = new VariableStore();
            store.Set("key", "value");
            Assert.Equal("value", store["key"]);
        }

        [Fact]
        public void Indexer_Set()
        {
            var store = new VariableStore();
            store["key"] = "value";
            Assert.Equal("value", store.Get("key"));
        }

        [Fact]
        public void All_Returns_Items()
        {
            var store = new VariableStore();
            store.Set("a", "1");
            store.Set("b", "2");
            var all = store.All;
            Assert.Equal(2, all.Count);
            Assert.Equal("1", all["a"]);
            Assert.Equal("2", all["b"]);
        }

        [Fact]
        public void Set_Long_Value()
        {
            var store = new VariableStore();
            store.Set("key", (long?)42);
            Assert.Equal("42", store.Get("key"));
        }

        [Fact]
        public void Set_Long_Null_NoOp()
        {
            var store = new VariableStore();
            store.Set("key", (long?)null);
            Assert.False(store.ContainsKey("key"));
        }

        [Fact]
        public void CaseInsensitive()
        {
            var store = new VariableStore();
            store.Set("Foo", "bar");
            Assert.Equal("bar", store.Get("foo"));
            Assert.Equal("bar", store.Get("FOO"));
        }
    }
}
