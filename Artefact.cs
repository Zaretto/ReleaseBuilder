using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{

    public class Artefact
    {
        public enum ArtefactType
        {
            File,
            Path,
            Match,
        }

        public int DirectoryRemoveCount { get; }
        public string Root { get; }
        public string NewName { get; }

        public string PathName;
        public DirectoryInfo? directoryInfo;
        public FileInfo? fileInfo;

        public ArtefactType Type;
        public Artefact(FileInfo file, string newName)
        {
            fileInfo = file;
            PathName = file.FullName;
            NewName = newName;
            Type = ArtefactType.File;
        }
        public Artefact(DirectoryInfo dir)
        {
            directoryInfo = dir;
            PathName = dir.FullName;
            Type = ArtefactType.Path;
        }

        public Artefact(int directoryRemoveCount, string root, string match)
        {
            DirectoryRemoveCount = directoryRemoveCount;
            Root = root;
            PathName = match.Replace("/", "\\");
            Type = ArtefactType.Match;
            DirectoryRemoveCount = directoryRemoveCount;
        }

        internal IEnumerable<FileDetails> GetFiles()
        {
            if (Type == ArtefactType.Match)
            {
                var pathRoot = Path.GetDirectoryName(PathName);
                return Directory.GetFiles(pathRoot, Path.GetFileName(PathName)).Select(xx => new FileDetails(DirectoryRemoveCount, Root, xx, NewName)).ToList();
            }
            if (directoryInfo != null)
            {
                return Directory.GetFiles(directoryInfo.FullName, "*.*", SearchOption.AllDirectories).Select(xx => new FileDetails(directoryInfo.FullName, xx)).ToList();
            }
            return new[] { new FileDetails(0, Path.GetDirectoryName(PathName), PathName, NewName) };
        }
        public override bool Equals(object obj)
        {
            var aobj = obj as Artefact;
            if (aobj != null)
                return aobj.GetHashCode() == GetHashCode();
            return false;
        }
        public override int GetHashCode()
        {
            if (PathName != null)
                return PathName.GetHashCode();
            return 0;
        }
    
    public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Type);
            sb.Append(": ");
            sb.Append(PathName);
            if (directoryInfo != null)
                sb.Append(" (directory info)");
            if (fileInfo != null)
                sb.Append(" (file info)");
            return sb.ToString();
        }
    }
}
