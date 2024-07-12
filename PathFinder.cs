using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    internal class PathFinder
    {
        /// <summary>
        /// find a directory somewhere in the list of paths.
        /// </summary>
        /// <param name="toFind"></param>
        /// <param name="paths">list of paths. A "-" means append the string to find with a "-" in 
        /// front of it.
        /// </param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static string? FindDirectory(string? toFind, List<String>[] paths)
        {
            if (toFind == null)
            {
                return null;
            }
            foreach (var path in paths)
            {
                var dir = "";
                foreach (var directory in path.Where(xx=>!string.IsNullOrEmpty(xx)))
                {
                    string fullPath = "";
                    if (directory == "-")
                    {
                        dir = dir + "-";
                        fullPath = dir + toFind;
                    }
                    else
                    {
                        dir = Path.Combine(dir, directory);
                        fullPath = Path.Combine(dir, toFind);
                    }
                    if (Directory.Exists(fullPath))
                        return fullPath;
                }
            }
            throw new Exception("Cannot locate " + toFind);
        }
        public static string? FindFile(string? toFind, List<String>[] paths)
        {
            if (toFind == null) 
                return null;
            foreach (var path in paths)
            {
                var dir = "";
                foreach (var directory in path.Where(xx => !string.IsNullOrEmpty(xx)))
                {
                    string fullPath = "";
                    dir = Path.Combine(dir, directory);
                    fullPath = Path.Combine(dir, toFind);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }
            return null;
        }
    }
}
