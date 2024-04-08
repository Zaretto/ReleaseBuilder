using Microsoft.VisualBasic;
using System.Reflection.PortableExecutable;

namespace ReleaseBuilder
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {

            var options = new OptionsParser(args);
            var root = options.GetDirectoryArgument("root", 'r');
            var xmlConfig = options.GetFileArgument("config", 'c');
            var target = options.GetStringArgument("target", 't');
            var verbose = options.GetSwitchArgument("verbose", 'v');
            var extraVerbose = options.GetSwitchArgument("verbose", 'v');
            var nobuild = options.GetSwitchArgument("nobuild", 'n');
            var toolPaths = new List<DirectoryInfo>();
            while (true)
            {
                var path = options.GetDirectoryArgument("toolsdir", 'p');
                if (path != null)
                    toolPaths.Add(path);
                else
                    break;
            }
            if (verbose)
            {
                RLog.Level = LogMessageLevel.Trace;
                if (extraVerbose)
                    RLog.Level = LogMessageLevel.Debug;
                if (root != null)
                    RLog.DebugFormat("root {0}", root);
                if (toolPaths.Any())
                    RLog.DebugFormat("toolsdir {0}", string.Join(",", toolPaths.Select(xx=>xx.FullName)));
                if (xmlConfig != null)
                    RLog.DebugFormat("xmlConfig {0}", xmlConfig);
                if (target != null)
                    RLog.DebugFormat("target {0}", target);
                RLog.DebugFormat("verbose {0}", verbose);
                RLog.DebugFormat("nobuild {0}", nobuild);
            }
            if (options.IsInvalid())
                return -1;
            try
            {
                var rb = new ReleaseBuilder(root, xmlConfig, target, toolPaths, nobuild);
                return rb.Process();
            }
            catch (Exception ex)
            {
                RLog.ErrorFormat(ex.Message);
                return -1;
            }
        }
    }
}