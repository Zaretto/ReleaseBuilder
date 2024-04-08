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
        public string PathName;
        public DirectoryInfo? directoryInfo;
        public FileInfo? fileInfo;

        public ArtefactType Type;
        public Artefact(FileInfo file)
        {
            fileInfo = file;
            PathName = file.FullName;
            Type = ArtefactType.File;
        }
        public Artefact(DirectoryInfo dir)
        {
            directoryInfo = dir;
            PathName = dir.FullName;
            Type = ArtefactType.Path;
        }

        public Artefact(string match)
        {
            PathName = match.Replace("/", "\\");
            Type = ArtefactType.Match;
        }

        internal IEnumerable<FileDetails> GetFiles()
        {
            if (Type == ArtefactType.Match)
            {
                var root = Path.GetDirectoryName(PathName);
                Directory.GetFiles(root, Path.GetFileName(PathName)).Select(xx => new FileDetails(root, xx)).ToList();
            }
            if (directoryInfo != null)
            {
                return Directory.GetFiles(directoryInfo.FullName, "*.*", SearchOption.AllDirectories).Select(xx => new FileDetails(directoryInfo.FullName, xx)).ToList();
            }
            return new[] { new FileDetails(Path.GetDirectoryName(PathName), PathName) };
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
