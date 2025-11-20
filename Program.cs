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
            var showHelp = options.GetSwitchArgument("help", 'h');
            var nobuild = options.GetSwitchArgument("nobuild", 'n');
            var useShellExecute = options.GetSwitchArgument("shell-exec", 's');
            var toolPaths = new List<DirectoryInfo>();
            var modules = new List<string>();

            if (showHelp)
            {
                RLog.InfoFormat("ReleaseBuilder ");
                RLog.InfoFormat(" --(r)oot             Root folder");
                RLog.InfoFormat(" --(c)onfig           ReleaseConfig.xml file");
                RLog.InfoFormat(" --(m)odule           Add path to search for tools. Can occur multiple times.");
                RLog.InfoFormat(" --(n)obuild          Do not build artefacts");
                RLog.InfoFormat(" --(p) --toolsdir     Add path to search for tools. Can occur multiple times.");
                RLog.InfoFormat(" --(s)hell-exec       Use ShellExecute.");
                RLog.InfoFormat(" --(t)arget           Target to build");
                RLog.InfoFormat(" --(v)erbose          Increase verbosity. Can be used twice.");
                return 0;
            }
            while (true)
            {
                var path = options.GetDirectoryArgument("toolsdir", 'p');
                if (path != null)
                    toolPaths.Add(path);
                else
                    break;
            } 
            while (true)
            {
                var module = options.GetStringArgument("module", 'm');
                if (!string.IsNullOrEmpty(module))
                    modules.Add(module);
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
                if (modules.Any())
                    RLog.TraceFormat("building only {0}", string.Join(",", modules));
            }
            if (options.IsInvalid())
                return -1;
            try
            {
                var rb = new ReleaseBuilder(root, xmlConfig, target, toolPaths, nobuild, useShellExecute);
                if (!rb.IsValid)
                    return 0;
                modules.ForEach(m => {rb.AddModule(m);});
                rb.Build();
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