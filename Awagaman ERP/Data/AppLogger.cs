using System;
using System.Diagnostics;
using System.IO;

namespace Awagaman_ERP.Data
{
    public static class AppLogger
    {
        private static readonly object SyncRoot = new object();

        public static string LogPath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "logs");
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                return Path.Combine(dir, "app.log");
            }
        }

        public static void LogException(string context, Exception ex)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}{Environment.NewLine}";
                lock (SyncRoot)
                {
                    File.AppendAllText(LogPath, line);
                }
                Debug.WriteLine(line);
            }
            catch
            {
                // Logging must never fail the app.
            }
        }
    }
}
