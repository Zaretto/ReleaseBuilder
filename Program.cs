using Microsoft.VisualBasic;

namespace ReleaseBuilder
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {

            var options = new OptionsParser(args);
            var root = options.GetDirectoryArgument("root", 'r');
            var toolsdir = options.GetDirectoryArgument("toolspath", 'p');
            var xmlConfig = options.GetFileArgument("config", 'c');
            var target = options.GetStringArgument("target", 't');
            var verbose = options.GetSwitchArgument("verbose", 'v');

            if (verbose)
                RLog.Level = LogMessageLevel.Trace;

            var rb = new ReleaseBuilder(root, xmlConfig, target, toolsdir);
            return rb.Process();
        }
    }
}