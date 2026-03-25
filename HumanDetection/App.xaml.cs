using System;
using System.IO;
using System.Text;

namespace HumanDetection
{
    public static class Logger
    {
        private static readonly string logFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.txt");

        public static void Log(string message)
        {
            try
            {
                var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
                File.AppendAllText(logFilePath, logMessage, Encoding.UTF8);
            }
            catch
            {
                // Avoid crash if logging fails
            }
        }

        public static void LogException(Exception ex)
        {
            try
            {
                var sb = new StringBuilder();

                sb.AppendLine("===== EXCEPTION =====");
                sb.AppendLine($"Time: {DateTime.Now}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"StackTrace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    sb.AppendLine("---- INNER EXCEPTION ----");
                    sb.AppendLine(ex.InnerException.Message);
                    sb.AppendLine(ex.InnerException.StackTrace);
                }

                sb.AppendLine("========================");

                File.AppendAllText(logFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}