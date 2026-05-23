using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace AngryMouse.Util
{
    internal static class DebugLog
    {
        private const long MaxLogBytes = 1024 * 1024;
        private const string LogFileName = "debug.log";
        private static readonly object SyncRoot = new object();

        public static string LogPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AngryMouse",
                    LogFileName);
            }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    return Properties.Settings.Default.DebugEnabled;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Write(string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            WriteEntry(message);
        }

        public static void WriteException(string context, Exception exception)
        {
            if (exception == null)
            {
                Write(context + ": unknown error");
                return;
            }

            Write(context + ": " + exception.GetType().Name + ": " + exception.Message);
        }

        public static void WriteCrashException(string context, Exception exception)
        {
            if (!IsEnabled)
            {
                return;
            }

            WriteException(context, exception);
            TryOpenInNotepad();
        }

        public static void OpenInNotepad()
        {
            EnsureDirectory();
            if (!File.Exists(LogPath))
            {
                File.WriteAllText(LogPath, string.Empty);
            }

            Process.Start("notepad.exe", LogPath);
        }

        public static void TryOpenInNotepad()
        {
            try
            {
                OpenInNotepad();
            }
            catch
            {
            }
        }

        private static void WriteEntry(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    EnsureDirectory();
                    RotateIfNeeded();
                    var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)
                               + " " + (message ?? string.Empty) + Environment.NewLine;
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
            }
        }

        private static void EnsureDirectory()
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
            {
                return;
            }

            var backupPath = LogPath + ".bak";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(LogPath, backupPath);
        }
    }
}
