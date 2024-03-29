using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public enum ArtefactType
    {
        File, 
        Path,
        Match,
    }
    public class Artefact
    {
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

        internal IEnumerable<string> GetFiles(string root)
        {
            if (Type == ArtefactType.Match)
            {
                return Directory.GetFiles(Path.GetDirectoryName(PathName), Path.GetFileName(PathName));
            }
            if (directoryInfo != null)
                return Directory.GetFiles(directoryInfo.FullName, "*.*", SearchOption.AllDirectories);
            return new[] { PathName };
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
