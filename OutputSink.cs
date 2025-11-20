using ReleaseBuilder;
using System.Text;

namespace rjtool
{
    public partial class fexec
    {
        // Output sink that ensures complete lines are written without intermixing
        private class OutputSink
        {
            public enum StreamType { StdOut, StdErr }

            private readonly object _lock = new object();
            private readonly StringBuilder _stdOutBuffer = new StringBuilder();
            private readonly StringBuilder _stdErrBuffer = new StringBuilder();

            public void Write(string data, StreamType streamType)
            {
                lock (_lock)
                {
                    var buffer = streamType == StreamType.StdOut ? _stdOutBuffer : _stdErrBuffer;
                    buffer.Append(data);
                    buffer.AppendLine(); // The data from OutputDataReceived is already line-based

                    // Output complete lines immediately
                    OutputCompleteLines();
                }
            }

            public void Flush()
            {
                lock (_lock)
                {
                    // Output any remaining partial lines from stdout
                    if (_stdOutBuffer.Length > 0)
                    {
                        RLog.DebugFormat(_stdOutBuffer.ToString().TrimEnd('\r', '\n'));
                        _stdOutBuffer.Clear();
                    }

                    // Output any remaining partial lines from stderr
                    if (_stdErrBuffer.Length > 0)
                    {
                        RLog.DebugFormat(_stdErrBuffer.ToString().TrimEnd('\r', '\n'));
                        _stdErrBuffer.Clear();
                    }
                }
            }

            private void OutputCompleteLines()
            {
                // Output complete lines from stdout buffer
                OutputCompleteLines(_stdOutBuffer);

                // Output complete lines from stderr buffer
                OutputCompleteLines(_stdErrBuffer);
            }

            private void OutputCompleteLines(StringBuilder buffer)
            {
                string text = buffer.ToString();
                int lastNewLine = text.LastIndexOf('\n');

                if (lastNewLine >= 0)
                {
                    // Output everything up to and including the last newline
                    string toOutput = text.Substring(0, lastNewLine + 1).TrimEnd('\r', '\n');
                    if (!string.IsNullOrEmpty(toOutput))
                    {
                        // Split into lines and output each
                        var lines = toOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrEmpty(line) || lines.Length == 1)
                            {
                                RLog.DebugFormat(line);
                            }
                        }
                    }

                    // Keep only the partial line after the last newline
                    buffer.Clear();
                    if (lastNewLine < text.Length - 1)
                    {
                        buffer.Append(text.Substring(lastNewLine + 1));
                    }
                }
            }
        }
    }
}
