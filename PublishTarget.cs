using rjtool;
using System.Xml.Linq;

namespace ReleaseBuilder
{
    public class PublishTarget
    {
        public PublishTarget(string name, string path)
        {
            this.Name = name;
            if (path != null && Directory.Exists(path))
                this.Path = new DirectoryInfo(path);
            else
                throw new Exception(string.Format("{0} missing Path", name));

        }
        public string GetVersion(string v) => string.IsNullOrEmpty(Version) ? v : Version;
        public string Name { get; }
        public DirectoryInfo? Path { get; }
        public string Version { get; set; }
    }
}