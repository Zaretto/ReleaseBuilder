using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public class PathFinder
    {
        /// <summary>
        /// find a directory somewhere in a list of paths.
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
                foreach (var directory in path.Where(xx => !string.IsNullOrEmpty(xx)))
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
                    {
                        RLog.DebugFormat("-- found {0}", fullPath);
                        return fullPath;
                    }
                    else
                        RLog.DebugFormat("-- not found {0}", fullPath);

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
        /// <summary>
        /// platform aware logic to get a filename or filenames that could be used to satisfy and "app target".
        /// </summary>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static IEnumerable<string> GetApplicationTargets(string appName)
        {
            var rv = new List<string>();

            // compatibility - if the path is a .exe it is from when we only supported windows; so handle this by removing the extension.
            if (string.Equals(Path.GetExtension(appName), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                appName = Path.Combine(Path.GetDirectoryName(appName) ?? string.Empty, Path.GetFileNameWithoutExtension(appName));
            }

            // for windows we also need to handle the norm which is .exe files; so add these.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                rv.Add(appName + ".exe");
                rv.Add(appName + ".cmd");
                rv.Add(appName + ".bat");
            }
            rv.Add(appName);

            // osx also could have .app bundles.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                rv.Add(appName + ".app");
            }
            return rv;
        }
        /// <summary>
        /// Expands environment variables and, if unqualified, locates the exe in the working directory
        /// or the evironment's path.
        /// </summary>
        /// <param name="exe">The name of the executable file</param>
        /// <returns>The fully-qualified path to the file</returns>
        /// <exception cref="System.IO.FileNotFoundException">Raised when the exe was not found</exception>
        /// http://csharptest.net/526/how-to-search-the-environments-path-for-an-exe-or-dll/index.html
        public static string FindExePath(string baseApp)
        {
            foreach (var app in GetApplicationTargets(baseApp))
            {
                var exe = Environment.ExpandEnvironmentVariables(app);
                if (!File.Exists(exe))
                {
                    if (Path.GetDirectoryName(exe) == String.Empty)
                    {
                        foreach (string test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                        {
                            string path = test.Trim();
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                var exePath = Path.Combine(path, exe);
                                if (CanExecute(exePath))
                                {
                                    return Path.GetFullPath(exePath);
                                }
                            }
                        }
                    }
                }
                var ff = Path.GetFullPath(exe);
                if (File.Exists(ff) && CanExecute(ff))
                    return ff;
            }
            RLog.ErrorFormat("FindExePath: Could not locate {0}", baseApp);
            throw new FileNotFoundException(baseApp);
        }
        private static bool IsUnixExecutable(string path)
        {
            //TODO: test the following code;
            //try
            //{
            //    var psi = new System.Diagnostics.ProcessStartInfo
            //    {
            //        FileName = "/bin/sh",
            //        Arguments = $"-c \"[ -x \\\"{path}\\\" ]\"",
            //        RedirectStandardOutput = true,
            //        RedirectStandardError = true,
            //        UseShellExecute = false
            //    };

            //    using var proc = System.Diagnostics.Process.Start(psi);
            //    proc.WaitForExit();
            //    return proc.ExitCode == 0;
            //}
            //catch
            //{
            //    return false;
            //}

            return true;
        }

        public static bool CanExecute(string path)
        {
            if (String.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(path))
                return false;
            var fileInfo = new FileInfo(path);
            bool result = (fileInfo.Attributes & FileAttributes.Directory) == 0 && fileInfo.Exists;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return result;

            // On Unix, check if file has any execute permission bit set
            return result && IsUnixExecutable(path);
        }
    }
}