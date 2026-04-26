using System.Text.Json;

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
            var dryRun = options.GetSwitchArgument("dry-run", 'd');
            var useShellExecute = options.GetSwitchArgument("shell-exec", 's');
            var outputManifest = options.GetStringArgument("output-manifest", 'o');
            var toolPaths = new List<DirectoryInfo>();
            var modules = new List<string>();

            if (showHelp)
            {
                RLog.InfoFormat("ReleaseBuilder ");
                RLog.InfoFormat(" --(r)oot             Root folder");
                RLog.InfoFormat(" --(c)onfig           ReleaseConfig.xml file");
                RLog.InfoFormat(" --(d)ry-run          Parse and validate without executing any build actions");
                RLog.InfoFormat(" --(m)odule           Add path to search for tools. Can occur multiple times.");
                RLog.InfoFormat(" --(n)obuild          Do not build artefacts");
                RLog.InfoFormat(" --(o)utput-manifest  Write JSON build manifest to file (use - for stdout)");
                RLog.InfoFormat(" --(p) --toolsdir     Add path to search for tools. Can occur multiple times.");
                RLog.InfoFormat(" --(s)hell-exec       Use ShellExecute.");
                RLog.InfoFormat(" --(t)arget           Target to build");
                RLog.InfoFormat(" --(v)erbose          Increase verbosity. Can be used twice.");
                return ExitCodes.Success;
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
                RLog.DebugFormat("dry-run {0}", dryRun);
                if (modules.Any())
                    RLog.TraceFormat("building only {0}", string.Join(",", modules));
            }
            if (options.IsInvalid())
                return ExitCodes.InvalidArguments;
            try
            {
                var rb = new ReleaseBuilder(root, xmlConfig, target, toolPaths, nobuild, useShellExecute, dryRun);
                if (!rb.IsValid)
                    return ExitCodes.ConfigError;
                modules.ForEach(m => { rb.AddModule(m); });
                if (!rb.Build())
                    return ExitCodes.BuildError;
                var result = rb.Process();
                if (!string.IsNullOrEmpty(outputManifest) && rb.Manifest != null)
                {
                    var json = JsonSerializer.Serialize(rb.Manifest, new JsonSerializerOptions { WriteIndented = true });
                    if (outputManifest == "-")
                        Console.WriteLine(json);
                    else
                        File.WriteAllText(outputManifest, json);
                }
                return result;
            }
            catch (Exception ex)
            {
                RLog.ErrorFormat(ex.Message);
                return ExitCodes.BuildError;
            }
        }
    }
}