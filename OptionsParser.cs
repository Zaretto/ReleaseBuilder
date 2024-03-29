using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public class OptionsParser
    {
        private readonly List<string> _args;

        public OptionsParser(string[] args)
        {
            _args = args.ToList();
        }
        public DirectoryInfo? GetDirectoryArgument(string key, char shortKey)
        {
            var v = GetStringArgument(key, shortKey);
            if (v != null)
            {
                if (Directory.Exists(v))
                    return new DirectoryInfo(v);
                else
                    RLog.ErrorFormat("{0} directory {1} not found", key, v);
            }
            return null;
        }
        public FileInfo? GetFileArgument(string key, char shortKey)
        {
            var v = GetStringArgument(key, shortKey);
            if (v != null)
            {
                if (File.Exists(v))
                    return new FileInfo(v);
                else
                    RLog.ErrorFormat("{0} file {1} not found", key, v);
            }
            return null;
        }
        public string? GetStringArgument(string key, char shortKey)
        {
            var index = _args.IndexOf("--" + key);

            if (index >= 0 && _args.Count > index)
            {
                return _args[index + 1];
            }

            index = _args.IndexOf("-" + shortKey);

            if (index >= 0 && _args.Count > index)
            {
                return _args[index + 1];
            }

            return null;
        }

        public bool GetSwitchArgument(string key,  char shortKey)
        {
            return _args.Contains("--" + key) || _args.Contains("-" + shortKey);
        }
    }
}

