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

		public long? Major { get; set; }
		public long? Minor { get; set; }
		public long? Patch { get; set; }
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

		public static GitVersion? ForDirectory(string srcdir)
        {
			var gst = fexec.executeCommand("gitversion.exe", "", srcdir, true);
			if (!string.IsNullOrEmpty(gst))
			{
				try
				{
                    return gst.FromJson<GitVersion>();
				}
				catch(Exception ex)
                {
					return null;
                }
			}
			throw new InvalidOperationException("No version available fexec failed");
		}
	}
}
