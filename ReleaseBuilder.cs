using rjtool;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace ReleaseBuilder
{
    public class ReleaseBuilder
    {
        public DirectoryInfo ToolsDirectory;
        public List<Artefact> Artefacts = new List<Artefact>();
        public string PublishType = "live";
        public string Root;
        private Dictionary<string, string> vars = new Dictionary<string, string>();

        public Dictionary<string, PublishTarget> Targets { get; private set; } = new Dictionary<string, PublishTarget>();
        public string TargetName { get; private set; }

        public ReleaseBuilder(DirectoryInfo? root, FileInfo? configFile, string? target, DirectoryInfo? toolsdir)
        {
            if (toolsdir != null)
            {
                ToolsDirectory = toolsdir;
            }
            else if (Directory.Exists(AppDomain.CurrentDomain.BaseDirectory))
                ToolsDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            if (ToolsDirectory != null)
                RLog.TraceFormat("Tools directory {0}", ToolsDirectory.FullName);

            if (root != null)
                Root = root.FullName;
            else
                Root = Directory.GetCurrentDirectory();

            var baseConfig = "ReleaseConfig.Xml";
            var cpath = baseConfig;
            if (configFile != null)
                cpath = configFile.FullName;
            if (!File.Exists(cpath))
                cpath = Path.Combine(Root, baseConfig);

            if (File.Exists(cpath))
            {
                configFile = new FileInfo(cpath);

                if (target != null)
                    PublishType = target;

                RLog.TraceFormat("Using config file {0}", configFile.FullName);

                GetVersion();

                RLog.InfoFormat("Version {0}", vars["SemVer"]);

                LoadConfigFromXml(configFile);

                if (!Targets.ContainsKey(PublishType))
                    RLog.InfoFormat("No target config for {0}", target);

                RLog.InfoFormat("Publish config target: {0}", PublishType);
            }
            else RLog.ErrorFormat("Could not locate config file");
        }

        private void LoadConfigFromXml(FileInfo configFile)
        {
            var doc = XDocument.Load(configFile.FullName);
            XPathNavigator navigator = doc.CreateNavigator();
            foreach (XElement node in doc.Root.Nodes())
            {
                if (node.Name == "Name")
                    TargetName = node.Value;
                if (node.Name == "Target")
                {
                    //     <Target name="test" path="$SYNC_MSOS_TEST" archive="7z a -r" />
                    var name = node.Attribute("name");
                    if (name == null)
                    {
                        RLog.ErrorFormat("Target Name missing");
                        continue;
                    }
                    Targets[name.Value] = new PublishTarget(name.Value, expand_vars(node.Attribute("path")));
                    var av = GetAttribute(node, "archive-version");
                    if (av != null)
                    {
                        av = Transform(av);
                        Targets[name.Value].Version = av;
                    }
                }
                if (node.Name == "Folder")
                {
                    //    <Folder name="APPPATH" path="MSOS.UWP_*" version="latest" />
                    var name = GetAttribute(node, "name");
                    var path = GetAttribute(node, "path");
                    var version = GetAttribute(node, "version");
                    if (name != null && path != null)
                    {
                        var matches = Directory.EnumerateDirectories(Root, path)
                                .Select(xx => new DirectoryInfo(xx));
                        if (!matches.Any())
                        {
                            RLog.ErrorFormat("{0} no folders matching {1}", name, path);
                        }
                        else
                        {
                            var ordered = matches.OrderByDescending(xx => xx.CreationTime).ToList();
                            switch (version)
                            {
                                case "latest":
                                    ordered = matches.OrderByDescending(xx => xx.CreationTime).ToList();
                                    break;
                            }
                            vars[name] = ordered.First().FullName;
                        }
                    }
                }
                if (node.Name == "Artefact")
                {
                    pn(node, "file", (file) =>
                    {
                        if (file != null)
                        {
                            if (file.Contains("*"))
                                Artefacts.Add(new Artefact(expand_vars(file)));
                            else
                            {
                                file = PathFinder.FindFile(file,
                                        new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                        });
                                if (file != null)
                                {
                                    Artefacts.Add(new Artefact(new FileInfo(file)));
                                }
                                else
                                    RLog.ErrorFormat("Cannot locate file artefact {0}", file);
                            }
                        }
                    });
                    pn(node, "directory,folder", (directory) =>
                    {
                        if (directory != null)
                        {
                            if (directory.Contains("*"))
                                Artefacts.Add(new Artefact(expand_vars(directory)));
                            else
                            {
                                directory = PathFinder.FindDirectory(directory,
                                     new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                                if (directory != null)
                                {
                                    Artefacts.Add(new Artefact(new DirectoryInfo(directory)));
                                }
                                else
                                    RLog.ErrorFormat("Cannot locate folder artefact {0}", directory!);
                            }
                        }
                    });
                }
            }
        }
        private void argCheck(string[] parts, int requiredLength, string transformation)
        {
            if (parts.Length != requiredLength)
                throw new Exception(String.Format("ERROR: Transform: {0} {1} arguments required; [{2}]", transformation, requiredLength, string.Join(",", parts)));
        }

        private string Transform(string Transformation)
        {
            var parts = Transformation.Split(',');
            if (parts.Length >= 1)
            {
                var method = parts[0];
                switch (method.ToLower())
                {
                    case "getversion":
                        {
                            argCheck(parts, 2, Transformation);
                            var v = expand_vars(parts[1]);
                            var vp = v.Split('\\');
                            v = vp.Last();
                            var rv = Regex.Match(v, @"\d+.+\d");
                            if (rv != null)
                                return rv.Value;
                            return Transformation;
                        }
                        break;


                    //case "replace":
                    //    {
                    //        argCheck(parts, 3, Transformation);
                    //        var s1 = parts[1];
                    //        var s2 = parts[2];
                    //        v = v.Replace(s1, s2);
                    //    }
                    //    break;
                    //case "eval":
                    //    {
                    //        if (string.IsNullOrEmpty(v))
                    //            return v;
                    //        argCheck(parts, 2, Transformation);
                    //        v = eval.Evaluate(parts[1].Replace("v", v)).ToString();

                    //        break;
                    //    }
                    default:
                        throw new Exception("Uknown transform " + method);
                }
            }
            return Transformation;
        }

        private void pn(XElement node, string atttributes, Action<string?> p)
        {
            foreach (var v in atttributes.Split(','))
            {
                var avs = node.Attributes(v);
                foreach (var av in avs)
                    p(av.Value);
            }
        }

        private string? GetAttribute(XElement node, string v)
        {
            var rv = node.Attribute(v);
            if (rv == null)
            {
                RLog.ErrorFormat("Cannot locate attribute {0}", v);
                return null;
            }
            return rv.Value;
        }

        void GetVersion()
        {
            var Info = GitVersion.ForDirectory(Root);
            if (Info != null)
            {
                addvar("SemVer", Info.NuGetVersionV2);
                addvar("Major", Info.Major);
                addvar("Minor", Info.Minor);
                addvar("Patch", Info.Patch);
                addvar("PreReleaseTag", Info.PreReleaseTag);
            }
        }

        private void addvar(string k, string v)
        {
            vars[k] = v;
        }

        private void addvar(string k, long? v)
        {
            if (v.HasValue)
                vars[k] = v.Value.ToString();
        }
        public string expand_vars(XAttribute? att)
        {
            if (att == null)
                return "";
            return expand_vars(att.Value);
        }
        public string expand_vars(string rv)
        {
            if (rv.StartsWith("$"))
                rv = Environment.GetEnvironmentVariable(rv.Substring(1));
            else foreach (var var in vars)
                {
                    var rt = "~" + var.Key + "~";
                    rv = rv.Replace(rt, var.Value);
                }
            //cmd = cmd.Replace("~VERSION~", release_version);
            //cmd = cmd.Replace("~PATH~", ArchivePath);
            //cmd = cmd.Replace("~TYPE~", PublishType);
            //cmd = cmd.Replace("~APPPATH~", APPPATH);
            return rv;
        }


        public int Process()
        {
            if (Targets.Any())
            {
                var target = Targets[PublishType];
                var filename = new List<string>
                {
                    TargetName,
                    PublishType,
                    target.GetVersion(vars["SemVer"])
                };
                int fileCount = 0;
                var zipFileName = Path.Combine(target.Path.FullName, string.Join("-", filename) + ".zip");
                File.Delete(zipFileName);
                {
                    using (FileStream zipToOpen = new FileStream(zipFileName, FileMode.Create))
                    {
                        using (var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Update))
                        {
                            foreach (var file in Artefacts.SelectMany(xx => xx.GetFiles(Root)).Distinct())
                            {
                                var archiveName = file.Replace(Root, "").Trim("\\/".ToArray());

                                RLog.TraceFormat(String.Format("Adding {0}", archiveName));

                                ZipArchiveEntry entry = archive.CreateEntry(archiveName, CompressionLevel.Optimal);
                                using (var infile = File.OpenRead(file))
                                {
                                    fileCount++;
                                    using (var os = entry.Open())
                                    {
                                        infile.CopyTo(os);
                                    }
                                }
                            }
                        }
                    }
                    RLog.InfoFormat(String.Format("Created {0} with {1} files", zipFileName, fileCount));
                }
                return 0;
            }
            return 1;
        }
    }
}