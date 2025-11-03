using Microsoft.VisualBasic;
using rjtool;
using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using static ReleaseBuilder.PublishTarget;

namespace ReleaseBuilder
{
    public class ReleaseBuilder
    {
        public List<DirectoryInfo> ToolsDirectories;
        public List<Artefact> Artefacts = new List<Artefact>();
        public string PublishType = "live";
        public string Root;
        private Dictionary<string, string> vars = new Dictionary<string, string>();
        public bool IsValid => ConfigFile != null;

        public Dictionary<string, PublishTarget> Targets { get; private set; } = new Dictionary<string, PublishTarget>();
        public string TargetName { get; private set; }
        public bool NoBuild { get; }
        public Dictionary<string, bool> Modules { get; private set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public FileInfo? ConfigFile { get; }

        public ReleaseBuilder(DirectoryInfo? root, FileInfo? configFile, string? target, IEnumerable<DirectoryInfo> toolsdir, bool nobuild)
        {
            ToolsDirectories = new List<DirectoryInfo>();
            if (toolsdir != null)
                ToolsDirectories.AddRange(toolsdir);

            if (Directory.Exists(AppDomain.CurrentDomain.BaseDirectory))
                ToolsDirectories.Add(new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory));
            if (ToolsDirectories.Any())
                RLog.TraceFormat("Tools directories {0}", string.Join(",", ToolsDirectories.Select(xx => xx.FullName)));
            if (root != null)
                Root = root.FullName;
            else
                Root = Directory.GetCurrentDirectory();

            NoBuild = nobuild;
            if (target != null)
                PublishType = target;

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
            }
            else RLog.ErrorFormat("Could not locate config file");
            ConfigFile = configFile;
        }
        public bool Valid => ConfigFile != null && Built;
        private bool Built { get; set; }
        public void Build()
        {
            addvar("TYPE", PublishType);
            addvar("PUBLISHROOT", Root);

            RLog.TraceFormat("Using config file {0}", ConfigFile.FullName);

            GetVersion();

            RLog.InfoFormat("Version {0}", vars["SemVer"]);

            if (LoadConfigFromXml(ConfigFile, PublishType, NoBuild))
            {

                if (!Targets.ContainsKey(PublishType))
                    RLog.InfoFormat("No target config for {0}", PublishType);

                RLog.InfoFormat("Publish config target: {0}", PublishType);
                Built = true;
            }
        }
        private bool LoadConfigFromXml(FileInfo configFile, string target, bool nobuild)
        {
            var doc = XDocument.Load(configFile.FullName);
            XPathNavigator navigator = doc.CreateNavigator();
            foreach (XElement node in doc.Root.Nodes().OfType<XElement>())
            {
                if (node.Name == "Name")
                {
                    TargetName = node.Value;
                    if (Modules.Any() && !Modules.Any(xx=> TargetName.ToLower().Contains(xx.Key.ToLower())))
                    {
                        RLog.InfoFormat("Not building {0} because not in modules", TargetName);
                        return false;
                    }
                }
                if (node.Name == "Target")
                {
                    //     <Target name="test" path="$SYNC_MSOS_TEST" archive="7z a -r" />
                    var name = node.Attribute("name");
                    if (name == null)
                    {
                        LogForNodeWithDetails(LogMessageLevel.Error, node, "Target Name missing");
                        continue;
                    }
                    Targets[name.Value] = new PublishTarget(name.Value, expand_vars(node.Attribute("path")));
                    var av = GetAttribute(node, "archive-version", "");
                    if (!string.IsNullOrEmpty(av))
                    {
                        av = Transform(av, "");
                        Targets[name.Value].Version = expand_vars(av);
                        addvar("TargetVersion", av);
                    }
                    var type = GetAttribute(node, "type", "zip");
                    if (type == "zip")
                        Targets[name.Value].Type = TargetTypeEnum.ZipFile;
                    else if (type == "folder")
                        Targets[name.Value].Type = TargetTypeEnum.LocalFolder;
                    else
                        RLog.ErrorFormat("Uknown target type {0}", type);
                    if (name.Value.Equals(target, StringComparison.InvariantCultureIgnoreCase))
                    {
                        addvar("TARGETPATH", Targets[name.Value].Path.FullName);

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
                    var name = GetFilePathAttribute(node, "name");
                    var versionName = GetAttribute<string>(node, "name-version");
                    var path = GetFilePathAttribute(node, "path");
                    var version = GetAttribute(node, "version", "latest");
                    if (name != null && path != null)
                    {
                        var matches = Directory.EnumerateDirectories(Root, path)
                                .Select(xx => new DirectoryInfo(xx));
                        if (matches == null || !matches.Any())
                        {
                            LogForNodeWithDetails(LogMessageLevel.Error, node, "{0} no folders matching {1}", name, path);
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
                            if (!string.IsNullOrEmpty(versionName) && folderName.Count(xx => xx == '.') > 1)
                            {
                                var rv = Regex.Match(folderName, @"\d+.+\d");
                                if (rv != null)
                                    vars[versionName] = rv.Value;
                                RLog.TraceFormat("{0} = {1}", versionName, rv.Value);
                            }
                        }
                    }
                }
                if (node.Name == "Artefacts")
                {
                    var artefactFolder = GetAttribute(node, "folder", "");
                    
                    if (When(node))
                    {
                        if (!string.IsNullOrEmpty(artefactFolder))
                        {
                            artefactFolder = expand_vars(artefactFolder);
                            artefactFolder = PathFinder.FindDirectory(artefactFolder,
                             new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                             });

                        }

                        foreach (XElement child in node.Elements())
                        {
                            switch (child.Name.ToString().ToLower())
                            {
                                case "file":
                                    {
                                        var file = expand_vars(child.Value);
                                        var fileFolder = GetAttribute(child, "folder", "");
                                        var newname = expand_vars(GetAttribute(child, "newname", ""));
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
                                    LogForNodeWithDetails(LogMessageLevel.Error, child, "Unknown artefact type {0}", child.Name);
                                    break;
                            }
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
                if (node.Name == "ReleaseBuilder")
                {
                    releaseBuilder(node);
                }
            }
            return true;
        }

        private bool When(XElement node)
        {
            var whenAttribute = GetAttribute(node, "active", "");
            if (!string.IsNullOrEmpty(whenAttribute))
            {
                if (Transform(whenAttribute, "") == "")
                {
                    LogForNodeWithDetails(LogMessageLevel.Trace, node, "Not processing node because of active attribute {0}", whenAttribute);
                    return false;
                }
            }
            return true;
        }

        private void releaseBuilder(XElement node)
        {
            var folderAttribute = expand_vars(GetAttribute(node, "folder", ""));

            var noBuildAttribute = GetAttribute(node, "nobuild", NoBuild);
            var processAttribute = GetAttribute(node, "process", false);
            var fromPath = PathFinder.FindDirectory(folderAttribute, new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()})
                    });

            var fileAttribute = expand_vars(GetAttribute(node, "file", ""));
            var nameAttribute = GetAttribute(node, "name", "");
            if (!string.IsNullOrEmpty(nameAttribute))
            {
                if (Modules.Any() && !Modules.Any(xx => nameAttribute.ToLower().Contains(xx.Key.ToLower())))
                {
                    RLog.InfoFormat("Not building {0} because not in modules list", nameAttribute);
                    return;
                }
                var newFileAttribute = "ReleaseConfig" + nameAttribute + ".xml";
                if (string.IsNullOrEmpty(fileAttribute))
                {
                    if (null != PathFinder.FindFile(newFileAttribute, new[] { new List<string>(new[] { fromPath }), }))
                    {
                        RLog.DebugFormat("No name specified and {0} attribute sets file {1} -> {2}", nameAttribute, fileAttribute, newFileAttribute);
                        fileAttribute = newFileAttribute;
                    }
                }
            }
            if (string.IsNullOrEmpty(fileAttribute))
                fileAttribute = "ReleaseConfig.xml";


            var fromFile = PathFinder.FindFile(fileAttribute, new[] {
                                        new List<string>(new []{fromPath}),
                                        });
            if (noBuildAttribute && !processAttribute)
                LogForNodeWithDetails(LogMessageLevel.Error, node, "nobuild cannot be true if process is false as no actions will result");

            if (CheckParamNotEmpty(fromPath, String.Format("ReleaseBuilder: folder is required and must point to a valid directory ({0})", folderAttribute))
            && CheckParamNotEmpty(fileAttribute, String.Format("ReleaseBuilder: file is required and must point to a valid directory {0}", fileAttribute)))
            {
                RLog.InfoFormat("Build release from {0}", fromFile);
                var currentFolder = Directory.GetCurrentDirectory();

                try
                {
                    //                public ReleaseBuilder(DirectoryInfo? root, FileInfo? configFile, string? target, IEnumerable<DirectoryInfo> toolsdir, bool nobuild)
                    var newRoot = new DirectoryInfo(fromPath);
                    Directory.SetCurrentDirectory(newRoot.FullName);
                    var newConfigFile = new FileInfo(fromFile);
                    var rb = new ReleaseBuilder(newRoot, newConfigFile, PublishType, ToolsDirectories, noBuildAttribute);
                    rb.Build();
                    if (processAttribute)
                        rb.Process();
                }
                catch (Exception ex)
                {
                    LogForNodeWithDetails(LogMessageLevel.Error, node, ex.Message);
                }
                finally
                {
                    Directory.SetCurrentDirectory(currentFolder);
                }
                RLog.InfoFormat("Finished building {0}", fromFile);
            }
        }

        private void processElements(XElement node, string name, Action<XElement> processNode)
        {
            foreach (var v in node.Elements(name))
                processNode(v);
        }

        private void processBuild(XElement artefactsNode, string path)
        {
            var newPathAttribute = GetAttribute(artefactsNode, "folder", "");

            foreach (XElement actionNode in artefactsNode.Elements())
            {
                var action = actionNode.Name.ToString().ToLower();
                switch (action)
                {
                    case "clean":
                        {
                            var cleanFolder = GetAttribute(actionNode, "folder", "");
                            var cleanFolders = GetAttribute(actionNode, "include-folders", false);
                            var cleanPattern = GetAttribute(actionNode, "match", "*.*");
                            if (CheckParamNotEmpty(cleanFolder, "Folder to clean required"))
                            {
                                cleanFolder = PathFinder.FindDirectory(expand_vars(cleanFolder), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                    });
                                if (CheckParamNotEmpty(cleanFolder, "folder to clean must be found"))
                                {
                                    RLog.InfoFormat("Cleaning folder {0}", cleanFolder);
                                    foreach (var toDel in Directory.EnumerateFiles(cleanFolder, cleanPattern, SearchOption.AllDirectories))
                                    {
                                        RLog.TraceFormat("del {0}", toDel);
                                        File.Delete(toDel);
                                    }
                                    if (cleanFolders)
                                    {
                                        foreach (var toDelFolder in Directory.EnumerateDirectories(cleanFolder, cleanPattern, SearchOption.AllDirectories))
                                        {
                                            try
                                            {
                                                Directory.Delete(toDelFolder, true);
                                                RLog.TraceFormat("del {0}", toDelFolder);
                                            }
                                            catch (Exception ex)
                                            {
                                                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Could not delete folder {0}", toDelFolder);
                                            }
                                        }

                                    }
                                }
                            }
                            break;
                        }
                    case "create":
                        {
                            string? file = GetFilenameFromNode(path, newPathAttribute, actionNode);
                            var text = actionNode.Value;
                            text = expand_vars(text).ReplaceLineEndings("\n");
                            File.WriteAllText(file, text);
                            break;
                        }
                    case "xml-edit":
                        {
                            string? file = GetFilenameFromNode(path, newPathAttribute, actionNode);
                            if (!string.IsNullOrEmpty(file))
                            {
                                var xdoc = XDocument.Load(file);
                                var changed = false;
                                var omitDeclaration = actionNode.Attribute("omit-declaration") != null;
                                foreach (XElement editNode in actionNode.Nodes())
                                {
                                    switch (editNode.Name.ToString().ToLower())
                                    {
                                        case "node":
                                            {
                                                var pathSelector = GetFilePathAttribute(editNode, "path");
                                                var nodeAction = GetAttribute<string>(editNode, "action");
                                                var elements = xdoc.XPathSelectElements(pathSelector);
                                                foreach (var element in elements)
                                                {
                                                    var nv = Transform(nodeAction, element.Value);
                                                    if (nv != element.Value)
                                                    {
                                                        element.Value = nv;
                                                        changed = true;
                                                        RLog.TraceFormat("Node {0} value {1}", element.Name, element.Value);
                                                    }
                                                }
                                                break;
                                            }

                                        default:
                                            ThrowErrorForNode(editNode, String.Format("Unknown directive {0}", editNode.Name));
                                            break;
                                    }
                                }
                                if (changed)
                                {
                                    XmlWriterSettings settings = new XmlWriterSettings
                                    {
                                        OmitXmlDeclaration = omitDeclaration,
                                        Indent = true
                                    };

                                    using (XmlWriter writer = XmlWriter.Create(file, settings))
                                    {
                                        xdoc.Save(writer);
                                    }
                                }
                            }
                            break;
                        }
                    case "copy":
                        {
                            var from = GetFilePathAttribute(actionNode, "from");
                            var recursive = GetAttribute(actionNode, "recursive", false);
                            var searchOption = SearchOption.TopDirectoryOnly;

                            // if from points to a file then just copy that.
                            var fromPath = PathFinder.FindFile(expand_vars(from), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

                            if (fromPath == null)
                                fromPath = PathFinder.FindDirectory(expand_vars(from), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            if (recursive)
                            {
                                if (fromPath != null && !Directory.Exists(fromPath))
                                {
                                    LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "{0} must be a directory when using recursive mode", fromPath);
                                    return;
                                }
                                searchOption = SearchOption.AllDirectories;
                            }
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
                                {
                                    copyList = Directory.EnumerateFiles(fromPath!, match, searchOption).ToList();
                                    if (recursive)
                                    {
                                        var folders = copyList.Select(xx => Path.GetDirectoryName(xx)).Distinct().ToList();
                                        foreach (var folder in folders)
                                        {
                                            var srcFolder = folder.Substring(fromPath.Length).Trim("/\\".ToArray());
                                            var destFolder = Path.Combine(toPath, srcFolder);
                                            if (!Directory.Exists(destFolder))
                                            {
                                                RLog.TraceFormat("Creating folder {0}", destFolder);
                                                Directory.CreateDirectory(destFolder);
                                            }
                                        }
                                    }
                                }
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
                                        if (recursive)
                                        {
                                            var srcFolder = Path.GetDirectoryName(file.Substring(fromPath.Length).Trim("/\\".ToArray()));
                                            destFile = Path.Combine(toPath, srcFolder, destFile);
                                        }
                                        else
                                            destFile = Path.Combine(toPath, destFile);
                                        copyFile(file, destFile, transformContents);
                                        RLog.TraceFormat("copied {0}", destFile);
                                    }
                            }
                        }
                        break;
                    case "modify":
                        {
                            var from = expand_vars(GetAttribute<string>(actionNode, "file", ""));

                            // if from points to a file then just copy that.
                            var fromPath = PathFinder.FindFile(from, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

                            var transformContents = actionNode.Elements("transform-content");
                            if (CheckParamNotEmpty(fromPath, "file must point to a valid file"))
                            {
                                if (EditFile(fromPath, transformContents))
                                    RLog.TraceFormat("modified {0}", fromPath);
                            }
                        }
                        break;
                    case "release-builder":
                        {
                            releaseBuilder(actionNode);
                            break;
                        }
                    case "exec":
                        {
                            var app = GetAttribute<string>(actionNode, "app");
                            var folder = GetAttribute(actionNode, "folder", "");
                            if (string.IsNullOrEmpty(folder))
                                folder = path;
                            else
                                folder = PathFinder.FindDirectory(expand_vars(folder), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            var args = GetAttribute(actionNode, "args", "");
                            var logStdout = GetAttribute<bool>(actionNode, "log-stdout");

                            var requiredExitCodes = GetAttributeAsArray(actionNode, "required-exit-codes", (av) => av.Split(',').Select(xx => int.Parse(xx)));
                            if (requiredExitCodes == null)
                                requiredExitCodes = new[] { 0 };

                            var appFile = FindTool(app);
                            if (appFile == null)
                                appFile = PathFinder.FindExePath(app);
                            if (appFile == null)
                                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Cannot locate {0}", app);
                            else
                            {
                                args = expand_vars(args);
                                var msg = fexec.executeCommand(appFile, args, folder, true, logStdout, requiredExitCodes);
                                RLog.TraceFormat(msg);
                            }
                            break;
                        }
                    default:
                        LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Unkown artefact tag {0}", action);
                        break;
                }
            }
        }

        private string GetFilenameFromNode(string path, string? newPathAttribute, XElement actionNode)
        {
            var file = expand_vars(GetFilePathAttribute(actionNode, "file"));
            var filePath = PathFinder.FindFile(file, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
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

            return file;
        }

        private bool EditFile(string fromPath, IEnumerable<XElement> transforms)
        {
            if (transforms != null && transforms.Any())
            {
                var contents = File.ReadAllText(fromPath);
                var originalContents = new string(contents);
                foreach (XElement transformNode in transforms)
                {
                    var transform = transformNode.Attribute("transform");
                    contents = Transform(transform.Value, contents);
                }
                if (originalContents != contents)
                {
                    File.WriteAllText(fromPath, contents);
                    return true;
                }
            }
            return false;
        }
        private void copyFile(string? fromPath, string destFile, IEnumerable<XElement> transforms)
        {
            File.Copy(fromPath, destFile, true);
            if (transforms != null && transforms.Any())
                EditFile(destFile, transforms);
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
        //
        private string GetFilePathAttribute(XElement node, string v)
        {
            var val = GetAttribute(node, v, "");
            if (val != "")
                return val.Replace("\\", "/");
            else
                return default;
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
        public string? FindTool(string? appBase)
        {
            string? rv = null;
            foreach (var file in PathFinder.GetApplicationTargets(appBase))
            {
                rv = PathFinder.FindFile(file,
                                                new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                });
                if (rv == null)
                {
                    rv = PathFinder.FindFile(file, ToolsDirectories.Select(xx => new List<string>(new[] { xx.FullName })).ToArray());
                }
                if (rv != null && PathFinder.CanExecute(rv))
                    return rv;
            }
            return null;
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
                        RLog.DebugFormat("Added {0}: {1}", file, newName);
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
                    case "set":
                        return expand_vars(parts[1]);
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
                    case "when":
                        {
                            argCheck(parts, 4, Transformation);
                            var s1 = expand_vars(parts[1]);
                            var cond = parts[2];
                            var s2 = expand_vars(parts[3]);
                            return compare(s1, cond, s2);
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

        private string compare(string? s1, string cond, string? s2)
        {
            var result = false;
            switch (cond.ToLower())
            {
                case "eq":
                case "==":
                case "=":
                    result = s1 == s2;
                    break;

                case "ne":
                case "!=":
                case "<>":
                    result = s1 != s2;
                    break;
                default:
                    RLog.ErrorFormat("Unknown comparison {0}", cond);
                    break;
            }
            if (result)
                return "1";
            return "";
        }

        private void LogForNodeWithDetails(LogMessageLevel level, XElement node, string message, params object[] args)
        {
            IXmlLineInfo info = node;
            var sb = new StringBuilder();
            if (info.HasLineInfo())
            {
                sb.Append(" Line: ");
                sb.Append(info.LineNumber);
                sb.Append(": ");
                sb.Append(info.LinePosition);
                sb.Append(": ");
            }
            sb.Append(message);
            RLog.Format(level, sb.ToString(), args);
        }
        private void ThrowErrorForNode(XElement node, string message)
        {

            IXmlLineInfo info = node;
            var sb = new StringBuilder();
            sb.Append(message);

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

        private T? GetAttribute<T>(XElement node, string v, T? defaultValue = default(T))
        {
            var rv = node.Attribute(v);
            if (rv == null)
            {
                if (defaultValue == null)
                    LogForNodeWithDetails(LogMessageLevel.Error, node, "Cannot locate attribute {0}", v);
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
                if (!string.IsNullOrEmpty(Info.NuGetVersionV2))
                    addvar("VERSION", Info.NuGetVersionV2);
                else 
                    addvar("VERSION", Info.MajorMinorPatch);

                var iv = new PackedIntegerVersion(Info);
                addvar("IntSemVer", iv.Value);
                var properties = from p in typeof(GitVersion).GetProperties()
                                 where p.PropertyType == typeof(string) &&
                                       p.CanRead &&
                                       p.CanWrite
                                 select p;
                foreach (var property in properties)
                {
                    var val = property.GetValue(Info, null) as string;
                    if (!string.IsNullOrEmpty(val))
                        addvar(property.Name, val);
                }

            }
        }

        private void addvar(string k, string v)
        {
            if (string.IsNullOrEmpty(v))
                RLog.ErrorFormat("{0} is null or empty", k);
            RLog.DebugFormat("{0}={1}", k, v);
            vars[k] = v;
        }

        private void addvar(string k, long? v)
        {
            if (v.HasValue)
                vars[k] = v.Value.ToString();
        }
        public string? expand_vars(XAttribute? att)
        {
            if (att == null)
                return "";
            return expand_vars(att.Value);
        }
        public string? expand_vars(string rv)
        {
            if (rv == null)
                return "";

            rv = Regex.Replace(rv, @"\$(\w+)", match =>
            {
                string variable = match.Groups[1].Value;
                var rv = Environment.GetEnvironmentVariable(variable);
                if (rv == null)
                    throw new Exception("Environment variable not found: " + variable);
                return rv;
            });
            rv = Regex.Replace(rv, "\\~(.*?)\\~", match =>
            {
                string variable = match.Groups[1].Value;
                if (vars.ContainsKey(variable))
                    return vars[variable];
                else
                    throw new Exception("Variable not found: " + variable);
            });

            return rv;
        }


        public int Process()
        {
            if (Targets.Any())
            {
                var files = Artefacts.SelectMany(xx => xx.GetFiles()).Distinct();
                if (!files.Any())
                {
                    RLog.InfoFormat("No artefacts - not building archive");
                    return 0;
                }
                var target = Targets[PublishType];

                var filename = new List<string>
                {
                    TargetName,
                    PublishType,
                    target.GetVersion(vars["SemVer"])
                };
                switch (target.Type)
                {
                    case TargetTypeEnum.ZipFile:
                        CreateZipFileArtefact(files, target, filename);
                        return 0;
                    case TargetTypeEnum.LocalFolder:
                        {
//                            var outputFile = Path.Combine(target.Path.FullName, string.Join("-", filename) + ".zip");

                            foreach (var file in files)
                            {
                                var outputFileName = Path.Combine(target.Path.FullName, file.GetArchiveName());
                                RLog.TraceFormat(String.Format("Copy {0} (from {1})", outputFileName, Path.GetFileName(file.Name)));
                                File.Copy(file.Name, outputFileName, true);
                            }
                            return 0;
                        }
                }
            }
            return 1;
        }
        private static void CreateZipFileArtefact(IEnumerable<FileDetails> files, PublishTarget target, List<string> filename)
        {
            int fileCount = 0;
            var zipFileName = Path.Combine(target.Path.FullName, string.Join("-", filename) + ".zip");
            File.Delete(zipFileName);
            using (FileStream zipToOpen = new FileStream(zipFileName, FileMode.Create))
            {
                using (var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Update))
                {
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

        internal void AddModule(string module)
        {
            Modules[module] = true;
        }
    }
}