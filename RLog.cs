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
        public static void Format(LogMessageLevel level, string message, params object[] args)
        {
            LogMessage newMessage;
            if (args != null && args.Any())
                newMessage = new LogMessage(level, String.Format(message, args));
            else
                newMessage = new LogMessage(level, message);
            if (level >= Level)
                Console.WriteLine(newMessage.ToString());
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
