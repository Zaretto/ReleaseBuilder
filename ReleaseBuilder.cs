using rjtool;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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

                LoadConfigFromXml(configFile, target, nobuild);

                if (!Targets.ContainsKey(PublishType))
                    RLog.InfoFormat("No target config for {0}", target);

                RLog.InfoFormat("Publish config target: {0}", PublishType);
            }
            else RLog.ErrorFormat("Could not locate config file");
        }

        private void LoadConfigFromXml(FileInfo configFile, string target, bool nobuild)
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
                        av = Transform(av, "");
                        Targets[name.Value].Version = av;
                        addvar("TargetVersion", av);
                    }
                    if (name.Value.Equals(target, StringComparison.InvariantCultureIgnoreCase))
                    {
                        processElements(node, "Set", (n) =>
                        {
                            processAttributes(n, "name", (nn) =>
                            {
                                processAttributes(n, "value", (v) =>
                                {
                                    addvar(nn, v);
                                });
                            });

                        });
                    }
                }
                if (node.Name == "Folder")
                {
                    //    <Folder name="APPPATH" path="MSOS.UWP_*" version="latest" />
                    var name = GetAttribute<string>(node, "name");
                    var versionName = GetAttribute<string>(node, "name-version");
                    var path = GetAttribute<string>(node, "path");
                    var version = GetAttribute(node, "version", "latest");
                    if (name != null && path != null)
                    {
                        var matches = Directory.EnumerateDirectories(Root, path)
                                .Select(xx => new DirectoryInfo(xx));
                        if (matches == null || !matches.Any())
                        {
                            RLog.ErrorFormat("{0} no folders matching {1}", name, path);
                        }
                        else
                        {
                            List<DirectoryInfo> ordered = new List<DirectoryInfo>();
                            switch (version)
                            {
                                case "latest":
                                    ordered = matches.OrderByDescending(xx => xx.CreationTime).ToList();
                                    break;
                                case "oldest":
                                    ordered = matches.OrderBy(xx => xx.CreationTime).ToList();
                                    break;
                                case "last-name":
                                    ordered = matches.OrderByDescending(xx => xx.Name).ToList();
                                    break;

                                default:
                                case "name":
                                    ordered = matches.OrderBy(xx => xx.Name).ToList();
                                    break;

                            }
                            var value = ordered.First().FullName;
                            vars[name] = value;
                            var folderName = Path.GetFileName(value);
                            if (!string.IsNullOrEmpty(versionName) && folderName.Count(xx=>xx=='.') > 1)
                            {
                                var rv = Regex.Match(folderName, @"\d+.+\d");
                                if (rv != null)
                                    vars[versionName] = rv.Value;
                            }
                        }
                    }
                }
                if (node.Name == "Artefacts")
                {
                    var artefactFolder = GetAttribute(node, "folder", "");
                    if (!string.IsNullOrEmpty(artefactFolder))
                    {
                        artefactFolder = expand_vars(artefactFolder);
                        artefactFolder = PathFinder.FindDirectory(artefactFolder,
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
                                    var newname = GetAttribute(child, "newname", "");
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
                                    file = AddFile(GetAttribute<int>(child, "skip-directories-front"), Root, file, newname);
                                }
                                break;
                            case "folder":
                                var folder = AddFolder(GetAttribute<int>(child, "skip-directories-front"), Root, expand_vars(child.Value));
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
                    var rootAttribute = node.Attribute("root");
                    var artefactRoot = Root;
                    if (rootAttribute != null)
                        artefactRoot = rootAttribute.Value;
                    processAttributes(node, "file", (file) =>
                    {
                        file = AddFile(GetAttribute<int>(node, "skip-directories-front"), artefactRoot, file, GetAttribute(node, "newname", ""));
                    });
                    processAttributes(node, "directory,folder", (directory) =>
                    {
                        directory = AddFolder(GetAttribute<int>(node, "skip-directories-front"), artefactRoot, directory);
                    });
                }
            }
        }

        private void processElements(XElement node, string name, Action<XElement> processNode)
        {
            foreach (var v in node.Elements(name))
                processNode(v);
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
                            var file = expand_vars(GetAttribute<string>(actionNode, "file"));
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
                            var from = GetAttribute<string>(actionNode, "from");

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
                            var transform = GetAttribute(actionNode, "transform", "");
                            var transformContents = actionNode.Elements("transform-content");
                            var match = GetAttribute(actionNode, "match", "*.*")!;
                            var toPath = expand_vars(GetAttribute(actionNode, "to", ""));
                            if (!string.IsNullOrEmpty(toPath))
                            {
                                toPath = Path.Combine(path, toPath);
                                if (!Directory.Exists(toPath))
                                {
                                    var newDir = Directory.CreateDirectory(toPath);
                                    if (newDir == null)
                                    {
                                        throw new Exception(string.Format("Failed to create {0} in {1}", toPath, path));
                                    }
                                }
                            }
                            else
                                toPath = path;

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

                                    destFile = Transform(transform, destFile);
                                    copyFile(fromPath, destFile, transformContents);
                                    RLog.TraceFormat("copied {0}", destFile);
                                }
                                else foreach (var file in copyList)
                                    {
                                        var destFile = Transform(transform, Path.GetFileName(file));
                                        destFile = Path.Combine(toPath, destFile);
                                        copyFile(file, destFile, transformContents);
                                        RLog.TraceFormat("copied {0}", destFile);
                                    }
                            }
                        }
                        break;
                    case "exec":
                        {
                            var app = GetAttribute<string>(actionNode, "app");
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
      
        private void copyFile(string? fromPath, string destFile, IEnumerable<XElement> transforms)
        {
            if (transforms != null && transforms.Any())
            {
                var contents = File.ReadAllText(fromPath);
                foreach (XElement transformNode in transforms)
                {
                    var transform = transformNode.Attribute("transform");
                    contents = Transform(transform.Value, contents);
                }
                File.WriteAllText(destFile, contents);
            }
            else
                File.Copy(fromPath, destFile, true);
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

        private string? AddFolder(int skipCount, string root, string? directory)
        {
            if (directory != null)
            {
                if (directory.Contains("*"))
                    Artefacts.Add(new Artefact(skipCount, root, expand_vars(directory)));
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

        private string? AddFile(int skipCount, string root, string? srcFile, string? newName)
        {
            var file = srcFile;
            if (file != null)
            {

                if (file.Contains("*"))
                    Artefacts.Add(new Artefact(skipCount, root, expand_vars(file)));
                else
                {
                    file = PathFinder.FindFile(file,
                            new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                            });
                    if (file != null)
                    {
                        Artefacts.Add(new Artefact(new FileInfo(file), newName));
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

        private string Transform(string? Transformation, string v)
        {
            if (string.IsNullOrEmpty(Transformation))
                return v;
            var parts = Transformation.Split(',');
            if (parts.Length >= 1)
            {
                var method = parts[0];
                switch (method.ToLower())
                {
                    case "getversion":
                        {
                            argCheck(parts, 2, Transformation);
                            v = expand_vars(parts[1]);
                            var vp = v.Split('\\');
                            v = vp.Last();
                            var rv = Regex.Match(v, @"\d+.+\d");
                            if (rv != null)
                                return rv.Value;
                            return Transformation;
                        }


                    case "replace":
                        {
                            argCheck(parts, 3, Transformation);
                            var s1 = expand_vars(parts[1]);
                            var s2 = expand_vars(parts[2]);
                            if (s1 == s2)
                                return v;
                            return v.Replace(s1, s2);
                        }
                    case "regex-replace":
                        {
                            argCheck(parts, 3, Transformation);
                            var s1 = expand_vars(parts[1]);
                            var s2 = expand_vars(parts[2]);
                            return Regex.Replace(v, s1, s2);
                        }
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

        private void Error(XElement node, string v)
        {
            
            IXmlLineInfo info = node;
            var sb = new StringBuilder();
            sb.Append(v);

            if (info.HasLineInfo())
            {
                sb.Append(" Line: ");
                sb.Append(info.LineNumber);
                sb.Append(": ");
                sb.Append(info.LinePosition);
            }
            throw new Exception(sb.ToString());
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

        private T? GetAttribute<T>(XElement node, string v, T? defaultValue=default(T))
        {
            var rv = node.Attribute(v);
            if (rv == null)
            {
                if (defaultValue == null)
                    RLog.ErrorFormat("Cannot locate attribute {0}", v);
                return defaultValue;
            }
            Type returnType = typeof(T);
            return (T)Convert.ChangeType(rv.Value, returnType);
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
                addvar("AssemblySemVer", Info.AssemblySemVer);
                addvar("Major", Info.Major);
                addvar("Minor", Info.Minor);
                addvar("Patch", Info.Patch);
                addvar("PreReleaseTag", Info.PreReleaseTag);
            }
        }

        private void addvar(string k, string v)
        {
            RLog.DebugFormat("{0}={1}", k, v);
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
                            var files = Artefacts.SelectMany(xx => xx.GetFiles()).Distinct();
                            foreach (var file in files)
                            {
                                var archiveName = file.GetArchiveName();
                                var sourceName = Path.GetFileName(file.Name);
                                if (archiveName != sourceName)
                                    RLog.TraceFormat(String.Format("Adding {0} (from {1})", archiveName, sourceName));
                                else
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