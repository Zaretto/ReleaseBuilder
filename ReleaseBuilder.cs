using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public class ReleaseBuilder
    {
        public string toolsdir = "I:/dev/brightserve/MSOS/release-tools";
        public List<Artefact> Artefacts = new List<Artefact>();
        public string ArchivePath;
        public string PublishType;
        public string Root;
        public ReleaseBuilder(string path, string archive_path, string publish_type)
        {
            addArtefact("7z a -r ~PATH~ThemisApp-~TYPE~-~VERSION~.7z index.html MSOS.UWP.appinstaller ~APPPATH~\\*.appxbundle", "del ~PATH~ThemisApp-~TYPE~-~VERSION~.7z");
            Root = path;
            ArchivePath = archive_path;
            PublishType = publish_type;

            if (publish_type == "")
                publish_type = "live";

            Console.WriteLine("Archive into ", archive_path);
            Console.WriteLine("Publish for ", publish_type);
        }
        void addArtefact(string create, string clean)
        {
            Artefacts.Add(new Artefact(create, clean));
        }
        void GetVersion()
        {
            var lines = fexec(Root, "GitVersion");
            var Info = Newtonsoft.Json.JsonConvert.DeserializeObject<GitVersionInfo>(lines);
            SemVer = Info.NuGetVersionV2;
            Major = Info.Major;
            Minor = Info.Minor;
            Patch = Info.Patch;
            PreReleaseTag = Info.LegacySemVer;
        }

        void exec(string cmd)
        {
            Console.WriteLine(cmd);
            fexec(cmd);
        }
        string expand_vars(string cmd)
        {
            cmd = cmd.Replace("~VERSION~", release_version);
            cmd = cmd.Replace("~PATH~", ArchivePath);
            cmd = cmd.Replace("~TYPE~", PublishType);
            cmd = cmd.Replace("~APPPATH~", APPPATH);
            return cmd;
        }


        void Process()
        {

            foreach (var artefact in Artefacts)
            {
                rv = rv.Replace("\"", "");
                rv = rv.Replace(",", "");
                release_version = rv;
                Console.WriteLine("VERSION : ", release_version);

                APPPATH = string.Format("MSOS.UWP_{0}.{1}.{2}.0_Test", Major, Minor, Patch);

                exec(expand_vars(clean_archive));

                exec(expand_vars(mkarchive));

            }
        }

    }
}
