using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public enum LogMessageLevel
    {
        Debug = 0,
        Trace = 1,
        Info  = 2,
        Warn  = 3,
        Error = 4,
        Fatal = 5
    };

    public class LogMessage
    {
        public LogMessageLevel Level { get; set; }
        public DateTimeOffset Date { get; set; }
        public string Message { get; set; }

        public LogMessage(LogMessageLevel level, string message)
        {
            this.Message = message;
            this.Level = level;
            this.Date = DateTime.UtcNow;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToShortDateString());
            sb.Append(' ');
            sb.Append(DateTime.Now.ToShortTimeString());
            var lvl = (" [" + Level.ToString().ToUpper().PadLeft(5) + "] ");
            sb.Append(lvl);

            sb.Append(Message);
            return sb.ToString();
        }
    }

    public static class RLog
    {
        public static LogMessageLevel Level=LogMessageLevel.Info;
        public static int ErrorCount { get; private set; } = 0;
        public static void ResetErrorCount() => ErrorCount = 0;

        /// <summary>
        /// Values shorter than this are not registered as secrets: redacting them would hide
        /// unrelated text (a short PIN, a port number) all over the log.
        /// </summary>
        public const int MinSecretLength = 8;

        public const string Redaction = "***";

        // Longest first, so a secret that contains another is redacted whole.
        private static string[] _secrets = Array.Empty<string>();
        private static readonly object _secretsLock = new object();

        /// <summary>
        /// Registers a value to be replaced with *** in everything this log writes — variable
        /// values, command lines, dry-run messages and captured process output alike.
        /// </summary>
        public static void RegisterSecret(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < MinSecretLength)
                return;

            lock (_secretsLock)
            {
                if (_secrets.Contains(value))
                    return;
                _secrets = _secrets.Append(value).OrderByDescending(s => s.Length).ToArray();
            }

            // Ask the CI runner to mask it too, so anything the job prints is covered.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                Console.WriteLine("::add-mask::" + value);
        }

        public static void ResetSecrets()
        {
            lock (_secretsLock)
                _secrets = Array.Empty<string>();
        }

        public static string Redact(string message)
        {
            var secrets = _secrets;
            if (secrets.Length == 0 || string.IsNullOrEmpty(message))
                return message;
            foreach (var secret in secrets)
                message = message.Replace(secret, Redaction);
            return message;
        }

        public static void Format(LogMessageLevel level, string message, params object[] args)
        {
            LogMessage newMessage;
            if (args != null && args.Any())
                newMessage = new LogMessage(level, String.Format(message, args));
            else
                newMessage = new LogMessage(level, message);
            if (level >= LogMessageLevel.Error)
                ErrorCount++;
            if (level >= Level)
                Console.WriteLine(Redact(newMessage.ToString()));
        }

        public static void ErrorFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Error, message, args);
        }

        public static void InfoFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Info, message, args);
        }

        public static void FatalFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Fatal, message, args);
        }

        public static void WarnFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Warn, message, args);
        }

        public static void DebugFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Debug, message, args);
        }

        public static void TraceFormat(string message, params object[] args)
        {
            Format(LogMessageLevel.Trace, message, args);
        }
    }
}
