using ReleaseBuilder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rjtool
{
    public class fexec
    {
		/// <summary>
		/// Execute a process
		/// </summary>
		/// <param name="programFilePath">full path of app</param>
		/// <param name="commandLineArgs">arguments to use</param>
		/// <param name="workingDirectory">working directory</param>
		/// <param name="wait">whether to wait or run async</param>
		/// <param name="useShellExecute">if true will do via shell execute; output will be in a terminal window.</param>
		/// <param name="requiredExitCodes">list of exit codes that mean success. If null or empty process exit code will not be checked.</param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public static string executeCommand(string programFilePath, string commandLineArgs, string workingDirectory, bool wait, bool useShellExecute, IEnumerable<int> requiredExitCodes)
		{
			Process myProcess = new Process();

			myProcess.StartInfo.WorkingDirectory = workingDirectory;
			myProcess.StartInfo.FileName = programFilePath;
			myProcess.StartInfo.Arguments = commandLineArgs;
			myProcess.StartInfo.UseShellExecute = useShellExecute;
			myProcess.StartInfo.CreateNoWindow = true;
			myProcess.StartInfo.RedirectStandardOutput = !useShellExecute;
			myProcess.StartInfo.RedirectStandardError = !useShellExecute;
			var v = myProcess.Start();
			RLog.TraceFormat("Exec: {0} {1}", programFilePath, commandLineArgs);
			if (useShellExecute)
			{
				myProcess.WaitForExit();
                if (requiredExitCodes != null && requiredExitCodes.Any() && !requiredExitCodes.Contains(myProcess.ExitCode))
                {
                    throw new Exception(string.Format("Command {0} failed ",Path.GetFileName(programFilePath)));
                }
                return "";
			}
			else
			{
				StreamReader sOut = myProcess.StandardOutput;
				StreamReader sErr = myProcess.StandardError;
				var rv = new StringBuilder();
				try
				{
					string str;
					// reading errors and output async...
					var buffer = new char[100];
					int l;
					if (wait)
					{
						while ((l = sOut.Read(buffer, 0, buffer.Length)) > 0)
						{
							//logMessage(str + Environment.NewLine, true);
							//Application.DoEvents();
							var msg = new String(buffer, 0, l);

							sOut.BaseStream.Flush();
							if (useShellExecute)
								Console.WriteLine(msg);
							else
								rv.Append(msg);

							if (sOut.EndOfStream)
								break;
						}
						while ((str = sErr.ReadLine()) != null && !sErr.EndOfStream)
						{
							if (useShellExecute)
								Console.WriteLine(str);
							else
								rv.Append(str);
							sErr.BaseStream.Flush();
						}
						myProcess.WaitForExit();

						if (requiredExitCodes != null && requiredExitCodes.Any() && !requiredExitCodes.Contains(myProcess.ExitCode))
						{
							RLog.ErrorFormat(rv.ToString());
							throw new Exception(String.Format("Command {0} {1} failed ", Path.GetFileName(programFilePath), commandLineArgs));
						}
					}
				}
				finally
				{
					sOut.Close();
					sErr.Close();
				}
                return rv.ToString();
            }
		}
        /// <summary>
        /// Expands environment variables and, if unqualified, locates the exe in the working directory
        /// or the evironment's path.
        /// </summary>
        /// <param name="exe">The name of the executable file</param>
        /// <returns>The fully-qualified path to the file</returns>
        /// <exception cref="System.IO.FileNotFoundException">Raised when the exe was not found</exception>
		/// http://csharptest.net/526/how-to-search-the-environments-path-for-an-exe-or-dll/index.html
        public static string FindExePath(string exe)
        {
            exe = Environment.ExpandEnvironmentVariables(exe);
            if (!File.Exists(exe))
            {
                if (Path.GetDirectoryName(exe) == String.Empty)
                {
                    foreach (string test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    {
                        string path = test.Trim();
                        if (!String.IsNullOrEmpty(path) && File.Exists(path = Path.Combine(path, exe)))
                            return Path.GetFullPath(path);
                    }
                }
				RLog.ErrorFormat("FindExePath: Could not locate {0}", exe);
                throw new FileNotFoundException(exe);
            }
            return Path.GetFullPath(exe);
        }
        public static void AddExePath(IEnumerable<DirectoryInfo> d)
        {
            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (envPath != null)
            {
                var newPath = envPath.Split(';').Where(xx => Directory.Exists(xx)).Select(xx => new DirectoryInfo(xx)).Select(xx => xx.FullName).ToList();
                var l = d.Select(d => d.FullName).Except(newPath);
                newPath.AddRange(l);
                if (l.Any())
                {
                    RLog.TraceFormat("Added {0} to path", string.Join(" ", l));
                    Environment.SetEnvironmentVariable("PATH", string.Join(";", newPath));
                }
            }
        }
    }
}
