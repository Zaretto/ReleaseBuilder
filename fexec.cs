using ReleaseBuilder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace rjtool
{
    public partial class fexec
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
        public static string executeCommand(
            string programFilePath,
            string commandLineArgs,
            string workingDirectory,
            bool wait,
            bool useShellExecute,
            IEnumerable<int> requiredExitCodes)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.WorkingDirectory = workingDirectory;
            myProcess.StartInfo.FileName = programFilePath;
            myProcess.StartInfo.Arguments = commandLineArgs;
            myProcess.StartInfo.UseShellExecute = useShellExecute;
            myProcess.StartInfo.CreateNoWindow = true;
            myProcess.StartInfo.RedirectStandardOutput = !useShellExecute;
            myProcess.StartInfo.RedirectStandardError = !useShellExecute;

            RLog.TraceFormat("Exec: {0} {1}", programFilePath, commandLineArgs);

            if (useShellExecute)
            {
                myProcess.Start();
                if (wait)
                {
                    myProcess.WaitForExit();
                    if (requiredExitCodes != null && requiredExitCodes.Any() && !requiredExitCodes.Contains(myProcess.ExitCode))
                    {
                        throw new Exception(string.Format("Command {0} failed", Path.GetFileName(programFilePath)));
                    }
                }
                return "";
            }
            else
            {
                var output = new StringBuilder();
                var errors = new StringBuilder();
                var outputSink = new OutputSink();

                // Setup async output handlers
                myProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                        outputSink.Write(e.Data, OutputSink.StreamType.StdOut);
                    }
                };

                myProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errors.AppendLine(e.Data);
                        outputSink.Write(e.Data, OutputSink.StreamType.StdErr);
                    }
                };

                myProcess.Start();

                // Begin async reading
                myProcess.BeginOutputReadLine();
                myProcess.BeginErrorReadLine();

                if (wait)
                {
                    myProcess.WaitForExit();

                    // Important: Wait for async event handlers to complete
                    myProcess.WaitForExit();

                    // Flush any remaining partial lines
                    outputSink.Flush();

                    if (requiredExitCodes != null && requiredExitCodes.Any() && !requiredExitCodes.Contains(myProcess.ExitCode))
                    {
                        var errorOutput = errors.ToString();
                        if (!string.IsNullOrEmpty(errorOutput))
                        {
                            RLog.ErrorFormat(errorOutput);
                        }
                        throw new Exception(String.Format("Command {0} {1} failed", Path.GetFileName(programFilePath), commandLineArgs));
                    }
                }

                // Combine output and errors
                var result = output.ToString();
                var errorText = errors.ToString();
                if (!string.IsNullOrEmpty(errorText))
                {
                    result += errorText;
                }

                return result;
            }
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
