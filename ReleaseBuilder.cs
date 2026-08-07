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
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        private VariableStore vars = new VariableStore();
        private TransformEngine _transform;
        public bool IsValid => ConfigFile != null;

        public Dictionary<string, PublishTarget> Targets { get; private set; } = new Dictionary<string, PublishTarget>();
        public string TargetName { get; private set; }
        public bool NoBuild { get; }
        public bool DryRun { get; }
        public bool UseShellExecute { get; }
        public BuildManifest? Manifest { get; private set; }
        public Dictionary<string, bool> Modules { get; private set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public FileInfo? ConfigFile { get; }

        private readonly List<KeyValuePair<string, string>> _initialVars = new();

        public ReleaseBuilder(DirectoryInfo? root, FileInfo? configFile, string? target, IEnumerable<DirectoryInfo> toolsdir, bool nobuild, bool useShellExecute, bool dryRun = false, IEnumerable<KeyValuePair<string, string>>? initialVars = null)
        {
            if (initialVars != null)
                _initialVars.AddRange(initialVars);
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
            DryRun = dryRun;
            UseShellExecute = useShellExecute;
            if (target != null)
                PublishType = target;

            fexec.AddExePath(ToolsDirectories);

            var baseConfig = "ReleaseConfig.xml";
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
            _transform = new TransformEngine(vars);
        }
        public bool Valid => ConfigFile != null && Built;
        private bool Built { get; set; }
        public bool Build()
        {
            // Inherited vars applied first so child's own built-ins override them.
            foreach (var kv in _initialVars)
                addvar(kv.Key, kv.Value);

            addvar("TYPE", PublishType);
            addvar("PUBLISHROOT", Root);

            var (os, arch, runtime) = GetPlatformInfo();
            addvar("OS", os);
            addvar("ARCH", arch);
            addvar("RUNTIME", runtime);

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
            return RLog.ErrorCount == 0;
        }
        private bool LoadConfigFromXml(FileInfo configFile, string target, bool nobuild)
        {
            // SetLineInfo is what makes LogForNodeWithDetails/ThrowErrorForNode able to report
            // where in the config the problem is; without it IXmlLineInfo.HasLineInfo() is false.
            var doc = XDocument.Load(configFile.FullName, LoadOptions.SetLineInfo);
            XPathNavigator navigator = doc.CreateNavigator();
            foreach (XElement node in doc.Root.Nodes().OfType<XElement>())
            {
                if (node.Name == "Name")
                {
                    TargetName = _transform.ExpandVars(node.Value);
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
                    Targets[name.Value] = new PublishTarget(name.Value, _transform.ExpandVars(node.Attribute("path")));
                    var av = GetAttribute(node, "archive-version", "");
                    if (!string.IsNullOrEmpty(av))
                    {
                        av = _transform.ExpandVars(av);
                        Targets[name.Value].Version = av;
                        addvar("TargetVersion", av);
                    }
                    var type = GetAttribute(node, "type", "zip");
                    if (type == "zip")
                        Targets[name.Value].Type = TargetTypeEnum.ZipFile;
                    else if (type == "folder")
                        Targets[name.Value].Type = TargetTypeEnum.LocalFolder;
                    else
                        RLog.ErrorFormat("Unknown target type {0}", type);
                    if (name.Value.Equals(target, StringComparison.InvariantCultureIgnoreCase))
                    {
                        addvar("TARGETPATH", Targets[name.Value].Path.FullName);

                        processElements(node, "Set", (n) =>
                       {
                           processAttributes(n, "name", (nn) =>
                           {
                               var transformAttribute = GetAttribute(n, "transform", "");
                               if (string.IsNullOrEmpty(transformAttribute))
                               {
                                   processAttributes(n, "value", (v) =>
                                   {
                                       addSetVar(n, nn!, _transform.ExpandVars(v)!);
                                   });
                               }
                               else
                                   addSetVar(n, nn!, _transform.Transform(transformAttribute, _transform.ExpandVars(GetAttribute(n, "value", ""))!));
                           });

                       });
                    }
                }
                if (node.Name == "Folder")
                {
                    //    <Folder name="APPPATH" path="MSOS.UWP_*" version="latest" />
                    var name = GetAttribute<string>(node, "name");
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
                            artefactFolder = _transform.ExpandVars(artefactFolder);
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
                                        var file = _transform.ExpandVars(child.Value);
                                        var fileFolder = GetAttribute(child, "folder", "");
                                        var newname = _transform.ExpandVars(GetAttribute(child, "newname", ""));
                                        if (fileFolder != "")
                                        {
                                            var nfolder = PathFinder.FindDirectory(_transform.ExpandVars(fileFolder),
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
                                    var folder = AddFolder(GetAttribute<int>(child, "skip-directories-front"), Root, _transform.ExpandVars(child.Value));
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
                        artefactRoot = PathFinder.NormalizeSeparators(rootAttribute.Value);
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
                if (_transform.Transform(whenAttribute, "") == "")
                {
                    LogForNodeWithDetails(LogMessageLevel.Trace, node, "Not processing node because of active attribute {0}", whenAttribute);
                    return false;
                }
            }
            return true;
        }

        private void releaseBuilder(XElement node)
        {
            if (!When(node))
                return;
            var folderAttribute = _transform.ExpandVars(GetAttribute(node, "folder", ""));

            var noBuildAttribute = GetAttribute(node, "nobuild", NoBuild);
            var processAttribute = GetAttribute(node, "process", false);
            var fromPath = PathFinder.FindDirectory(folderAttribute, new[] {
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()})
                    });

            var fileAttribute = _transform.ExpandVars(GetAttribute(node, "file", ""));
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
                // Build initial vars for child: parent's full store (inherited) then
                // <Set> children (can add new vars or override inherited ones).
                var childInitialVars = vars.All
                    .Concat(ReadSetElements(node))
                    .ToList();

                RLog.InfoFormat("Build release from {0}", fromFile);
                var currentFolder = Directory.GetCurrentDirectory();

                try
                {
                    var newRoot = new DirectoryInfo(fromPath);
                    Directory.SetCurrentDirectory(newRoot.FullName);
                    var newConfigFile = new FileInfo(fromFile);
                    var rb = new ReleaseBuilder(newRoot, newConfigFile, PublishType, ToolsDirectories, noBuildAttribute, UseShellExecute, DryRun, childInitialVars);
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

        private IEnumerable<KeyValuePair<string, string>> ReadSetElements(XElement node)
        {
            foreach (var n in node.Elements("Set"))
            {
                var name = GetAttribute(n, "name", "");
                var value = _transform.ExpandVars(GetAttribute(n, "value", ""))!;
                var transformAttribute = GetAttribute(n, "transform", "");
                if (!string.IsNullOrEmpty(transformAttribute))
                    value = _transform.Transform(transformAttribute, value);
                if (GetAttribute(n, "secret", false))
                    RLog.RegisterSecret(value);
                if (!string.IsNullOrEmpty(name))
                    yield return new KeyValuePair<string, string>(name, value);
            }
        }

        /// <summary>
        /// Sets a variable from a &lt;Set&gt; element. A secret value is registered for log
        /// redaction before it is stored, so it never reaches the log in the first place.
        /// </summary>
        private void addSetVar(XElement setNode, string name, string value)
        {
            if (GetAttribute(setNode, "secret", false))
                RLog.RegisterSecret(value);
            addvar(name, value);
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
                // active= was previously only honoured on Artefacts and ReleaseBuilder; it was
                // silently ignored here. Reading it is additive — nothing sets it on a build
                // action today because it did nothing.
                if (!When(actionNode))
                    continue;

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
                                cleanFolder = PathFinder.FindDirectory(_transform.ExpandVars(cleanFolder), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                    }, required: false);
                                if (string.IsNullOrEmpty(cleanFolder))
                                {
                                    RLog.WarnFormat("clean: folder not found, skipping — {0}", GetAttribute(actionNode, "folder", ""));
                                    break;
                                }
                                RLog.InfoFormat("Cleaning folder {0}", cleanFolder);
                                foreach (var toDel in Directory.EnumerateFiles(cleanFolder, cleanPattern, SearchOption.AllDirectories))
                                {
                                    if (DryRun)
                                        RLog.InfoFormat("[dry-run] Would delete: {0}", toDel);
                                    else
                                    {
                                        RLog.TraceFormat("del {0}", toDel);
                                        File.Delete(toDel);
                                    }
                                }
                                if (cleanFolders)
                                {
                                    foreach (var toDelFolder in Directory.EnumerateDirectories(cleanFolder, cleanPattern, SearchOption.AllDirectories))
                                    {
                                        try
                                        {
                                            if (DryRun)
                                                RLog.InfoFormat("[dry-run] Would delete folder: {0}", toDelFolder);
                                            else
                                            {
                                                Directory.Delete(toDelFolder, true);
                                                RLog.TraceFormat("del {0}", toDelFolder);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Could not delete folder {0}", toDelFolder);
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
                            text = _transform.ExpandVars(text).ReplaceLineEndings("\n");
                            if (DryRun)
                                RLog.InfoFormat("[dry-run] Would create: {0}", file);
                            else
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
                                        case "attribute":
                                            {
                                                // XPath selector — must NOT be separator-normalised (a '\' inside
                                                // a predicate string literal is data, not a path separator).
                                                var pathSelector = GetAttribute<string>(editNode, "path");
                                                var nodeAction = GetAttribute<string>(editNode, "action");
                                                // XPathEvaluate returns IEnumerable<object> (not IEnumerable<XObject>)
                                                // for node-set results; items are XAttribute or XElement at runtime.
                                                // Using XPathEvaluate (rather than XPathSelectElements) means both
                                                // <node> and <attribute> handle element- and attribute-selecting
                                                // XPaths, including predicates like @*[local-name()='x'].
                                                if (xdoc.XPathEvaluate(pathSelector) is IEnumerable<object> items)
                                                {
                                                    foreach (var item in items)
                                                    {
                                                        if (item is XAttribute attr)
                                                        {
                                                            var nv = _transform.Transform(nodeAction, attr.Value);
                                                            if (nv != attr.Value)
                                                            {
                                                                attr.Value = nv;
                                                                changed = true;
                                                                RLog.TraceFormat("Attribute {0} value {1}", attr.Name, attr.Value);
                                                            }
                                                        }
                                                        else if (item is XElement el)
                                                        {
                                                            var nv = _transform.Transform(nodeAction, el.Value);
                                                            if (nv != el.Value)
                                                            {
                                                                el.Value = nv;
                                                                changed = true;
                                                                RLog.TraceFormat("Node {0} value {1}", el.Name, el.Value);
                                                            }
                                                        }
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
                                    if (DryRun)
                                    {
                                        RLog.InfoFormat("[dry-run] Would save xml: {0}", file);
                                    }
                                    else
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
                            }
                            break;
                        }
                    case "copy":
                        {
                            var from = GetFilePathAttribute(actionNode, "from");
                            var recursive = GetAttribute(actionNode, "recursive", false);
                            var searchOption = SearchOption.TopDirectoryOnly;

                            // if from points to a file then just copy that.
                            var fromPath = PathFinder.FindFile(_transform.ExpandVars(from), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

                            if (fromPath == null)
                                fromPath = PathFinder.FindDirectory(_transform.ExpandVars(from), new[] {
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
                            var toPath = _transform.ExpandVars(GetFilePathAttribute(actionNode, "to"));
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
                                    newName = _transform.ExpandVars(newName);
                                    if (copyList.Count() != 1)
                                        throw new Exception("Cannot use name attribute when copying multiple files");
                                    var destFile = Path.Combine(toPath, Path.GetFileName(newName));

                                    destFile = _transform.Transform(transform, destFile);
                                    if (DryRun)
                                        RLog.InfoFormat("[dry-run] Would copy: {0} -> {1}", fromPath, destFile);
                                    else
                                    {
                                        copyFile(fromPath, destFile, transformContents);
                                        RLog.TraceFormat("copied {0}", destFile);
                                    }
                                }
                                else foreach (var file in copyList)
                                    {
                                        var destFile = _transform.Transform(transform, Path.GetFileName(file));
                                        if (recursive)
                                        {
                                            var srcFolder = Path.GetDirectoryName(file.Substring(fromPath.Length).Trim("/\\".ToArray()));
                                            destFile = Path.Combine(toPath, srcFolder, destFile);
                                        }
                                        else
                                            destFile = Path.Combine(toPath, destFile);
                                        if (DryRun)
                                            RLog.InfoFormat("[dry-run] Would copy: {0} -> {1}", file, destFile);
                                        else
                                        {
                                            copyFile(file, destFile, transformContents);
                                            RLog.TraceFormat("copied {0}", destFile);
                                        }
                                    }
                            }
                        }
                        break;
                    case "modify":
                        {
                            var from = _transform.ExpandVars(GetAttribute<string>(actionNode, "file", ""));

                            // if from points to a file then just copy that.
                            var fromPath = PathFinder.FindFile(from, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

                            var transformContents = actionNode.Elements("transform-content");
                            if (CheckParamNotEmpty(fromPath, "file must point to a valid file"))
                            {
                                if (DryRun)
                                    RLog.InfoFormat("[dry-run] Would modify: {0}", fromPath);
                                else if (EditFile(fromPath, transformContents))
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
                                folder = PathFinder.FindDirectory(_transform.ExpandVars(folder), new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                            var args = GetAttribute(actionNode, "args", "");

                            if (actionNode.Attribute("log-stdout") != null)
                                RLog.ErrorFormat("attribute log-stdout not supported, use --shell-exec on the command line");

                            var requiredExitCodes = GetAttributeAsArray(actionNode, "required-exit-codes", (av) => av.Split(',').Select(xx => int.Parse(xx)));
                            if (requiredExitCodes == null)
                                requiredExitCodes = new[] { 0 };

                            var appFile = PathFinder.FindTool(app, Root, ToolsDirectories);
                            if (appFile == null)
                                appFile = PathFinder.FindExePath(app);
                            if (appFile == null)
                                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Cannot locate {0}", app);
                            else
                            {
                                args = _transform.ExpandVars(args);
                                if (DryRun)
                                    RLog.InfoFormat("[dry-run] Would exec: {0} {1}", appFile, args);
                                else
                                {
                                    var msg = fexec.executeCommand(appFile, args, folder, true, UseShellExecute, requiredExitCodes);
                                    RLog.TraceFormat(msg);
                                }
                            }
                            break;
                        }
                    case "check-certificate":
                        {
                            checkCertificate(actionNode, path, newPathAttribute);
                            break;
                        }
                    case "create-certificate":
                        {
                            createCertificate(actionNode, path, newPathAttribute);
                            break;
                        }
                    default:
                        LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "Unkown artefact tag {0}", action);
                        break;
                }
            }
        }

        private void checkCertificate(XElement actionNode, string path, string? newPathAttribute)
        {
            var file = _transform.ExpandVars(GetAttribute<string>(actionNode, "file", ""));
            if (!CheckParamNotEmpty(file, "check-certificate: file is required"))
                return;

            var certPath = PathFinder.FindFile(file, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });

            var onFail = GetAttribute(actionNode, "on-fail", "stop");
            if (!TryParseFailurePolicy(onFail, out var policy))
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode,
                    "check-certificate: unknown on-fail '{0}' (expected stop, error or warn)", onFail!);
                return;
            }
            var prefix = GetAttribute(actionNode, "prefix", "CERT");

            if (string.IsNullOrEmpty(certPath))
            {
                addvar(prefix + "_VALID", "0");
                certificateCheckFailed(actionNode, policy, file!, new[] { "certificate not found" });
                return;
            }

            // A check that cannot run leaves the build unverified, so a malformed attribute is
            // treated the same as a failed criterion — under the default policy it stops the build.
            if (!TryResolveRequiredEkus(actionNode, out var ekus, out var ekuError))
            {
                addvar(prefix + "_VALID", "0");
                certificateCheckFailed(actionNode, policy, certPath, new[] { ekuError! });
                return;
            }

            var options = new CertificateCheckOptions
            {
                File = certPath,
                Password = ResolvePassword(actionNode, "password"),
                MinDays = GetAttribute(actionNode, "min-days", 0),
                RequirePrivateKey = GetAttribute(actionNode, "private-key", true),
                RequiredEkus = ekus,
                MinKeySize = GetAttribute(actionNode, "min-key-size", 0),
                RequireTrustedChain = GetAttribute(actionNode, "trusted", false),
                CheckRevocation = GetAttribute(actionNode, "revocation", false),
                ExpectedThumbprint = NullIfEmpty(GetAttribute(actionNode, "thumbprint", "")),
                ExpectedSubject = NullIfEmpty(_transform.ExpandVars(GetAttribute(actionNode, "subject", ""))),
            };

            var keyAlgorithm = GetAttribute(actionNode, "key-algorithm", "");
            if (!string.IsNullOrEmpty(keyAlgorithm))
            {
                if (!CertificateGenerator.IsValidKeyAlgorithm(keyAlgorithm))
                {
                    addvar(prefix + "_VALID", "0");
                    certificateCheckFailed(actionNode, policy, certPath,
                        new[] { $"unsupported key-algorithm '{keyAlgorithm}' (expected rsa or ecdsa)" });
                    return;
                }
                options.RequiredKeyAlgorithm = keyAlgorithm;
            }

            var trustRoots = new List<X509Certificate2>();
            try
            {
                foreach (var rootNode in actionNode.Elements("trust-root"))
                {
                    var rootFile = _transform.ExpandVars(GetAttribute<string>(rootNode, "file", ""));
                    var rootPath = PathFinder.FindFile(rootFile, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                    if (string.IsNullOrEmpty(rootPath))
                    {
                        addvar(prefix + "_VALID", "0");
                        certificateCheckFailed(rootNode, policy, certPath, new[] { $"trust-root not found — {rootFile}" });
                        return;
                    }
                    try
                    {
                        trustRoots.Add(CertificateChecker.LoadTrustRoot(File.ReadAllBytes(rootPath), ResolvePassword(rootNode, "password")));
                    }
                    catch (CryptographicException)
                    {
                        addvar(prefix + "_VALID", "0");
                        certificateCheckFailed(rootNode, policy, certPath, new[] { $"cannot read trust-root {rootPath} as a certificate" });
                        return;
                    }
                }
                options.TrustRoots = trustRoots;

                // Deliberately runs for real under --dry-run: it changes nothing, and verifying
                // the certificate is present and in date is the most useful thing a dry run can do.
                var result = CertificateChecker.Check(options);
                exportCertificateVars(prefix, result);

                foreach (var warning in result.Warnings)
                    LogForNodeWithDetails(LogMessageLevel.Warn, actionNode, "check-certificate {0}: {1}", certPath, warning);

                if (result.Ok)
                {
                    RLog.InfoFormat("Certificate {0} is valid — {1}, expires {2} ({3} days)",
                        certPath, result.Subject, CertificateChecker.Format(result.NotAfterUtc), result.DaysRemaining);
                }
                else
                {
                    certificateCheckFailed(actionNode, policy, certPath, result.Failures);
                }
            }
            finally
            {
                foreach (var root in trustRoots)
                    root.Dispose();
            }
        }

        private void createCertificate(XElement actionNode, string path, string? newPathAttribute)
        {
            var subject = _transform.ExpandVars(GetAttribute(actionNode, "subject", ""));
            if (!CheckParamNotEmpty(subject, "create-certificate: subject is required"))
                return;
            if (!CheckParamNotEmpty(GetAttribute(actionNode, "file", ""), "create-certificate: file is required"))
                return;

            var purposeName = GetAttribute(actionNode, "purpose", CertificatePurpose.Default);
            var purpose = CertificatePurpose.Resolve(purposeName);
            if (purpose == null)
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unknown purpose '{0}' (expected one of {1})",
                    purposeName!, string.Join(", ", CertificatePurpose.Names));
                return;
            }

            var options = new CertificateGenerateOptions
            {
                Subject = subject!,
                Ekus = purpose.Ekus,
                KeyUsage = purpose.KeyUsage,
                Ca = purpose.Ca,
                Days = GetAttribute(actionNode, "days", CertificateGenerateOptions.DefaultDays),
                Password = ResolvePassword(actionNode, "password"),
                HashAlgorithm = GetAttribute(actionNode, "hash", "sha256")!,
                KeyAlgorithm = GetAttribute(actionNode, "key", "rsa")!,
            };

            // eku= and key-usage= override whatever the purpose implied.
            var ekuAttribute = GetAttribute(actionNode, "eku", "");
            if (!string.IsNullOrEmpty(ekuAttribute))
            {
                if (!CertificatePurpose.TryParseEkuList(ekuAttribute, out var ekus, out var invalidEku))
                {
                    LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unknown eku '{0}'", invalidEku!);
                    return;
                }
                options.Ekus = ekus;
            }

            var keyUsageAttribute = GetAttribute(actionNode, "key-usage", "");
            if (!string.IsNullOrEmpty(keyUsageAttribute))
            {
                if (!CertificatePurpose.TryParseKeyUsage(keyUsageAttribute, out var keyUsage, out var invalidUsage))
                {
                    LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unknown key-usage '{0}'", invalidUsage!);
                    return;
                }
                options.KeyUsage = keyUsage;
            }

            if (!CertificateGenerator.IsValidKeyAlgorithm(options.KeyAlgorithm))
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unsupported key '{0}' (expected rsa or ecdsa)", options.KeyAlgorithm);
                return;
            }

            options.KeySize = GetAttribute(actionNode, "key-size",
                CertificateGenerator.IsEcdsa(options.KeyAlgorithm)
                    ? CertificateGenerateOptions.DefaultEcdsaKeySize
                    : CertificateGenerateOptions.DefaultRsaKeySize);

            if (!CertificateGenerator.IsValidKeySize(options.KeyAlgorithm, options.KeySize))
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unsupported key-size {0} for {1}", options.KeySize, options.KeyAlgorithm);
                return;
            }

            var hash = GetAttribute(actionNode, "hash", "sha256");
            if (hash != "sha256" && hash != "sha384" && hash != "sha512")
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: unsupported hash '{0}' (expected sha256, sha384 or sha512)", hash!);
                return;
            }

            var notBefore = GetAttribute(actionNode, "not-before", "");
            if (!string.IsNullOrEmpty(notBefore))
            {
                if (!DateTimeOffset.TryParse(notBefore, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: cannot parse not-before '{0}' as a date", notBefore!);
                    return;
                }
                options.NotBefore = parsed;
            }

            if (options.Days <= 0)
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: days must be greater than zero");
                return;
            }

            var dnsNames = _transform.ExpandVars(GetAttribute(actionNode, "dns-names", ""));
            if (!string.IsNullOrEmpty(dnsNames))
                options.DnsNames = dnsNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string file;
            try
            {
                file = GetFilenameFromNode(path, newPathAttribute, actionNode);
            }
            catch (Exception ex)
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: {0}", ex.Message);
                return;
            }

            var overwrite = GetAttribute(actionNode, "overwrite", false);
            if (!overwrite && File.Exists(file))
            {
                LogForNodeWithDetails(LogMessageLevel.Error, actionNode,
                    "create-certificate: {0} already exists — set overwrite=\"true\" to replace it", file);
                return;
            }

            var issuerFile = _transform.ExpandVars(GetAttribute(actionNode, "issuer-file", ""));
            X509Certificate2? issuer = null;
            try
            {
                if (!string.IsNullOrEmpty(issuerFile))
                {
                    var issuerPath = PathFinder.FindFile(issuerFile, new[] {
                                        new List<string>(new []{path,newPathAttribute}),
                                        new List<string>(new []{Root}),
                                        new List<string>(new []{Directory.GetCurrentDirectory()}),
                                     });
                    if (string.IsNullOrEmpty(issuerPath))
                    {
                        LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: issuer-file not found — {0}", issuerFile);
                        return;
                    }
                    try
                    {
                        issuer = CertificateChecker.Load(File.ReadAllBytes(issuerPath), ResolvePassword(actionNode, "issuer-password"));
                    }
                    catch (CryptographicException)
                    {
                        LogForNodeWithDetails(LogMessageLevel.Error, actionNode,
                            "create-certificate: cannot open issuer-file {0} — wrong issuer-password, or not a PKCS#12 archive", issuerPath);
                        return;
                    }
                    if (!issuer.HasPrivateKey)
                    {
                        LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: issuer-file {0} has no private key", issuerPath);
                        return;
                    }
                    options.Issuer = issuer;
                }

                if (issuer == null && purpose.WarnIfSelfSigned)
                    RLog.WarnFormat("create-certificate: {0} is self-signed and will not be trusted outside machines that have been told to trust it — suitable for test and internal use only", file);

                X509Certificate2 cert;
                byte[] pfx;
                try
                {
                    (cert, pfx) = CertificateGenerator.Create(options);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    LogForNodeWithDetails(LogMessageLevel.Error, actionNode, "create-certificate: {0}", ex.Message);
                    return;
                }

                using (cert)
                {
                    // Generated even under --dry-run so that variables downstream of this action
                    // still resolve; only the write is skipped.
                    if (DryRun)
                        RLog.InfoFormat("[dry-run] Would create certificate: {0} (subject {1})", file, options.Subject);
                    else
                    {
                        File.WriteAllBytes(file, pfx);
                        RLog.InfoFormat("Created certificate {0} — {1}, expires {2}",
                            file, cert.Subject, CertificateChecker.Format(cert.NotAfter.ToUniversalTime()));
                    }

                    // Read straight off the in-memory certificate rather than the file, so the
                    // variables resolve identically under --dry-run where nothing was written.
                    var prefix = GetAttribute(actionNode, "prefix", "CERT");
                    exportCertificateVars(prefix, cert);
                    addvar(prefix + "_FILE", file);
                }
            }
            finally
            {
                issuer?.Dispose();
            }
        }

        /// <summary>
        /// What a failed &lt;check-certificate&gt; does to the build.
        /// </summary>
        private enum CertificateFailurePolicy
        {
            /// <summary>Abort the build immediately. The default: a pre-flight check that lets
            /// the build continue is no check at all.</summary>
            Stop,
            /// <summary>Log an error and carry on, so the run reports every problem and exits 2
            /// at the end — the continue-and-report behaviour the other actions use.</summary>
            Error,
            /// <summary>Log a warning and carry on; the exit code is unaffected.</summary>
            Warn,
        }

        private static bool TryParseFailurePolicy(string? value, out CertificateFailurePolicy policy)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "":
                case "stop": policy = CertificateFailurePolicy.Stop; return true;
                case "error": policy = CertificateFailurePolicy.Error; return true;
                case "warn": policy = CertificateFailurePolicy.Warn; return true;
                default: policy = CertificateFailurePolicy.Stop; return false;
            }
        }

        /// <summary>
        /// Reports every failed criterion, then applies the on-fail policy. Under Stop the build
        /// is aborted here rather than at the end of the run, so nothing downstream gets to use a
        /// certificate that failed its check.
        /// </summary>
        private void certificateCheckFailed(XElement node, CertificateFailurePolicy policy, string file, IReadOnlyList<string> failures)
        {
            var level = policy == CertificateFailurePolicy.Warn ? LogMessageLevel.Warn : LogMessageLevel.Error;
            foreach (var failure in failures)
                LogForNodeWithDetails(level, node, "check-certificate {0}: {1}", file, failure);

            if (policy == CertificateFailurePolicy.Stop)
                ThrowErrorForNode(node, $"check-certificate failed for {file} - stopping the build");
        }

        private bool TryResolveRequiredEkus(XElement actionNode, out string[] ekus, out string? error)
        {
            ekus = Array.Empty<string>();
            error = null;

            var purposeName = GetAttribute(actionNode, "purpose", "");
            if (!string.IsNullOrEmpty(purposeName))
            {
                var purpose = CertificatePurpose.Resolve(purposeName);
                if (purpose == null)
                {
                    error = $"unknown purpose '{purposeName}' (expected one of {string.Join(", ", CertificatePurpose.Names)})";
                    return false;
                }
                ekus = purpose.Ekus.ToArray();
            }

            var ekuAttribute = GetAttribute(actionNode, "eku", "");
            if (!string.IsNullOrEmpty(ekuAttribute))
            {
                if (!CertificatePurpose.TryParseEkuList(ekuAttribute, out var parsed, out var invalid))
                {
                    error = $"unknown eku '{invalid}'";
                    return false;
                }
                ekus = parsed;
            }

            return true;
        }

        /// <summary>
        /// Publishes a checked certificate's properties as ~PREFIX_*~ variables. Values are never
        /// empty because VariableStore.Set logs an error for an empty value; booleans are 1/0 and
        /// absent strings become "(none)".
        /// </summary>
        private void exportCertificateVars(string? prefix, CertificateCheckResult result)
        {
            prefix = string.IsNullOrEmpty(prefix) ? "CERT" : prefix;

            if (string.IsNullOrEmpty(result.Thumbprint))
            {
                // The certificate could not be read at all — only the verdict is knowable.
                addvar(prefix + "_VALID", "0");
                return;
            }

            exportCertificateVars(prefix, result.Subject, result.Issuer, result.Thumbprint, result.SerialNumber,
                result.NotBeforeUtc, result.NotAfterUtc, result.DaysRemaining, result.HasPrivateKey,
                result.KeyAlgorithm, result.KeySize, result.Ekus, result.Ok);
        }

        /// <summary>Publishes a freshly generated certificate's properties, which is valid by construction.</summary>
        private void exportCertificateVars(string? prefix, X509Certificate2 cert)
        {
            prefix = string.IsNullOrEmpty(prefix) ? "CERT" : prefix;

            var notBefore = cert.NotBefore.ToUniversalTime();
            var notAfter = cert.NotAfter.ToUniversalTime();
            var (keyAlgorithm, keySize) = CertificateChecker.DescribeKey(cert);

            exportCertificateVars(prefix, cert.Subject, cert.Issuer, cert.Thumbprint, cert.SerialNumber,
                notBefore, notAfter, (int)Math.Floor((notAfter - DateTime.UtcNow).TotalDays), cert.HasPrivateKey,
                keyAlgorithm, keySize, CertificateChecker.ReadEkus(cert).Oids, valid: true);
        }

        private void exportCertificateVars(string prefix, string subject, string issuer, string thumbprint,
            string serial, DateTime notBeforeUtc, DateTime notAfterUtc, int daysRemaining, bool hasPrivateKey,
            string keyAlgorithm, int keySize, IReadOnlyList<string> ekus, bool valid)
        {
            addvar(prefix + "_SUBJECT", NoneIfEmpty(subject));
            addvar(prefix + "_ISSUER", NoneIfEmpty(issuer));
            addvar(prefix + "_THUMBPRINT", NoneIfEmpty(thumbprint));
            addvar(prefix + "_SERIAL", NoneIfEmpty(serial));
            addvar(prefix + "_NOT_BEFORE", CertificateChecker.Format(notBeforeUtc));
            addvar(prefix + "_NOT_AFTER", CertificateChecker.Format(notAfterUtc));
            addvar(prefix + "_DAYS_REMAINING", daysRemaining.ToString());
            addvar(prefix + "_HAS_PRIVATE_KEY", hasPrivateKey ? "1" : "0");
            addvar(prefix + "_KEY_ALGORITHM", NoneIfEmpty(keyAlgorithm));
            addvar(prefix + "_KEY_SIZE", keySize.ToString());
            addvar(prefix + "_EKU", ekus.Count > 0 ? string.Join(",", ekus.Select(CertificatePurpose.DescribeEku)) : "(none)");
            addvar(prefix + "_VALID", valid ? "1" : "0");
        }

        private static string NoneIfEmpty(string? value) => string.IsNullOrEmpty(value) ? "(none)" : value;
        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// A password attribute is a secret by definition, so it is registered for redaction
        /// unconditionally — unlike &lt;Set&gt;, which requires secret="true".
        /// </summary>
        private string? ResolvePassword(XElement node, string attributeName)
        {
            var raw = GetAttribute(node, attributeName, "");
            if (string.IsNullOrEmpty(raw))
                return null;
            var value = _transform.ExpandVars(raw)!;
            RLog.RegisterSecret(value);
            return value;
        }

        private string GetFilenameFromNode(string path, string? newPathAttribute, XElement actionNode)
        {
            var file = _transform.ExpandVars(GetFilePathAttribute(actionNode, "file"));
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
                    contents = _transform.Transform(transform.Value, contents);
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

        private static bool CheckParamNotEmpty(string? param, string errorMessage)
        {
            if (String.IsNullOrEmpty(param))
            {
                RLog.ErrorFormat(errorMessage);
                return false;
            }
            return true;
        }


        private string? AddFolder(int skipCount, string root, string? directory)
        {
            if (directory != null)
            {
                if (directory.Contains("*"))
                    Artefacts.Add(new Artefact(skipCount, root, _transform.ExpandVars(directory)));
                else
                {
                    directory = PathFinder.FindDirectory(_transform.ExpandVars(directory),
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
                    Artefacts.Add(new Artefact(skipCount, root, _transform.ExpandVars(file)));
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

                addvar("Major", (long?)Info.Major);
                addvar("Minor", (long?)Info.Minor);
                addvar("Patch", (long?)Info.Patch);
                addvar("MsiMajor", (long?)(Info.Major - 2000));
            }
        }

        private void addvar(string k, string v) => vars.Set(k, v);
        private void addvar(string k, long? v) => vars.Set(k, v);

        internal static (string os, string arch, string runtime) GetPlatformInfo()
        {
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                   : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "osx"
                   :                                                        "linux";
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            var runtime = $"{(os == "windows" ? "win" : os)}-{arch}";
            return (os, arch, runtime);
        }
        public int Process()
        {
            if (Targets.Any())
            {
                var files = Artefacts.SelectMany(xx => xx.GetFiles()).Distinct().ToList();
                if (!files.Any())
                {
                    RLog.InfoFormat("No artefacts - not building archive");
                    return ExitCodes.Success;
                }
                var target = Targets[PublishType];

                var filename = new List<string>
                {
                    TargetName,
                    PublishType,
                    target.GetVersion(vars["SemVer"])
                };

                Manifest = new BuildManifest
                {
                    TargetName = TargetName ?? "",
                    TargetType = target.Type.ToString(),
                    Version = vars["SemVer"] ?? "",
                    OutputPath = target.Path.FullName,
                    ErrorCount = RLog.ErrorCount,
                    Timestamp = DateTimeOffset.UtcNow,
                };

                switch (target.Type)
                {
                    case TargetTypeEnum.ZipFile:
                        if (DryRun)
                        {
                            var zipFileName = Path.Combine(target.Path.FullName, string.Join("-", filename) + ".zip");
                            RLog.InfoFormat("[dry-run] Would create zip: {0} with {1} files", zipFileName, files.Count);
                        }
                        else
                        {
                            var zipPath = CreateZipFileArtefact(files, target, filename);
                            Manifest.Artefacts.Add(new ManifestEntry
                            {
                                Path = zipPath,
                                ArchiveName = Path.GetFileName(zipPath),
                                SizeBytes = new FileInfo(zipPath).Length
                            });
                        }
                        return ExitCodes.Success;
                    case TargetTypeEnum.LocalFolder:
                        {
                            foreach (var file in files)
                            {
                                var outputFileName = Path.Combine(target.Path.FullName, file.GetArchiveName());
                                if (DryRun)
                                {
                                    RLog.InfoFormat("[dry-run] Would copy: {0} -> {1}", Path.GetFileName(file.Name), outputFileName);
                                    Manifest.Artefacts.Add(new ManifestEntry { Path = outputFileName, ArchiveName = file.GetArchiveName() });
                                }
                                else
                                {
                                    target.Path.Create();
                                    RLog.TraceFormat(String.Format("Copy {0} (from {1})", outputFileName, Path.GetFileName(file.Name)));
                                    File.Copy(file.Name, outputFileName, true);
                                    Manifest.Artefacts.Add(new ManifestEntry
                                    {
                                        Path = outputFileName,
                                        ArchiveName = file.GetArchiveName(),
                                        SizeBytes = new FileInfo(file.Name).Length
                                    });
                                }
                            }
                            return ExitCodes.Success;
                        }
                }
            }
            return ExitCodes.BuildError;
        }
        private static string CreateZipFileArtefact(IEnumerable<FileDetails> files, PublishTarget target, List<string> filename)
        {
            int fileCount = 0;
            target.Path.Create();
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
            return zipFileName;
        }

        internal void AddModule(string module)
        {
            Modules[module] = true;
        }
    }
}