using System;
using System.IO;

namespace Scribo;

public static class Logger
{
    private static readonly object _syncLock = new();
    private static string? _logFilePath;

    public static string LogPath
    {
        get
        {
            if (_logFilePath != null) return _logFilePath;

            try
            {
                // 1. Portable mode: if local directory is writable
                string localLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                if (File.Exists(localLog))
                {
                    using var test = File.Open(localLog, FileMode.Open, FileAccess.ReadWrite);
                    _logFilePath = localLog;
                    return _logFilePath;
                }
            }
            catch { }

            try
            {
                // 2. Standard AppData logs directory
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Scribo", "logs");

                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                _logFilePath = Path.Combine(logDir, "app.log");
            }
            catch
            {
                _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
            }

            return _logFilePath;
        }
    }

    public static void Log(string message)
    {
        lock (_syncLock)
        {
            try
            {
                string path = LogPath;
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Auto-truncate if log exceeds 1 MB
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists && fileInfo.Length > 1024 * 1024)
                {
                    File.WriteAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] --- Log Rolled Over ---\n");
                }

                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }
    }

    public static void LogException(string context, Exception? ex)
    {
        if (ex == null) return;
        Log($"[ERROR] [{context}] {ex.GetType().Name}: {ex.Message}\nStack Trace:\n{ex.StackTrace}");
    }
}
