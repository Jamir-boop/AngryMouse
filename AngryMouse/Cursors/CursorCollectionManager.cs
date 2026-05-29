using AngryMouse.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace AngryMouse.Cursors
{
    internal static class CursorCollectionManager
    {
        public const string CollectionMode = "Collection";
        public const string SystemMode = "System";
        public const string BundledAdwaitaName = "Adwaita";

        private const string AssignmentFileName = "assignments.txt";
        private const string RoleSettingsFileName = "cursor-settings.txt";
        private const string CursorCollectionsFolderName = "CursorCollections";
        private const string PackageSettingsFileName = "settings.xml";
        private const string PackageCollectionsRoot = "CursorCollections";
        private const string PackageVersion = "2.5.3";

        public static readonly CursorRoleDefinition[] Roles =
        {
            new CursorRoleDefinition("arrow", "Arrow", "arrow.png", 32512, new System.Windows.Point(0, 0)),
            new CursorRoleDefinition("ibeam", "I-beam", "ibeam.png", 32513, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("wait", "Wait", "wait.png", 32514, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("appstarting", "App starting", "appstarting.png", 32650, new System.Windows.Point(0, 0)),
            new CursorRoleDefinition("crosshair", "Crosshair", "crosshair.png", 32515, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("uparrow", "Up arrow", "uparrow.png", 32516, new System.Windows.Point(12, 0)),
            new CursorRoleDefinition("sizens", "Size NS", "sizens.png", 32645, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("sizewe", "Size WE", "sizewe.png", 32644, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("sizenwse", "Size NWSE", "sizenwse.png", 32642, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("sizenesw", "Size NESW", "sizenesw.png", 32643, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("sizeall", "Size all", "sizeall.png", 32646, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("no", "No", "no.png", 32648, new System.Windows.Point(12, 12)),
            new CursorRoleDefinition("hand", "Hand", "hand.png", 32649, new System.Windows.Point(6, 1)),
            new CursorRoleDefinition("help", "Help", "help.png", 32651, new System.Windows.Point(0, 0))
        };

        private static readonly Dictionary<string, CursorRoleDefinition> RolesByKey =
            Roles.ToDictionary(item => item.Key, item => item, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> KnownFileNames =
            CreateKnownFileNameMap();

        private static readonly object MetadataCacheLock = new object();

        private static readonly Dictionary<string, Dictionary<string, string>> AssignmentsCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Dictionary<string, CursorRoleRenderSettings>> RoleSettingsCache =
            new Dictionary<string, Dictionary<string, CursorRoleRenderSettings>>(StringComparer.OrdinalIgnoreCase);

        public static event EventHandler CollectionChanged;

        public static void InitializeDefaults()
        {
            EnsureBundledCollectionsInstalled();

            var settings = Properties.Settings.Default;
            var changed = false;

            if (string.IsNullOrWhiteSpace(settings.CursorCollectionName) ||
                !IsCollectionUsable(settings.CursorCollectionName))
            {
                settings.CursorCollectionName = BundledAdwaitaName;
                changed = true;
            }

            if (string.Equals(settings.CursorSourceMode, "Custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settings.CursorSourceMode, "BuiltIn", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(settings.CursorSourceMode))
            {
                settings.CursorSourceMode = SystemMode;
                changed = true;
            }

            if (changed)
            {
                settings.Save();
            }
        }

        public static bool TryFallbackMissingSelectedCollection(out string missingCollectionName)
        {
            EnsureBundledCollectionsInstalled();

            missingCollectionName = null;
            var settings = Properties.Settings.Default;
            var selectedCollectionName = settings.CursorCollectionName;
            if (IsCollectionUsable(selectedCollectionName))
            {
                return false;
            }

            if (string.Equals(selectedCollectionName, BundledAdwaitaName, StringComparison.OrdinalIgnoreCase))
            {
                DebugLog.Write("Bundled cursor collection is missing or empty: " + BundledAdwaitaName);
                return false;
            }

            missingCollectionName = string.IsNullOrWhiteSpace(selectedCollectionName)
                ? "(none)"
                : selectedCollectionName;
            settings.CursorCollectionName = BundledAdwaitaName;
            settings.Save();
            DebugLog.Write("Selected cursor collection missing or empty. Fallback to " + BundledAdwaitaName + ": " + missingCollectionName);
            NotifyCollectionChanged();
            return true;
        }

        public static void EnsureBundledCollectionsInstalled()
        {
            var bundledRoot = GetBundledCollectionsRoot();
            if (!Directory.Exists(bundledRoot))
            {
                return;
            }

            Directory.CreateDirectory(GetUserCollectionsRoot());

            foreach (var bundledCollectionPath in Directory.GetDirectories(bundledRoot).OrderBy(Path.GetFileName))
            {
                var collectionName = Path.GetFileName(bundledCollectionPath);
                var targetPath = GetCollectionPath(collectionName);
                Directory.CreateDirectory(targetPath);

                var sourceFiles = Directory.GetFiles(bundledCollectionPath, "*.png", SearchOption.TopDirectoryOnly);
                if (sourceFiles.Length == 0)
                {
                    var renderedPath = Path.Combine(bundledCollectionPath, "Rendered");
                    if (Directory.Exists(renderedPath))
                    {
                        sourceFiles = Directory.GetFiles(renderedPath, "*.png", SearchOption.TopDirectoryOnly);
                    }
                }

                foreach (var sourceFile in sourceFiles.OrderBy(Path.GetFileName))
                {
                    var targetFile = Path.Combine(targetPath, Path.GetFileName(sourceFile));
                    if (!File.Exists(targetFile))
                    {
                        File.Copy(sourceFile, targetFile);
                    }
                }

                var assignmentsPath = GetAssignmentsPath(collectionName);
                if (!File.Exists(assignmentsPath) || LoadAssignments(collectionName).Count == 0)
                {
                    SaveAssignments(collectionName, InferAssignments(collectionName));
                }
            }
        }

        public static string[] GetCollectionNames()
        {
            EnsureBundledCollectionsInstalled();
            var root = GetUserCollectionsRoot();
            if (!Directory.Exists(root))
            {
                return new string[0];
            }

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string ImportCollectionFolder(string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException("Cursor collection folder not found.");
            }

            Directory.CreateDirectory(GetUserCollectionsRoot());

            var baseName = SanitizeCollectionName(Path.GetFileName(sourceFolder));
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Imported";
            }

            var collectionName = GetAvailableCollectionName(baseName);
            var targetPath = GetCollectionPath(collectionName);
            var sourceFiles = Directory.GetFiles(sourceFolder, "*.png", SearchOption.TopDirectoryOnly);
            if (sourceFiles.Length == 0)
            {
                throw new InvalidOperationException("Cursor collection folder contains no PNG files.");
            }

            Directory.CreateDirectory(targetPath);

            foreach (var sourceFile in sourceFiles.OrderBy(Path.GetFileName))
            {
                var targetFile = Path.Combine(targetPath, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, targetFile);
            }

            SaveAssignments(collectionName, InferAssignments(collectionName));
            return collectionName;
        }

        public static void ExportSettingsPackage(string packagePath)
        {
            ExportSettingsPackage(packagePath, includeCollections: true);
        }

        public static void ExportSettingsPackage(string packagePath, bool includeCollections)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new InvalidOperationException("Export path is empty.");
            }

            EnsureBundledCollectionsInstalled();

            var directory = Path.GetDirectoryName(packagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteSettingsPackageEntry(archive);

                if (includeCollections)
                {
                    var root = GetUserCollectionsRoot();
                    if (Directory.Exists(root))
                    {
                        foreach (var collectionPath in Directory.GetDirectories(root).OrderBy(Path.GetFileName))
                        {
                            var collectionName = Path.GetFileName(collectionPath);
                            if (string.Equals(collectionName, BundledAdwaitaName, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            foreach (var filePath in GetPackageCollectionFiles(collectionPath))
                            {
                                var entryName = PackageCollectionsRoot + "/" + collectionName + "/" + Path.GetFileName(filePath);
                                AddPackageFile(archive, filePath, entryName);
                            }
                        }
                    }
                }
            }
        }

        public static SettingsPackageImportResult ImportSettingsPackage(string packagePath)
        {
            return ImportSettingsPackage(packagePath, includeCollections: true);
        }

        public static SettingsPackageImportResult ImportSettingsPackage(string packagePath, bool includeCollections)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException("Settings package not found.", packagePath);
            }

            EnsureBundledCollectionsInstalled();

            var result = new SettingsPackageImportResult();
            XDocument settingsDocument = null;

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var settingsEntry = archive.GetEntry(PackageSettingsFileName);
                if (settingsEntry != null)
                {
                    using (var stream = settingsEntry.Open())
                    {
                        settingsDocument = XDocument.Load(stream);
                    }
                }

                var collectionGroups = archive.Entries
                    .Where(IsPackageCollectionFile)
                    .GroupBy(GetPackageCollectionName, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (includeCollections)
                {
                    foreach (var group in collectionGroups)
                    {
                        var sourceCollectionName = SanitizeCollectionName(group.Key);
                        if (string.IsNullOrWhiteSpace(sourceCollectionName))
                        {
                            sourceCollectionName = "Imported";
                        }

                        var targetCollectionName = GetAvailableCollectionName(sourceCollectionName);
                        var targetPath = GetCollectionPath(targetCollectionName);
                        Directory.CreateDirectory(targetPath);

                        foreach (var entry in group.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
                        {
                            var fileName = GetPackageCollectionFileName(entry.FullName);
                            if (string.IsNullOrWhiteSpace(fileName))
                            {
                                continue;
                            }

                            ExtractPackageEntry(entry, Path.Combine(targetPath, fileName));
                        }

                        if (!File.Exists(GetAssignmentsPath(targetCollectionName)))
                        {
                            SaveAssignments(targetCollectionName, InferAssignments(targetCollectionName));
                        }

                        result.CollectionNameMap[sourceCollectionName] = targetCollectionName;
                        result.ImportedCollectionCount++;
                        if (!string.Equals(sourceCollectionName, targetCollectionName, StringComparison.OrdinalIgnoreCase))
                        {
                            result.RenamedCollectionCount++;
                        }
                    }
                }

                if (settingsDocument != null)
                {
                    ApplyImportedSettings(settingsDocument, result.CollectionNameMap);
                }
            }

            NotifyCollectionChanged();
            return result;
        }

        public static bool CanRemoveCollection(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) ||
                string.Equals(collectionName, BundledAdwaitaName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var root = Path.GetFullPath(GetUserCollectionsRoot()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(GetCollectionPath(collectionName)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path);
        }

        public static void RemoveCollection(string collectionName)
        {
            if (!CanRemoveCollection(collectionName))
            {
                return;
            }

            Directory.Delete(GetCollectionPath(collectionName), true);

            var settings = Properties.Settings.Default;
            if (string.Equals(settings.CursorCollectionName, collectionName, StringComparison.OrdinalIgnoreCase))
            {
                settings.CursorCollectionName = BundledAdwaitaName;
                settings.Save();
            }

            NotifyCollectionChanged();
        }

        public static Dictionary<string, string> LoadAssignments(string collectionName)
        {
            var cacheKey = collectionName ?? string.Empty;
            lock (MetadataCacheLock)
            {
                Dictionary<string, string> cachedAssignments;
                if (AssignmentsCache.TryGetValue(cacheKey, out cachedAssignments))
                {
                    return new Dictionary<string, string>(cachedAssignments, StringComparer.OrdinalIgnoreCase);
                }
            }

            var assignments = LoadAssignmentsFromDisk(collectionName);

            lock (MetadataCacheLock)
            {
                AssignmentsCache[cacheKey] = new Dictionary<string, string>(assignments, StringComparer.OrdinalIgnoreCase);
            }

            return assignments;
        }

        private static Dictionary<string, string> LoadAssignmentsFromDisk(string collectionName)
        {
            var path = GetAssignmentsPath(collectionName);
            if (!File.Exists(path))
            {
                var inferred = InferAssignments(collectionName);
                SaveAssignments(collectionName, inferred);
                return inferred;
            }

            var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var roleKey = trimmed.Substring(0, separatorIndex).Trim();
                var fileName = Path.GetFileName(trimmed.Substring(separatorIndex + 1).Trim());
                if (RolesByKey.ContainsKey(roleKey) && !string.IsNullOrWhiteSpace(fileName))
                {
                    assignments[roleKey] = fileName;
                }
            }

            return assignments;
        }

        public static Dictionary<string, CursorRoleRenderSettings> LoadRoleSettings(string collectionName)
        {
            var cacheKey = collectionName ?? string.Empty;
            lock (MetadataCacheLock)
            {
                Dictionary<string, CursorRoleRenderSettings> cachedSettings;
                if (RoleSettingsCache.TryGetValue(cacheKey, out cachedSettings))
                {
                    return CloneRoleSettings(cachedSettings);
                }
            }

            var loadedSettings = LoadRoleSettingsFromDisk(collectionName);

            lock (MetadataCacheLock)
            {
                RoleSettingsCache[cacheKey] = CloneRoleSettings(loadedSettings);
            }

            return loadedSettings;
        }

        private static Dictionary<string, CursorRoleRenderSettings> CloneRoleSettings(
            Dictionary<string, CursorRoleRenderSettings> source)
        {
            var clone = new Dictionary<string, CursorRoleRenderSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source)
            {
                clone[entry.Key] = entry.Value != null ? entry.Value.Clone() : new CursorRoleRenderSettings();
            }

            return clone;
        }

        private static Dictionary<string, CursorRoleRenderSettings> LoadRoleSettingsFromDisk(string collectionName)
        {
            var settings = CreateDefaultRoleSettings();
            var path = GetRoleSettingsPath(collectionName);
            if (!File.Exists(path))
            {
                return settings;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                var parts = key.Split('.');
                if (parts.Length != 2 || !RolesByKey.ContainsKey(parts[0]))
                {
                    continue;
                }

                CursorRoleRenderSettings roleSettings;
                if (!settings.TryGetValue(parts[0], out roleSettings))
                {
                    continue;
                }

                double doubleValue;
                bool boolValue;
                if (string.Equals(parts[1], "hotspotOffsetX", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out doubleValue))
                {
                    roleSettings.HotspotOffsetX = doubleValue;
                }
                else if (string.Equals(parts[1], "hotspotOffsetY", StringComparison.OrdinalIgnoreCase) &&
                         double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out doubleValue))
                {
                    roleSettings.HotspotOffsetY = doubleValue;
                }
                else if (string.Equals(parts[1], "trimTransparentPadding", StringComparison.OrdinalIgnoreCase) &&
                         bool.TryParse(value, out boolValue))
                {
                    roleSettings.TrimTransparentPadding = boolValue;
                }
            }

            return settings;
        }

        public static CursorRoleRenderSettings GetRoleSettings(string collectionName, string roleKey)
        {
            var settings = LoadRoleSettings(collectionName);
            CursorRoleRenderSettings roleSettings;
            return settings.TryGetValue(roleKey, out roleSettings)
                ? roleSettings.Clone()
                : new CursorRoleRenderSettings();
        }

        public static void SaveRoleSettings(string collectionName, string roleKey, CursorRoleRenderSettings roleSettings)
        {
            var settings = LoadRoleSettings(collectionName);
            if (RolesByKey.ContainsKey(roleKey))
            {
                settings[roleKey] = (roleSettings ?? new CursorRoleRenderSettings()).Clone();
            }

            SaveRoleSettings(collectionName, settings);
        }

        public static void SaveRoleSettings(string collectionName, IDictionary<string, CursorRoleRenderSettings> settings)
        {
            var path = GetRoleSettingsPath(collectionName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var lines = new List<string>
            {
                "# AngryMouse cursor render settings"
            };

            foreach (var role in Roles)
            {
                CursorRoleRenderSettings roleSettings;
                if (!settings.TryGetValue(role.Key, out roleSettings) || roleSettings == null)
                {
                    roleSettings = new CursorRoleRenderSettings();
                }

                lines.Add(role.Key + ".hotspotOffsetX=" + roleSettings.HotspotOffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                lines.Add(role.Key + ".hotspotOffsetY=" + roleSettings.HotspotOffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture));
                lines.Add(role.Key + ".trimTransparentPadding=" + roleSettings.TrimTransparentPadding.ToString().ToLowerInvariant());
            }

            File.WriteAllLines(path, lines);
            NotifyCollectionChanged();
        }

        public static void SaveAssignments(string collectionName, IDictionary<string, string> assignments)
        {
            var path = GetAssignmentsPath(collectionName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var lines = new List<string>
            {
                "# AngryMouse cursor role assignments"
            };

            foreach (var role in Roles)
            {
                string fileName;
                if (assignments.TryGetValue(role.Key, out fileName) && !string.IsNullOrWhiteSpace(fileName))
                {
                    lines.Add(role.Key + "=" + Path.GetFileName(fileName));
                }
            }

            File.WriteAllLines(path, lines);
            NotifyCollectionChanged();
        }

        public static string CopyFileIntoCollection(string collectionName, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PNG file not found.", sourcePath);
            }

            var collectionPath = GetCollectionPath(collectionName);
            Directory.CreateDirectory(collectionPath);

            var sourceDirectory = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
            var targetDirectory = Path.GetFullPath(collectionPath).TrimEnd(Path.DirectorySeparatorChar);
            var fileName = Path.GetFileName(sourcePath);

            if (string.Equals(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            var targetFileName = GetUniqueFileName(collectionPath, fileName);
            File.Copy(sourcePath, Path.Combine(collectionPath, targetFileName));
            return targetFileName;
        }

        public static string ResolveRoleFilePath(string collectionName, string roleKey)
        {
            var assignments = LoadAssignments(collectionName);
            var assignedPath = GetAssignedExistingFilePath(collectionName, assignments, roleKey);
            if (!string.IsNullOrWhiteSpace(assignedPath))
            {
                return assignedPath;
            }

            var arrowPath = GetAssignedExistingFilePath(collectionName, assignments, "arrow");
            if (!string.IsNullOrWhiteSpace(arrowPath))
            {
                return arrowPath;
            }

            var selectedArrowPath = Path.Combine(GetCollectionPath(collectionName), "arrow.png");
            if (File.Exists(selectedArrowPath))
            {
                return selectedArrowPath;
            }

            return GetBundledAdwaitaArrowPath();
        }

        public static CursorRoleDefinition GetRole(string roleKey)
        {
            CursorRoleDefinition role;
            return RolesByKey.TryGetValue(roleKey, out role) ? role : RolesByKey["arrow"];
        }

        public static string GetCollectionPath(string collectionName)
        {
            var safeName = string.IsNullOrWhiteSpace(collectionName)
                ? BundledAdwaitaName
                : SanitizeCollectionName(collectionName);

            return Path.Combine(GetUserCollectionsRoot(), safeName);
        }

        public static string GetBundledAdwaitaArrowPath()
        {
            return Path.Combine(GetBundledCollectionsRoot(), BundledAdwaitaName, "arrow.png");
        }

        public static string GetBundledCollectionFilePath(string collectionName, string fileName)
        {
            return Path.Combine(GetBundledCollectionsRoot(), SanitizeCollectionName(collectionName), Path.GetFileName(fileName));
        }

        public static string GetBundledRenderedPngPath(string pngFileName)
        {
            return Path.Combine(GetBundledCollectionsRoot(), BundledAdwaitaName, "Rendered", pngFileName);
        }

        public static void NotifyCollectionChanged()
        {
            lock (MetadataCacheLock)
            {
                AssignmentsCache.Clear();
                RoleSettingsCache.Clear();
            }

            CursorVisualCache.ClearMemory();

            var handler = CollectionChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        private static void WriteSettingsPackageEntry(ZipArchive archive)
        {
            var entry = archive.CreateEntry(PackageSettingsFileName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                CreateSettingsDocument().Save(stream);
            }
        }

        private static XDocument CreateSettingsDocument()
        {
            var settings = Properties.Settings.Default;
            var root = new XElement(
                "AngryMouseSettings",
                new XAttribute("version", PackageVersion),
                new XElement(
                    "Settings",
                    CreateSettingElement("CursorSize", settings.CursorSize.ToString(CultureInfo.InvariantCulture)),
                    CreateSettingElement("CursorAnimationLength", settings.CursorAnimationLength.ToString(CultureInfo.InvariantCulture)),
                    CreateSettingElement("CursorVisibleDuration", settings.CursorVisibleDuration.ToString(CultureInfo.InvariantCulture)),
                    CreateSettingElement("ShakeActivationEnabled", settings.ShakeActivationEnabled.ToString().ToLowerInvariant()),
                    CreateSettingElement("HotkeyActivationEnabled", settings.HotkeyActivationEnabled.ToString().ToLowerInvariant()),
                    CreateSettingElement("HotkeyActivationMode", NormalizeHotkeyActivationMode(settings.HotkeyActivationMode)),
                    CreateSettingElement("HotkeyModifiers", NormalizeHotkeyModifiers(settings.HotkeyModifiers)),
                    CreateSettingElement("HotkeyKey", NormalizeHotkeyKey(settings.HotkeyKey)),
                    CreateSettingElement("HideBuiltInCursor", settings.HideBuiltInCursor.ToString().ToLowerInvariant()),
                    CreateSettingElement("CursorSourceMode", settings.CursorSourceMode ?? string.Empty),
                    CreateSettingElement("CursorCollectionName", settings.CursorCollectionName ?? string.Empty),
                    CreateSettingElement("ThemeMode", settings.ThemeMode ?? string.Empty),
                    CreateSettingElement("ShakeTrackingInterval", settings.ShakeTrackingInterval.ToString(CultureInfo.InvariantCulture)),
                    CreateSettingElement("ShakeMinimumSpeed", settings.ShakeMinimumSpeed.ToString(CultureInfo.InvariantCulture)),
                    CreateSettingElement("ShakeMinimumTurns", settings.ShakeMinimumTurns.ToString(CultureInfo.InvariantCulture))));

            var roleSettingsElement = CreateRoleSettingsElement();
            if (roleSettingsElement != null)
            {
                root.Add(roleSettingsElement);
            }

            return new XDocument(root);
        }

        private static XElement CreateSettingElement(string name, string value)
        {
            return new XElement("Setting", new XAttribute("name", name), new XAttribute("value", value ?? string.Empty));
        }

        private static XElement CreateRoleSettingsElement()
        {
            var root = new XElement("CursorRoleSettings");
            var collectionsRoot = GetUserCollectionsRoot();
            if (!Directory.Exists(collectionsRoot))
            {
                return null;
            }

            foreach (var collectionPath in Directory.GetDirectories(collectionsRoot).OrderBy(Path.GetFileName))
            {
                var collectionName = Path.GetFileName(collectionPath);
                if (!File.Exists(GetRoleSettingsPath(collectionName)))
                {
                    continue;
                }

                var roleSettings = LoadRoleSettings(collectionName);
                var collectionElement = new XElement("Collection", new XAttribute("name", collectionName));
                foreach (var role in Roles)
                {
                    CursorRoleRenderSettings settings;
                    if (!roleSettings.TryGetValue(role.Key, out settings) || settings == null)
                    {
                        settings = new CursorRoleRenderSettings();
                    }

                    collectionElement.Add(new XElement(
                        "Role",
                        new XAttribute("key", role.Key),
                        new XAttribute("hotspotOffsetX", settings.HotspotOffsetX.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("hotspotOffsetY", settings.HotspotOffsetY.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("trimTransparentPadding", settings.TrimTransparentPadding.ToString().ToLowerInvariant())));
                }

                root.Add(collectionElement);
            }

            return root.HasElements ? root : null;
        }

        private static IEnumerable<string> GetPackageCollectionFiles(string collectionPath)
        {
            foreach (var filePath in Directory.GetFiles(collectionPath, "*.png", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName))
            {
                yield return filePath;
            }

            var assignmentsPath = Path.Combine(collectionPath, AssignmentFileName);
            if (File.Exists(assignmentsPath))
            {
                yield return assignmentsPath;
            }

            var roleSettingsPath = Path.Combine(collectionPath, RoleSettingsFileName);
            if (File.Exists(roleSettingsPath))
            {
                yield return roleSettingsPath;
            }
        }

        private static void AddPackageFile(ZipArchive archive, string filePath, string entryName)
        {
            var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
            using (var source = File.OpenRead(filePath))
            using (var target = entry.Open())
            {
                source.CopyTo(target);
            }
        }

        private static bool IsPackageCollectionFile(ZipArchiveEntry entry)
        {
            return !string.IsNullOrWhiteSpace(GetPackageCollectionFileName(entry.FullName));
        }

        private static string GetPackageCollectionName(ZipArchiveEntry entry)
        {
            var fullName = NormalizePackagePath(entry.FullName);
            var rest = fullName.Substring(PackageCollectionsRoot.Length + 1);
            var separatorIndex = rest.IndexOf('/');
            return separatorIndex > 0 ? rest.Substring(0, separatorIndex) : string.Empty;
        }

        private static string GetPackageCollectionFileName(string entryName)
        {
            var fullName = NormalizePackagePath(entryName);
            var rootPrefix = PackageCollectionsRoot + "/";
            if (!fullName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var rest = fullName.Substring(rootPrefix.Length);
            var separatorIndex = rest.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex >= rest.Length - 1)
            {
                return null;
            }

            if (rest.IndexOf('/', separatorIndex + 1) >= 0)
            {
                return null;
            }

            var fileName = rest.Substring(separatorIndex + 1);
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            if (string.Equals(fileName, AssignmentFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, RoleSettingsFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            return null;
        }

        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim('/');
        }

        private static void ExtractPackageEntry(ZipArchiveEntry entry, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

            using (var source = entry.Open())
            using (var target = File.Create(targetPath))
            {
                source.CopyTo(target);
            }
        }

        private static void ApplyImportedSettings(XDocument document, IDictionary<string, string> collectionNameMap)
        {
            var values = ReadSettingsDocument(document);
            var settings = Properties.Settings.Default;

            double doubleValue;
            int intValue;
            bool boolValue;
            string stringValue;

            if (TryGetSettingValue(values, "CursorSize", out stringValue) &&
                double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                settings.CursorSize = doubleValue;
            }

            if (TryGetSettingValue(values, "CursorAnimationLength", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.CursorAnimationLength = intValue;
            }

            if (TryGetSettingValue(values, "CursorVisibleDuration", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.CursorVisibleDuration = intValue;
            }

            if (TryGetSettingValue(values, "ShakeActivationEnabled", out stringValue) &&
                bool.TryParse(stringValue, out boolValue))
            {
                settings.ShakeActivationEnabled = boolValue;
            }

            if (TryGetSettingValue(values, "HotkeyActivationEnabled", out stringValue) &&
                bool.TryParse(stringValue, out boolValue))
            {
                settings.HotkeyActivationEnabled = boolValue;
            }

            if (TryGetSettingValue(values, "HotkeyActivationMode", out stringValue))
            {
                settings.HotkeyActivationMode = NormalizeHotkeyActivationMode(stringValue);
            }

            if (TryGetSettingValue(values, "HotkeyModifiers", out stringValue))
            {
                settings.HotkeyModifiers = NormalizeHotkeyModifiers(stringValue);
            }

            if (TryGetSettingValue(values, "HotkeyKey", out stringValue))
            {
                settings.HotkeyKey = NormalizeHotkeyKey(stringValue);
            }

            NormalizeActivationSettings(settings);

            if (TryGetSettingValue(values, "HideBuiltInCursor", out stringValue) &&
                bool.TryParse(stringValue, out boolValue))
            {
                settings.HideBuiltInCursor = boolValue;
            }

            if (TryGetSettingValue(values, "CursorSourceMode", out stringValue))
            {
                settings.CursorSourceMode = string.Equals(stringValue, SystemMode, StringComparison.OrdinalIgnoreCase)
                    ? SystemMode
                    : CollectionMode;
            }

            if (TryGetSettingValue(values, "CursorCollectionName", out stringValue))
            {
                var selectedCollectionName = SanitizeCollectionName(stringValue);
                string mappedCollectionName;
                if (!string.IsNullOrWhiteSpace(selectedCollectionName) &&
                    collectionNameMap.TryGetValue(selectedCollectionName, out mappedCollectionName))
                {
                    selectedCollectionName = mappedCollectionName;
                }

                settings.CursorCollectionName = !string.IsNullOrWhiteSpace(selectedCollectionName) &&
                                                Directory.Exists(GetCollectionPath(selectedCollectionName))
                    ? selectedCollectionName
                    : BundledAdwaitaName;
            }

            if (TryGetSettingValue(values, "ThemeMode", out stringValue))
            {
                settings.ThemeMode = string.Equals(stringValue, AppTheme.LightMode, StringComparison.OrdinalIgnoreCase)
                    ? AppTheme.LightMode
                    : AppTheme.DarkMode;
            }

            if (TryGetSettingValue(values, "ShakeTrackingInterval", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.ShakeTrackingInterval = intValue;
            }

            if (TryGetSettingValue(values, "ShakeMinimumSpeed", out stringValue) &&
                double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                settings.ShakeMinimumSpeed = doubleValue;
            }

            if (TryGetSettingValue(values, "ShakeMinimumTurns", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.ShakeMinimumTurns = intValue;
            }

            ApplyImportedRoleSettings(document, collectionNameMap);
            settings.Save();
        }

        private static void ApplyImportedRoleSettings(XDocument document, IDictionary<string, string> collectionNameMap)
        {
            var roleSettingsRoot = document.Root?.Element("CursorRoleSettings");
            if (roleSettingsRoot == null)
            {
                return;
            }

            foreach (var collectionElement in roleSettingsRoot.Elements("Collection"))
            {
                var sourceCollectionName = SanitizeCollectionName(collectionElement.Attribute("name")?.Value);
                if (string.IsNullOrWhiteSpace(sourceCollectionName))
                {
                    continue;
                }

                string targetCollectionName;
                if (!collectionNameMap.TryGetValue(sourceCollectionName, out targetCollectionName))
                {
                    targetCollectionName = sourceCollectionName;
                }

                if (!Directory.Exists(GetCollectionPath(targetCollectionName)))
                {
                    continue;
                }

                var settings = CreateDefaultRoleSettings();
                var hasRoleSettings = false;
                foreach (var roleElement in collectionElement.Elements("Role"))
                {
                    var roleKey = roleElement.Attribute("key")?.Value;
                    if (string.IsNullOrWhiteSpace(roleKey) || !RolesByKey.ContainsKey(roleKey))
                    {
                        continue;
                    }

                    CursorRoleRenderSettings roleSettings;
                    if (!settings.TryGetValue(roleKey, out roleSettings) || roleSettings == null)
                    {
                        roleSettings = new CursorRoleRenderSettings();
                        settings[roleKey] = roleSettings;
                    }

                    ApplyRoleSettingAttribute(roleElement, "hotspotOffsetX", value => roleSettings.HotspotOffsetX = value);
                    ApplyRoleSettingAttribute(roleElement, "hotspotOffsetY", value => roleSettings.HotspotOffsetY = value);

                    bool trimTransparentPadding;
                    if (bool.TryParse(roleElement.Attribute("trimTransparentPadding")?.Value, out trimTransparentPadding))
                    {
                        roleSettings.TrimTransparentPadding = trimTransparentPadding;
                    }

                    hasRoleSettings = true;
                }

                if (hasRoleSettings)
                {
                    SaveRoleSettings(targetCollectionName, settings);
                }
            }
        }

        private static void ApplyRoleSettingAttribute(XElement element, string name, Action<double> apply)
        {
            double value;
            if (double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                apply(value);
            }
        }

        private static Dictionary<string, string> ReadSettingsDocument(XDocument document)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var settingsElement = document.Root?.Element("Settings");
            if (settingsElement == null)
            {
                return values;
            }

            foreach (var settingElement in settingsElement.Elements("Setting"))
            {
                var name = settingElement.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                values[name] = settingElement.Attribute("value")?.Value ?? string.Empty;
            }

            return values;
        }

        private static bool TryGetSettingValue(IDictionary<string, string> values, string name, out string value)
        {
            return values.TryGetValue(name, out value) && value != null;
        }

        private static void NormalizeActivationSettings(Properties.Settings settings)
        {
            var originalShakeEnabled = settings.ShakeActivationEnabled;
            var originalHotkeyEnabled = settings.HotkeyActivationEnabled;
            var originalMode = settings.HotkeyActivationMode;
            var originalModifiers = settings.HotkeyModifiers;
            var originalKey = settings.HotkeyKey;

            if (!settings.ShakeActivationEnabled && !settings.HotkeyActivationEnabled)
            {
                settings.ShakeActivationEnabled = true;
            }

            settings.HotkeyActivationMode = NormalizeHotkeyActivationMode(settings.HotkeyActivationMode);
            settings.HotkeyModifiers = NormalizeHotkeyModifiers(settings.HotkeyModifiers);
            settings.HotkeyKey = NormalizeHotkeyKey(settings.HotkeyKey);

            if (string.Equals(settings.HotkeyModifiers, "None", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(settings.HotkeyKey, "None", StringComparison.OrdinalIgnoreCase))
            {
                settings.HotkeyModifiers = "Control";
            }

            if (originalShakeEnabled != settings.ShakeActivationEnabled ||
                originalHotkeyEnabled != settings.HotkeyActivationEnabled ||
                !string.Equals(originalMode, settings.HotkeyActivationMode, StringComparison.Ordinal) ||
                !string.Equals(originalModifiers, settings.HotkeyModifiers, StringComparison.Ordinal) ||
                !string.Equals(originalKey, settings.HotkeyKey, StringComparison.Ordinal))
            {
                DebugLog.Write(
                    "Imported activation settings normalized: Shake=" +
                    (settings.ShakeActivationEnabled ? "On" : "Off") +
                    ", Hotkey=" +
                    (settings.HotkeyActivationEnabled ? "On" : "Off") +
                    ", Mode=" +
                    settings.HotkeyActivationMode +
                    ", Shortcut=" +
                    FormatHotkeyForLog(settings.HotkeyModifiers, settings.HotkeyKey));
            }
        }

        private static string FormatHotkeyForLog(string modifiers, string key)
        {
            var parts = new List<string>();
            var normalizedModifiers = NormalizeHotkeyModifiers(modifiers);
            if (!string.Equals(normalizedModifiers, "None", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var modifier in normalizedModifiers.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    parts.Add(string.Equals(modifier, "Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : modifier);
                }
            }

            var normalizedKey = NormalizeHotkeyKey(key);
            if (!string.Equals(normalizedKey, "None", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(normalizedKey.Length == 2 && normalizedKey[0] == 'D' && char.IsDigit(normalizedKey[1])
                    ? normalizedKey[1].ToString()
                    : normalizedKey);
            }

            return parts.Count == 0 ? "Ctrl" : string.Join("+", parts);
        }

        private static string NormalizeHotkeyActivationMode(string value)
        {
            return string.Equals(value, "Toggle", StringComparison.OrdinalIgnoreCase) ? "Toggle" : "Hold";
        }

        private static string NormalizeHotkeyModifiers(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "none":
                    return "None";
                case "alt":
                    return "Alt";
                case "shift":
                    return "Shift";
                case "control+shift":
                case "ctrl+shift":
                    return "Control+Shift";
                case "control+alt":
                case "ctrl+alt":
                    return "Control+Alt";
                case "alt+shift":
                    return "Alt+Shift";
                case "control+alt+shift":
                case "ctrl+alt+shift":
                    return "Control+Alt+Shift";
                default:
                    return "Control";
            }
        }

        private static string NormalizeHotkeyKey(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase))
            {
                return "None";
            }

            if (normalized.Length == 1)
            {
                var c = char.ToUpperInvariant(normalized[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    return c.ToString();
                }

                if (c >= '0' && c <= '9')
                {
                    return "D" + c;
                }
            }

            for (var i = 0; i <= 9; i++)
            {
                if (string.Equals(normalized, "D" + i.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    return "D" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            for (var i = 1; i <= 12; i++)
            {
                if (string.Equals(normalized, "F" + i.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    return "F" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (string.Equals(normalized, "Space", StringComparison.OrdinalIgnoreCase))
            {
                return "Space";
            }

            return string.Equals(normalized, "Escape", StringComparison.OrdinalIgnoreCase) ? "Escape" : "None";
        }

        private static Dictionary<string, string> InferAssignments(string collectionName)
        {
            var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var collectionPath = GetCollectionPath(collectionName);
            if (!Directory.Exists(collectionPath))
            {
                return assignments;
            }

            foreach (var file in Directory.GetFiles(collectionPath, "*.png", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName))
            {
                string roleKey;
                var fileName = Path.GetFileName(file);
                if (KnownFileNames.TryGetValue(fileName, out roleKey) && !assignments.ContainsKey(roleKey))
                {
                    assignments[roleKey] = fileName;
                }
            }

            return assignments;
        }

        private static string GetAssignedExistingFilePath(
            string collectionName,
            IDictionary<string, string> assignments,
            string roleKey)
        {
            string fileName;
            if (!assignments.TryGetValue(roleKey, out fileName) || string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var path = Path.Combine(GetCollectionPath(collectionName), Path.GetFileName(fileName));
            return File.Exists(path) ? path : null;
        }

        private static bool IsCollectionUsable(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return false;
            }

            try
            {
                var path = GetCollectionPath(collectionName);
                return Directory.Exists(path) &&
                       Directory.EnumerateFiles(path, "*.png", SearchOption.TopDirectoryOnly).Any();
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                DebugLog.WriteException("Cursor collection availability check failed: " + collectionName, ex);
                return false;
            }
        }

        private static string GetAssignmentsPath(string collectionName)
        {
            return Path.Combine(GetCollectionPath(collectionName), AssignmentFileName);
        }

        private static string GetRoleSettingsPath(string collectionName)
        {
            return Path.Combine(GetCollectionPath(collectionName), RoleSettingsFileName);
        }

        private static Dictionary<string, CursorRoleRenderSettings> CreateDefaultRoleSettings()
        {
            var settings = new Dictionary<string, CursorRoleRenderSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in Roles)
            {
                settings[role.Key] = new CursorRoleRenderSettings();
            }

            return settings;
        }

        private static string GetUserCollectionsRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AngryMouse",
                CursorCollectionsFolderName);
        }

        private static string GetBundledCollectionsRoot()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", CursorCollectionsFolderName);
        }

        private static string GetAvailableCollectionName(string baseName)
        {
            var candidate = baseName;
            var index = 2;
            while (Directory.Exists(GetCollectionPath(candidate)))
            {
                candidate = baseName + " " + index;
                index++;
            }

            return candidate;
        }

        private static string GetUniqueFileName(string directory, string fileName)
        {
            var safeFileName = Path.GetFileName(fileName);
            var candidate = safeFileName;
            var stem = Path.GetFileNameWithoutExtension(safeFileName);
            var extension = Path.GetExtension(safeFileName);
            var index = 2;

            while (File.Exists(Path.Combine(directory, candidate)))
            {
                candidate = stem + "-" + index + extension;
                index++;
            }

            return candidate;
        }

        private static string SanitizeCollectionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim()
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(chars);
        }

        private static Dictionary<string, string> CreateKnownFileNameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in Roles)
            {
                map[role.DefaultFileName] = role.Key;
            }

            map["left_ptr.png"] = "arrow";
            map["xterm.png"] = "ibeam";
            map["watch_0001.png"] = "wait";
            map["left_ptr_watch_0001.png"] = "appstarting";
            map["crosshair.png"] = "crosshair";
            map["sb_up_arrow.png"] = "uparrow";
            map["sb_v_double_arrow.png"] = "sizens";
            map["sb_h_double_arrow.png"] = "sizewe";
            map["bd_double_arrow.png"] = "sizenwse";
            map["fd_double_arrow.png"] = "sizenesw";
            map["all-scroll.png"] = "sizeall";
            map["crossed_circle.png"] = "no";
            map["hand2.png"] = "hand";
            map["question_arrow.png"] = "help";

            return map;
        }

        public sealed class SettingsPackageImportResult
        {
            public SettingsPackageImportResult()
            {
                CollectionNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public int ImportedCollectionCount { get; set; }

            public int RenamedCollectionCount { get; set; }

            public Dictionary<string, string> CollectionNameMap { get; }
        }
    }
}
