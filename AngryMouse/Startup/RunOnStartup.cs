using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;

namespace AngryMouse.Startup
{
    /// <summary>
    /// This class defines if app would run on Windows startup.
    /// </summary>
    public class RunOnStartup
    {
        private const string AppName = "AngryMouse";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ClickOnceShortcutName = AppName + ".appref-ms";

        /// <summary>
        /// Returns whether app would be run on Windows startup.
        /// </summary>
        public static bool isRunOnStartup()
        {
            return File.Exists(GetStartupShortcutPath()) || IsRegistryRunEnabled();
        }

        /// <summary>
        /// Set app to run or not on Windows startup.
        /// </summary>
        public static void setRunOnStartup(bool enable)
        {
            if (enable)
            {
                var clickOnceShortcutPath = GetClickOnceShortcutPath();
                if (clickOnceShortcutPath != null)
                {
                    Directory.CreateDirectory(GetStartupFolderPath());
                    File.Copy(clickOnceShortcutPath, GetStartupShortcutPath(), true);
                    DeleteRegistryRunValue();
                    return;
                }

                SetRegistryRunValue();
            }
            else
            {
                DeleteStartupShortcut();
                DeleteRegistryRunValue();
            }
        }

        private static bool IsRegistryRunEnabled()
        {
            using (var registryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                return registryKey?.GetValue(AppName) != null;
            }
        }

        private static void SetRegistryRunValue()
        {
            using (var registryKey = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                registryKey?.SetValue(AppName, QuotePath(System.Windows.Forms.Application.ExecutablePath));
            }
        }

        private static void DeleteRegistryRunValue()
        {
            using (var registryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                registryKey?.DeleteValue(AppName, false);
            }
        }

        private static string GetClickOnceShortcutPath()
        {
            var programsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var expectedShortcutPath = Path.Combine(programsFolderPath, AppName, ClickOnceShortcutName);
            if (File.Exists(expectedShortcutPath))
            {
                return expectedShortcutPath;
            }

            if (!Directory.Exists(programsFolderPath))
            {
                return null;
            }

            return Directory.GetFiles(programsFolderPath, ClickOnceShortcutName, SearchOption.AllDirectories)
                            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
        }

        private static string GetStartupFolderPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }

        private static string GetStartupShortcutPath()
        {
            return Path.Combine(GetStartupFolderPath(), ClickOnceShortcutName);
        }

        private static void DeleteStartupShortcut()
        {
            var startupShortcutPath = GetStartupShortcutPath();
            if (File.Exists(startupShortcutPath))
            {
                File.Delete(startupShortcutPath);
            }
        }

        private static string QuotePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (path.StartsWith("\"", StringComparison.Ordinal) && path.EndsWith("\"", StringComparison.Ordinal))
            {
                return path;
            }

            return "\"" + path + "\"";
        }
    }
}
