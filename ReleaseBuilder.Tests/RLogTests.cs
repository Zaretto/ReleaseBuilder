using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class RLogTests
    {
        public RLogTests()
        {
            RLog.ResetErrorCount();
        }

        [Fact]
        public void ErrorFormat_increments_ErrorCount()
        {
            RLog.ErrorFormat("test error");
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void FatalFormat_increments_ErrorCount()
        {
            RLog.FatalFormat("test fatal");
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void InfoFormat_does_not_increment_ErrorCount()
        {
            RLog.InfoFormat("test info");
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void DebugFormat_does_not_increment_ErrorCount()
        {
            RLog.DebugFormat("test debug");
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void ResetErrorCount_clears_count()
        {
            RLog.ErrorFormat("error 1");
            RLog.ErrorFormat("error 2");
            Assert.Equal(2, RLog.ErrorCount);
            RLog.ResetErrorCount();
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Multiple_errors_accumulate()
        {
            RLog.ErrorFormat("error 1");
            RLog.ErrorFormat("error 2");
            RLog.ErrorFormat("error 3");
            Assert.Equal(3, RLog.ErrorCount);
        }
    }
}
