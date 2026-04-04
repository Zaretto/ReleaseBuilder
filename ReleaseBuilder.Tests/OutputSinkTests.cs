using rjtool;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class OutputSinkTests
    {
        [Fact]
        public void Write_SingleLine_NoException()
        {
            var sink = new OutputSink();
            sink.Write("hello", OutputSink.StreamType.StdOut);
        }

        [Fact]
        public void Write_MultipleLines_NoException()
        {
            var sink = new OutputSink();
            sink.Write("line1", OutputSink.StreamType.StdOut);
            sink.Write("line2", OutputSink.StreamType.StdOut);
            sink.Write("line3", OutputSink.StreamType.StdOut);
        }

        [Fact]
        public void Flush_Empty_NoException()
        {
            var sink = new OutputSink();
            sink.Flush();
        }

        [Fact]
        public void Flush_After_Write_NoException()
        {
            var sink = new OutputSink();
            sink.Write("data", OutputSink.StreamType.StdOut);
            sink.Flush();
        }

        [Fact]
        public void Write_StdErr_NoException()
        {
            var sink = new OutputSink();
            sink.Write("error", OutputSink.StreamType.StdErr);
            sink.Flush();
        }

        [Fact]
        public void Write_Interleaved_NoException()
        {
            var sink = new OutputSink();
            sink.Write("out1", OutputSink.StreamType.StdOut);
            sink.Write("err1", OutputSink.StreamType.StdErr);
            sink.Write("out2", OutputSink.StreamType.StdOut);
            sink.Flush();
        }

        [Fact]
        public void ThreadSafety()
        {
            var sink = new OutputSink();
            var tasks = Enumerable.Range(0, 10).Select(i =>
                Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        sink.Write($"thread{i}-line{j}", i % 2 == 0 ? OutputSink.StreamType.StdOut : OutputSink.StreamType.StdErr);
                    }
                })).ToArray();

            Task.WaitAll(tasks);
            sink.Flush();
        }
    }
}
