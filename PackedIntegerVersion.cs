using rjtool;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public class PackedIntegerVersion
    {
        public int Value { get; protected set; } = 0;
        public int MajorFactor { get; protected set; } = 10000;
        public int MinorFactor { get; protected set; } = 100;

        public PackedIntegerVersion(int? scale = null)
        {
            if (scale.HasValue)
                SetFactors(scale.Value);
        }
        public PackedIntegerVersion(int major, int minor, int patch, int? scale = null) : this(scale)
        {
            PackVersion(major, minor, patch);
        }
        void PackVersion(long? lmajor, long? lminor, long? lpatch)
        {
            int major = 1;
            int minor = 0;
            int patch = 0;
            if (lmajor.HasValue)
                major = (int)lmajor.Value;
            if (lminor.HasValue)
                minor = (int)lminor.Value;
            if (lpatch.HasValue)
                patch = (int)lpatch.Value;
            // Ensure each part is within the range [0, 99]
            if (major < 0 || major > 9999)
                throw new ArgumentOutOfRangeException("PackVersion: Major must be in the range [0, 9999]");
            if (minor < 0 || minor > 99 || patch < 0 || patch > 99)
            {
                throw new ArgumentOutOfRangeException("PackVersion: Minor and Patch must be in the range [0, 99]");
            }
            // Combine parts into a single integer
            Value = (major * MajorFactor) + (minor * MinorFactor) + patch;
        }
        public PackedIntegerVersion(long? lmajor, long? lminor, long? lpatch, int? scale = null) : this(scale)
        {
            PackVersion(lmajor, lminor, lpatch);
        }
        public PackedIntegerVersion(GitVersion info)
        {
            PackVersion(info.Major, info.Minor, info.Patch);
        } 
        private void SetFactors(int scale)
        {
            MajorFactor = (int)Math.Pow(10, scale * 2);
            MinorFactor = (int)Math.Pow(10, scale * 1);

        }
        public PackedIntegerVersion(int combinedVersion, int? scale = null)
        {
            if (scale.HasValue)
                SetFactors(scale.Value);

            Value = combinedVersion;


        }
        public int Major => Value / MajorFactor;
        public int Minor => (Value / MinorFactor) % MinorFactor;
        public int Patch => Value % MinorFactor;
        public override string ToString()
        {
            return String.Format("{0}.{1}.{2}", Major, Minor, Patch);
        }
    }
}