using System;
using System.Collections.Generic;
using System.Text;

namespace SignalVision
{
    public class Logger
    {
        private static readonly object FileLock = new();

        public string Tag { get; init; } = "";
        public string OutputFolder { get; }
        public string LogFilePath { get; }

        public Logger(string tag, string logFilePath)
        {
            Tag = tag;
            LogFilePath = logFilePath;
            OutputFolder = Path.GetDirectoryName(LogFilePath)
                ?? throw new ArgumentException("The log file path must include a directory.", nameof(logFilePath));
        }

        public Logger WithTag(string tag) => new(tag, LogFilePath);
        public void Trace(string message)
        {
            Log("TRACE", ConsoleColor.DarkGray, message);
        }

        public void Debug(string message)
        {
            Log("DEBUG", ConsoleColor.Cyan, message);
        }

        public void Info(string message)
        {
            Log("INFO", ConsoleColor.Gray, message);
        }

        public void Warn(string message)
        {
            Log("WARN", ConsoleColor.Yellow, message);
        }

        public void Error(string message)
        {
            Log("ERROR", ConsoleColor.Red, message);
        }

        private void Log(string level, ConsoleColor color, string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Tag}] {message}";

            Directory.CreateDirectory(OutputFolder);
            lock (FileLock)
            {
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
            }

            ConsoleColor originalColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(logEntry);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }
}
