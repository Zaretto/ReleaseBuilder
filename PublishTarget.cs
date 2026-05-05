using rjtool;
using System.Xml.Linq;

namespace ReleaseBuilder
{
    public class PublishTarget
    {
        public enum TargetTypeEnum
        {
            ZipFile,
            LocalFolder,
            RemoteFolderSCP,
        }

        public PublishTarget(string name, string path)
        {
            this.Name = name;
            if (path != null)
                this.Path = Directory.CreateDirectory(path);
            else
                throw new Exception(string.Format("PublishTarget: {0} path not found", name));

        }
        public string GetVersion(string v) => string.IsNullOrEmpty(Version) ? v : Version;
        public string Name { get; }
        public DirectoryInfo? Path { get; }
        public string Version { get; set; }
        public TargetTypeEnum Type { get; set; } = TargetTypeEnum.ZipFile;
    }
}