using ReleaseBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinyJson;

namespace rjtool
{
	public class GitVersion
	{

		public int Major { get; set; }
		public int Minor { get; set; }
		public int Patch { get; set; }
		public string PreReleaseTag { get; set; }
		public string PreReleaseTagWithDash { get; set; }
		public string PreReleaseLabel { get; set; }
		public long? PreReleaseNumber { get; set; }
		public string BuildMetaData { get; set; }
		public string BuildMetaDataPadded { get; set; }
		public string FullBuildMetaData { get; set; }
		public string MajorMinorPatch { get; set; }
		public string SemVer { get; set; }
		public string LegacySemVer { get; set; }
		public string LegacySemVerPadded { get; set; }
		public string AssemblySemVer { get; set; }
		public string AssemblySemFileVer { get; set; }
		public string FullSemVer { get; set; }
		public string InformationalVersion { get; set; }
		public string BranchName { get; set; }
		public string Sha { get; set; }
		public string NuGetVersionV2 { get; set; }
		public string NuGetVersion { get; set; }
		public string NuGetPreReleaseTagV2 { get; set; }
		public string NuGetPreReleaseTag { get; set; }
		public long? CommitsSinceVersionSource { get; set; }
		public string CommitsSinceVersionSourcePadded { get; set; }
		public string CommitDate { get; set; }

        public static string? GetJsonForDirectory(string srcdir)
        {
            var gst = fexec.executeCommand(PathFinder.FindExePath("dotnet-gitversion"), "", srcdir, true, false, null);
			if (!string.IsNullOrEmpty(gst))
			{
				return gst;
			}
			return null;
        }
        public static GitVersion? FromJson(string? gst)
		{
            try
            {
                if (gst != null)
                    return gst.FromJson<GitVersion>();
            }
            catch (Exception ex)
            {
                RLog.ErrorFormat("Failed to get version {0}", ex.Message);
                throw new InvalidOperationException("Failed to get git version: " + ex.Message);
            }
			return null;
        }
        public static GitVersion? ForDirectory(string srcdir)
        {
			return FromJson(GetJsonForDirectory(srcdir));
        }
    }
}
