using rjtool;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace ReleaseBuilder
{
    public class ReleaseBuilder
    {
        public List<DirectoryInfo> ToolsDirectories;
        public List<Artefact> Artefacts = new List<Artefact>();
        public string PublishType = "live";
        public string Root;
        private Dictionary<string, string> vars = new Dictionary<string, string>();

        public Dictionary<string, PublishTarget> Targets { get; private set; } = new Dictionary<string, PublishTarget>();
        public string TargetName { get; private set; }

        public ReleaseBuilder(DirectoryInfo? root, FileInfo? configFile, string? target, IEnumerable<DirectoryInfo> toolsdir, bool nobuild)
        {
            ToolsDirectories = new List<DirectoryInfo>();
            if (toolsdir != null)
                ToolsDirectories.AddRange(toolsdir);

            if (Directory.Exists(AppDomain.CurrentDomain.BaseDirectory))
                ToolsDirectories.Add(new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory));
            if (ToolsDirectories.Any())
                RLog.TraceFormat("Tools directories {0}", string.Join(",", ToolsDirectories.Select(xx=>xx.FullName)));
            if (root != null)
                Root = root.FullName;
            else
                Root = Directory.GetCurrentDirectory();

            fexec.AddExePath(ToolsDirectories);

            var baseConfig = "ReleaseConfig.Xml";
            var cpath = baseConfig;

            /// 1. if file specified on the command line then use this.
            /// 2. if config file found in root then use this
            /// 3. otherwise use config from current directory.
            /// 
            if (configFile != null)
                cpath = configFile.FullName;
            else
                cpath = Path.Combine(Root, baseConfig);
            if (!File.Exists(cpath))
                cpath = baseConfig;

            if (File.Exists(cpath))
            {
                configFile = new FileInfo(cpath);

                if (target != null)
                    PublishType = target;

                addvar("TYPE", PublishType); 
                addvar("PUBLISHROOT", Root);

                RLog.TraceFormat("Using config file {0}", configFile.FullName);

                GetVersion();

                RLog.InfoFormat("Version {0}", vars["SemVer"]);

                LoadConfigFromXml(configFile, nobuild);

                if (!Targets.ContainsKey(PublishType))
                    RLog.InfoFormat("No target config for {0}", target);

                RLog.InfoFormat("Publish config target: {0}", PublishType);
            }
            else RLog.ErrorFormat("Could not locate config file");
        }

        private void LoadConfigFromXml(FileInfo configFile, bool nobuild)
        {
            var doc = XDocument.Load(configFile.FullName);
            XPathNavigator navigator = doc.CreateNavigator();
            foreach (XElement node in doc.Root.Nodes().OfType<XElement>())
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
                    var av = GetAttribute(node, "archive-version", "");
                    if (!string.IsNullOrEmpty(av))
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
                    var version = GetAttribute(node, "version", "latest");
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
                if (node.Name == "Artefacts")
                {
                    var artefactFolder = GetAttribute(node, "folder", "");
                    if (!string.IsNullOrEmpty(artefactFolder))
                    {
                        artefactFolder = PathFinder.FindDirectory(expand_vars(artefactFolder),
                         new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                         });

                    }
                 
                    foreach(XElement child in node.Elements())
                    {
                        switch(child.Name.ToString().ToLower())
                        {
                            case "file":
                                {
                                    var file = expand_vars(child.Value);
                                    var fileFolder = GetAttribute(child, "folder", "");
                                    if (fileFolder != "")
                                    {
                                        var nfolder = PathFinder.FindDirectory(expand_vars(fileFolder),
                                             new[] {
                                        new List<string>(new []{artefactFolder}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                             });
                                        if (CheckParamNotEmpty(fileFolder, "folder must point to a directory " + fileFolder))
                                            file = Path.Combine(nfolder, file);
                                    }
                                    file = AddFile(file);
                                }
                                break;
                            case "folder":
                                var folder = AddFolder(expand_vars(child.Value));
                                break;
                            case "build":
                                if (nobuild)
                                    RLog.InfoFormat("Ignoring build step");
                                else
                                    processBuild(child, artefactFolder);
                                break;

                            default:
                                RLog.ErrorFormat("Unknown artefact type {0}", child.Name);
                                break;
                        }
                    }    
                }
                if (node.Name == "Artefact")
                {
                    processAttributes(node, "file", (file) =>
                    {
                        file = AddFile(file);
                    });
                    processAttributes(node, "directory,folder", (directory) =>
                    {
                        directory = AddFolder(directory);
                    });
                }
            }
        }

        private void processBuild(XElement artefactsNode, string path)
        {
            foreach (XElement actionNode in artefactsNode.Elements())
            {
                var action = actionNode.Name.ToString().ToLower();
                switch (action)
                {
                    case "clean":
                        {
                            var cleanFolder = GetAttribute(actionNode, "folder", "");

                            if (CheckParamNotEmpty(cleanFolder, "Folder to clean required"))
                            {
                                cleanFolder = PathFinder.FindDirectory(expand_vars(cleanFolder), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                    });
                                if (CheckParamNotEmpty(cleanFolder, "folder to clean must be found"))
                                {
                                    RLog.InfoFormat("Cleaning folder {0}", cleanFolder);
                                    foreach (var toDel in Directory.EnumerateFiles(cleanFolder, "*.*", SearchOption.AllDirectories))
                                    {
                                        RLog.TraceFormat("del {0}", toDel);
                                        File.Delete(toDel);
                                    }
                                }
                            }
                            break;
                        }
                    case "create":
                        {
                            var file = expand_vars(GetAttribute(actionNode, "file"));
                            var filePath = PathFinder.FindFile(file, new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            if (filePath != null)
                                file = filePath;
                            else
                            {
                                var fileFolder = PathFinder.FindDirectory(Path.GetDirectoryName(file), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                                if (fileFolder != null)
                                    file = Path.Join(fileFolder, Path.GetFileName(file));
                                else
                                    throw new Exception("Could not create " + file + " as path not found");
                            }
                            var text = actionNode.Value;
                            text = expand_vars(text).ReplaceLineEndings("\n");
                            File.WriteAllText(file, text);
                            break;
                        }
                    case "copy":
                        {
                            var from = GetAttribute(actionNode, "from");

                            // if from points to a file then just copy that.
                            var fromPath = PathFinder.FindFile(expand_vars(from), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

                            if (fromPath == null)
                                fromPath = PathFinder.FindDirectory(expand_vars(from), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            var newName = GetAttribute(actionNode, "name", "");
                            var match = GetAttribute(actionNode, "match", "*.*")!;
                            var to = GetAttribute(actionNode, "to", path);
                            var toPath = PathFinder.FindDirectory(expand_vars(to), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                    });

                            if (CheckParamNotEmpty(fromPath, "from must point to a valid file or directory")
                                && CheckParamNotEmpty(match, "match must not be empty")
                                && CheckParamNotEmpty(toPath, "to must point to a valid directory"))
                            {
                                RLog.TraceFormat("Copy from {0} to {1}", fromPath, toPath);
                                IEnumerable<string> copyList = null;
                                if (File.Exists(fromPath))
                                    copyList = new[] { fromPath };
                                else
                                    copyList = Directory.EnumerateFiles(fromPath!, match);

                                if (newName != "")
                                {
                                    newName = expand_vars(newName);
                                    if (copyList.Count() != 1)
                                        throw new Exception("Cannot use name attribute when copying multiple files");
                                    var destFile = Path.Combine(toPath, Path.GetFileName(newName));
                                    File.Copy(fromPath, destFile, true);
                                    RLog.TraceFormat("copied {0}", destFile);
                                }
                                else foreach (var file in copyList)
                                    {
                                        var destFile = Path.Combine(toPath, Path.GetFileName(file));
                                        File.Copy(file, destFile, true);
                                        RLog.TraceFormat("copied {0}", destFile);
                                    }
                            }
                        }
                        break;
                    case "exec":
                        {
                            var app = GetAttribute(actionNode, "app");
                            var folder = GetAttribute(actionNode, "folder", "");
                            if (string.IsNullOrEmpty(folder))
                                folder = path;
                            else
                                folder = PathFinder.FindDirectory(expand_vars(folder), new[] {
                                        new List<string>(new []{path}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            var args = GetAttribute(actionNode, "args", "");
                            var logStdout = GetAttribute<bool>(actionNode, "log-stdout");

                            var requiredExitCodes = GetAttributeAsArray(actionNode, "required-exit-codes",  (av) => av.Split(',').Select(xx => int.Parse(xx)));
                            if (requiredExitCodes == null)
                                requiredExitCodes = new[] { 0 };

                            var appFile = FindTool(app);
                            if (appFile == null)
                                appFile = fexec.FindExePath(app);
                            if (appFile == null)
                                RLog.ErrorFormat("Cannot locate {0}", app);
                            else
                            {
                                var msg = fexec.executeCommand(appFile, expand_vars(args), folder, true, logStdout, requiredExitCodes);
                                RLog.TraceFormat(msg);
                            }
                            break;
                        }
                    default:
                        RLog.ErrorFormat("Unkown artefact tag {0}", action);
                        break;
                }
            }
        }

        private T? GetAttributeAsArray<T>(XElement actionNode, string v, Func<string, T> func)
        {
            var av = GetAttribute(actionNode, v, "");
            if (av != "")
                return func(av);
            return default(T);
        }

        private T GetAttribute<T>(XElement node, string v)
        {
            var val = GetAttribute(node, v, "");
            if (val != "")
                return (T)Convert.ChangeType(val, typeof(T));
            else
                return default(T);
        }

        public static bool IsParameterInvalid(string error, string value, Func<string, bool> invalid)
        {
            if (invalid(value))
            {
                RLog.ErrorFormat(error, value);
                return true;
            }
            return false;
        }
        private static bool CheckParamNotEmpty(string? param, string errorMessage)
        {
            if (String.IsNullOrEmpty(param))
            {
                RLog.ErrorFormat(errorMessage);
                return false;
            }
            return true;
        }
        private string? FindTool(string? app)
        {
            var file = app;
            var rv = PathFinder.FindFile(file, 
                                            new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                            });
            if (rv == null)
            {
                rv = PathFinder.FindFile(file, ToolsDirectories.Select(xx => new List<string>(new[] { xx.FullName })).ToArray());
            }
            return rv;                       
        }

        private string? AddFolder(string? directory)
        {
            if (directory != null)
            {
                if (directory.Contains("*"))
                    Artefacts.Add(new Artefact(expand_vars(directory)));
                else
                {
                    directory = PathFinder.FindDirectory(expand_vars(directory),
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

            return directory;
        }

        private string? AddFile(string? srcFile)
        {
            var file = srcFile;
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
                        RLog.ErrorFormat("Cannot locate file artefact {0}", srcFile);
                }
            }

            return file;
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

        private void processAttributes(XElement node, string atttributes, Action<string?> p)
        {
            foreach (var v in atttributes.Split(','))
            {
                var avs = node.Attributes(v);
                foreach (var av in avs)
                    p(av.Value);
            }
        }

        private string? GetAttribute(XElement node, string v, string? defaultValue=null)
        {
            var rv = node.Attribute(v);
            if (rv == null)
            {
                if (defaultValue == null)
                    RLog.ErrorFormat("Cannot locate attribute {0}", v);
                return defaultValue;
            }
            return rv.Value;
        }

        void GetVersion()
        {
            var json = GitVersion.GetJsonForDirectory(Root);
            var Info = GitVersion.FromJson(json);
            if (Info != null)
            {
                addvar("GITVERSION.JSON", json);
                addvar("VERSION", Info.NuGetVersionV2);
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
            if (rv == null)
                return "";

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
                            foreach (var file in Artefacts.SelectMany(xx => xx.GetFiles()).Distinct())
                            {
                                var archiveName = file.GetArchiveName();

                                RLog.TraceFormat(String.Format("Adding {0}", archiveName));

                                ZipArchiveEntry entry = archive.CreateEntry(archiveName, CompressionLevel.Optimal);
                                using (var infile = File.OpenRead(file.Name))
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