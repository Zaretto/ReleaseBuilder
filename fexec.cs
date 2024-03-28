using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rjtool
{
    internal class fexec
    {
		public static string executeCommand(string programFilePath, string commandLineArgs, string workingDirectory, bool wait)
		{
			Process myProcess = new Process();

			myProcess.StartInfo.WorkingDirectory = workingDirectory;
			myProcess.StartInfo.FileName = programFilePath;
			myProcess.StartInfo.Arguments = commandLineArgs;
			myProcess.StartInfo.UseShellExecute = false;
			myProcess.StartInfo.CreateNoWindow = true;
			myProcess.StartInfo.RedirectStandardOutput = true;
			myProcess.StartInfo.RedirectStandardError = true;
			var v = myProcess.Start();

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
						rv.Append(new String(buffer, 0, l));
						sOut.BaseStream.Flush();
						if (sOut.EndOfStream)
							break;
					}
					while ((str = sErr.ReadLine()) != null && !sErr.EndOfStream)
					{
						//logError(str + Environment.NewLine, true);
						//Application.DoEvents();
						rv.Append(str);
						sErr.BaseStream.Flush();
					}
					myProcess.WaitForExit();
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
}
