using AngryMouse.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
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
        private const string PackageVersion = "2.12.2";
        private const int MaxPackageEntries = 1024;
        private const long MaxPackageEntryBytes = 64L * 1024 * 1024;
        private const long MaxPackageTotalBytes = 256L * 1024 * 1024;
        private const long MaxPackageCompressionRatio = 200;
        private const int MaximumCursorAnimationLength = 1000;

        // Hotspots are normalized (0-1) fractions of the cursor image, so they scale exactly to
        // any runtime bitmap size (CursorVisualCache.ScaleHotspot multiplies by the bitmap's pixel
        // dimensions). These were migrated from the original 24px design grid, e.g. 12/24 = 0.5.
        public static readonly CursorRoleDefinition[] Roles =
        {
            new CursorRoleDefinition("arrow", "Arrow", "arrow.png", 32512, new System.Windows.Point(0, 0)),
            new CursorRoleDefinition("ibeam", "I-beam", "ibeam.png", 32513, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("wait", "Wait", "wait.png", 32514, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("appstarting", "App starting", "appstarting.png", 32650, new System.Windows.Point(0, 0)),
            new CursorRoleDefinition("crosshair", "Crosshair", "crosshair.png", 32515, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("uparrow", "Up arrow", "uparrow.png", 32516, new System.Windows.Point(0.5, 0)),
            new CursorRoleDefinition("sizens", "Size NS", "sizens.png", 32645, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("sizewe", "Size WE", "sizewe.png", 32644, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("sizenwse", "Size NWSE", "sizenwse.png", 32642, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("sizenesw", "Size NESW", "sizenesw.png", 32643, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("sizeall", "Size all", "sizeall.png", 32646, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("no", "No", "no.png", 32648, new System.Windows.Point(0.5, 0.5)),
            new CursorRoleDefinition("hand", "Hand", "hand.png", 32649, new System.Windows.Point(0.25, 1.0 / 24.0)),
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
                TrySaveSettings("Default settings save failed");
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
            TrySaveSettings("Collection fallback settings save failed");
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

                var sourceFiles = Directory.GetFiles(bundledCollectionPath, "*.png", SearchOption.TopDirectoryOnly);
                if (sourceFiles.Length == 0)
                {
                    var renderedPath = Path.Combine(bundledCollectionPath, "Rendered");
                    if (Directory.Exists(renderedPath))
                    {
                        sourceFiles = Directory.GetFiles(renderedPath, "*.png", SearchOption.TopDirectoryOnly);
                    }
                }

                if (sourceFiles.Length == 0)
                {
                    continue;
                }

                if (Directory.Exists(targetPath) &&
                    Directory.GetFiles(targetPath, "*.png", SearchOption.TopDirectoryOnly).Length > 0 &&
                    BundledCollectionDiffers(targetPath, sourceFiles))
                {
                    BackupAndReplaceBundledCollection(collectionName, targetPath, sourceFiles);
                }

                Directory.CreateDirectory(targetPath);

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

        private static bool BundledCollectionDiffers(string targetPath, IEnumerable<string> sourceFiles)
        {
            var sources = sourceFiles.ToArray();
            var targets = Directory.GetFiles(targetPath, "*.png", SearchOption.TopDirectoryOnly)
                .ToDictionary(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            if (sources.Length != targets.Count)
            {
                return true;
            }

            foreach (var sourcePath in sources)
            {
                string targetPathForFile;
                if (!targets.TryGetValue(Path.GetFileName(sourcePath), out targetPathForFile) ||
                    new FileInfo(sourcePath).Length != new FileInfo(targetPathForFile).Length ||
                    !File.ReadAllBytes(sourcePath).SequenceEqual(File.ReadAllBytes(targetPathForFile)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void BackupAndReplaceBundledCollection(
            string collectionName,
            string targetPath,
            IEnumerable<string> sourceFiles)
        {
            var replacementPath = Path.Combine(
                GetUserCollectionsRoot(),
                ".bundled-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(replacementPath);
            try
            {
                foreach (var sourceFile in sourceFiles)
                {
                    File.Copy(sourceFile, Path.Combine(replacementPath, Path.GetFileName(sourceFile)));
                }

                var backupName = GetAvailableCollectionName(collectionName + " Backup");
                var backupPath = GetCollectionPath(backupName);
                Directory.Move(targetPath, backupPath);
                try
                {
                    Directory.Move(replacementPath, targetPath);
                }
                catch
                {
                    if (!Directory.Exists(targetPath) && Directory.Exists(backupPath))
                    {
                        Directory.Move(backupPath, targetPath);
                    }

                    throw;
                }

                DebugLog.Write("Bundled cursor collection refreshed; previous files backed up as " + backupName + ".");
            }
            finally
            {
                TryDeleteDirectory(replacementPath, "Bundled collection staging cleanup failed");
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
            var stagingPath = Path.Combine(GetUserCollectionsRoot(), ".import-" + Guid.NewGuid().ToString("N"));
            var sourceFiles = Directory.GetFiles(sourceFolder, "*.png", SearchOption.TopDirectoryOnly);
            if (sourceFiles.Length == 0)
            {
                throw new InvalidOperationException("Cursor collection folder contains no PNG files.");
            }

            var committed = false;
            try
            {
                Directory.CreateDirectory(stagingPath);
                foreach (var sourceFile in sourceFiles.OrderBy(Path.GetFileName))
                {
                    var targetFile = Path.Combine(stagingPath, Path.GetFileName(sourceFile));
                    File.Copy(sourceFile, targetFile);
                }

                Directory.Move(stagingPath, targetPath);
                committed = true;
                SaveAssignments(collectionName, InferAssignments(collectionName));
                return collectionName;
            }
            catch
            {
                if (committed)
                {
                    TryDeleteDirectory(targetPath, "Imported collection rollback failed");
                }

                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingPath, "Import staging cleanup failed");
            }
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

            var temporaryPath = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
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

                if (File.Exists(packagePath))
                {
                    File.Replace(temporaryPath, packagePath, null);
                }
                else
                {
                    File.Move(temporaryPath, packagePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
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
            var collectionsRoot = GetUserCollectionsRoot();
            Directory.CreateDirectory(collectionsRoot);
            var stagingRoot = Path.Combine(collectionsRoot, ".import-" + Guid.NewGuid().ToString("N"));
            var committedPaths = new List<string>();
            var roleSettingsSnapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long extractedBytes = 0;
            var settingsImportStarted = false;

            try
            {
                Directory.CreateDirectory(stagingRoot);
                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    ValidatePackageArchive(archive);

                    var settingsEntry = archive.GetEntry(PackageSettingsFileName);
                    if (settingsEntry != null)
                    {
                        using (var stream = new MemoryStream())
                        {
                            CopyPackageEntry(settingsEntry, stream, ref extractedBytes);
                            stream.Position = 0;
                            var xmlSettings = new XmlReaderSettings
                            {
                                DtdProcessing = DtdProcessing.Prohibit,
                                XmlResolver = null,
                                MaxCharactersInDocument = MaxPackageEntryBytes
                            };
                            using (var reader = XmlReader.Create(stream, xmlSettings))
                            {
                                settingsDocument = XDocument.Load(reader);
                            }
                        }
                    }

                    var collectionGroups = archive.Entries
                        .Where(IsPackageCollectionFile)
                        .GroupBy(GetPackageCollectionName, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (includeCollections)
                    {
                        var reservedNames = new HashSet<string>(GetCollectionNames(), StringComparer.OrdinalIgnoreCase);
                        foreach (var group in collectionGroups)
                        {
                            var sourceCollectionName = SanitizeCollectionName(group.Key);
                            if (string.IsNullOrWhiteSpace(sourceCollectionName))
                            {
                                sourceCollectionName = "Imported";
                            }

                            if (result.CollectionNameMap.ContainsKey(sourceCollectionName))
                            {
                                throw new InvalidDataException("Settings package contains duplicate collection names.");
                            }

                            var targetCollectionName = GetAvailableCollectionName(sourceCollectionName, reservedNames);
                            reservedNames.Add(targetCollectionName);
                            var targetPath = Path.Combine(stagingRoot, targetCollectionName);
                            Directory.CreateDirectory(targetPath);

                            var extractedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var entry in group.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
                            {
                                var fileName = GetPackageCollectionFileName(entry.FullName);
                                if (string.IsNullOrWhiteSpace(fileName))
                                {
                                    continue;
                                }

                                if (!extractedNames.Add(fileName))
                                {
                                    throw new InvalidDataException("Settings package contains duplicate collection files.");
                                }

                                ExtractPackageEntry(
                                    entry,
                                    Path.Combine(targetPath, fileName),
                                    ref extractedBytes);
                            }

                            result.CollectionNameMap[sourceCollectionName] = targetCollectionName;
                            result.ImportedCollectionCount++;
                            if (!string.Equals(sourceCollectionName, targetCollectionName, StringComparison.OrdinalIgnoreCase))
                            {
                                result.RenamedCollectionCount++;
                            }
                        }
                    }
                }

                roleSettingsSnapshots = CaptureImportedRoleSettings(settingsDocument, result.CollectionNameMap);

                foreach (var stagedPath in Directory.GetDirectories(stagingRoot).OrderBy(Path.GetFileName))
                {
                    var finalPath = GetCollectionPath(Path.GetFileName(stagedPath));
                    Directory.Move(stagedPath, finalPath);
                    committedPaths.Add(finalPath);
                }

                foreach (var targetCollectionName in result.CollectionNameMap.Values)
                {
                    if (!File.Exists(GetAssignmentsPath(targetCollectionName)))
                    {
                        SaveAssignments(targetCollectionName, InferAssignments(targetCollectionName));
                    }
                }

                if (settingsDocument != null)
                {
                    settingsImportStarted = true;
                    ApplyImportedSettings(settingsDocument, result.CollectionNameMap);
                }
            }
            catch
            {
                foreach (var committedPath in committedPaths)
                {
                    TryDeleteDirectory(committedPath, "Imported collection rollback failed");
                }

                RestoreRoleSettings(roleSettingsSnapshots);
                if (settingsImportStarted)
                {
                    TryReloadSettings();
                }
                if (committedPaths.Count > 0 || roleSettingsSnapshots.Count > 0)
                {
                    TryNotifyCollectionChanged();
                }
                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingRoot, "Import staging cleanup failed");
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
                TrySaveSettings("Removed collection settings save failed");
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
                    CreateSettingElement("HotkeyActivationMode", HotkeySettings.NormalizeActivationMode(settings.HotkeyActivationMode)),
                    CreateSettingElement("HotkeyActivationMethod", HotkeySettings.NormalizeActivationMethod(settings.HotkeyActivationMethod)),
                    CreateSettingElement("HotkeyDoubleControlRequireWindowsKey", settings.HotkeyDoubleControlRequireWindowsKey.ToString().ToLowerInvariant()),
                    CreateSettingElement("HotkeyModifiers", HotkeySettings.NormalizeModifiers(settings.HotkeyModifiers)),
                    CreateSettingElement("HotkeyKey", HotkeySettings.NormalizeKey(settings.HotkeyKey)),
                    CreateSettingElement("HideBuiltInCursor", settings.HideBuiltInCursor.ToString().ToLowerInvariant()),
                    CreateSettingElement("CursorSourceMode", settings.CursorSourceMode ?? string.Empty),
                    CreateSettingElement("CursorCollectionName", settings.CursorCollectionName ?? string.Empty),
                    CreateSettingElement("ThemeMode", settings.ThemeMode ?? string.Empty),
                    CreateSettingElement("StartMinimizedToTray", settings.StartMinimizedToTray.ToString().ToLowerInvariant()),
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
                        new XAttribute("hotspotOffsetY", settings.HotspotOffsetY.ToString(CultureInfo.InvariantCulture))));
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

        private static void ValidatePackageArchive(ZipArchive archive)
        {
            if (archive.Entries.Count > MaxPackageEntries)
            {
                throw new InvalidDataException("Settings package contains too many entries.");
            }

            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var components = NormalizePackagePath(entry.FullName)
                    .Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
                if (components.Any(component => component == "." || component == ".."))
                {
                    throw new InvalidDataException("Settings package contains an unsafe path.");
                }

                if (entry.Length > MaxPackageEntryBytes)
                {
                    throw new InvalidDataException("Settings package entry is too large.");
                }

                totalBytes = checked(totalBytes + entry.Length);
                if (totalBytes > MaxPackageTotalBytes)
                {
                    throw new InvalidDataException("Settings package is too large.");
                }

                if (entry.Length > 0 &&
                    (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > MaxPackageCompressionRatio))
                {
                    throw new InvalidDataException("Settings package compression ratio is unsafe.");
                }
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

        private static void ExtractPackageEntry(
            ZipArchiveEntry entry,
            string targetPath,
            ref long totalBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

            using (var target = File.Create(targetPath))
            {
                CopyPackageEntry(entry, target, ref totalBytes);
            }
        }

        private static void CopyPackageEntry(ZipArchiveEntry entry, Stream target, ref long totalBytes)
        {
            var buffer = new byte[81920];
            long entryBytes = 0;
            using (var source = entry.Open())
            {
                int bytesRead;
                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (entryBytes > MaxPackageEntryBytes - bytesRead ||
                        totalBytes > MaxPackageTotalBytes - bytesRead)
                    {
                        throw new InvalidDataException("Settings package expands beyond the allowed size.");
                    }

                    entryBytes += bytesRead;
                    totalBytes += bytesRead;
                    target.Write(buffer, 0, bytesRead);
                }
            }

            if (entryBytes != entry.Length)
            {
                throw new InvalidDataException("Settings package entry size is invalid.");
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
                settings.CursorSize = ClampImportedDouble(doubleValue, 2, 10, 6);
            }

            if (TryGetSettingValue(values, "CursorAnimationLength", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.CursorAnimationLength = NormalizeCursorAnimationLength(intValue);
            }

            if (TryGetSettingValue(values, "CursorVisibleDuration", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.CursorVisibleDuration = Clamp(intValue, 100, 3000);
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
                settings.HotkeyActivationMode = HotkeySettings.NormalizeActivationMode(stringValue);
            }

            // Packages created before 2.12.0 had only the regular shortcut method.
            settings.HotkeyActivationMethod = HotkeySettings.ShortcutMethod;
            settings.HotkeyDoubleControlRequireWindowsKey = false;
            if (TryGetSettingValue(values, "HotkeyActivationMethod", out stringValue))
            {
                settings.HotkeyActivationMethod = HotkeySettings.NormalizeActivationMethod(stringValue);
            }

            if (TryGetSettingValue(values, "HotkeyDoubleControlRequireWindowsKey", out stringValue) &&
                bool.TryParse(stringValue, out boolValue))
            {
                settings.HotkeyDoubleControlRequireWindowsKey = boolValue;
            }

            if (TryGetSettingValue(values, "HotkeyModifiers", out stringValue))
            {
                settings.HotkeyModifiers = HotkeySettings.NormalizeModifiers(stringValue);
            }

            if (TryGetSettingValue(values, "HotkeyKey", out stringValue))
            {
                settings.HotkeyKey = HotkeySettings.NormalizeKey(stringValue);
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

            if (TryGetSettingValue(values, "StartMinimizedToTray", out stringValue) &&
                bool.TryParse(stringValue, out boolValue))
            {
                settings.StartMinimizedToTray = boolValue;
            }

            if (TryGetSettingValue(values, "ShakeTrackingInterval", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.ShakeTrackingInterval = Clamp(intValue, 100, 2000);
            }

            if (TryGetSettingValue(values, "ShakeMinimumSpeed", out stringValue) &&
                double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                settings.ShakeMinimumSpeed = ClampImportedDouble(doubleValue, 0.2, 5, 1);
            }

            if (TryGetSettingValue(values, "ShakeMinimumTurns", out stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                settings.ShakeMinimumTurns = Clamp(intValue, 1, 12);
            }

            ApplyImportedRoleSettings(document, collectionNameMap);
            try
            {
                settings.Save();
            }
            catch (Exception ex) when (
                ex is System.Configuration.ConfigurationErrorsException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                DebugLog.WriteException("Imported settings save failed", ex);
                throw new IOException("Imported settings could not be saved.", ex);
            }
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

        private static int NormalizeCursorAnimationLength(int value)
        {
            var normalizedValue = Math.Min(
                MaximumCursorAnimationLength,
                CursorAnimationSettings.NormalizeLength(value));
            if (normalizedValue != value)
            {
                DebugLog.Write(
                    "Imported cursor animation length normalized: configuredMs=" +
                    value +
                    ", effectiveMs=" +
                    normalizedValue);
            }

            return normalizedValue;
        }

        private static double ClampImportedDouble(double value, double minimum, double maximum, double fallback)
        {
            if (double.IsNaN(value))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static void NormalizeActivationSettings(Properties.Settings settings)
        {
            var originalShakeEnabled = settings.ShakeActivationEnabled;
            var originalHotkeyEnabled = settings.HotkeyActivationEnabled;
            var originalMode = settings.HotkeyActivationMode;
            var originalMethod = settings.HotkeyActivationMethod;
            var originalModifiers = settings.HotkeyModifiers;
            var originalKey = settings.HotkeyKey;

            if (!settings.ShakeActivationEnabled && !settings.HotkeyActivationEnabled)
            {
                settings.ShakeActivationEnabled = true;
            }

            settings.HotkeyActivationMode = HotkeySettings.NormalizeActivationMode(settings.HotkeyActivationMode);
            settings.HotkeyActivationMethod = HotkeySettings.NormalizeActivationMethod(settings.HotkeyActivationMethod);
            settings.HotkeyModifiers = HotkeySettings.NormalizeModifiers(settings.HotkeyModifiers);
            settings.HotkeyKey = HotkeySettings.NormalizeKey(settings.HotkeyKey);

            if (HotkeySettings.IsEmpty(settings.HotkeyModifiers, settings.HotkeyKey))
            {
                settings.HotkeyModifiers = HotkeySettings.DefaultModifiers;
                settings.HotkeyKey = HotkeySettings.DefaultKey;
            }

            if (originalShakeEnabled != settings.ShakeActivationEnabled ||
                originalHotkeyEnabled != settings.HotkeyActivationEnabled ||
                !string.Equals(originalMode, settings.HotkeyActivationMode, StringComparison.Ordinal) ||
                !string.Equals(originalMethod, settings.HotkeyActivationMethod, StringComparison.Ordinal) ||
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
                    ", Method=" +
                    HotkeySettings.FormatActivationMethod(settings.HotkeyActivationMethod) +
                    ", WinGuard=" +
                    (settings.HotkeyDoubleControlRequireWindowsKey ? "On" : "Off") +
                    ", Shortcut=" +
                    HotkeySettings.FormatDisplay(settings.HotkeyModifiers, settings.HotkeyKey));
            }
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
            return GetAvailableCollectionName(baseName, null);
        }

        private static string GetAvailableCollectionName(string baseName, ISet<string> reservedNames)
        {
            var candidate = baseName;
            var index = 2;
            while (Directory.Exists(GetCollectionPath(candidate)) ||
                   (reservedNames != null && reservedNames.Contains(candidate)))
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
            var sanitized = new string(chars);
            return sanitized == "." || sanitized == ".." ? string.Empty : sanitized;
        }

        private static bool TrySaveSettings(string context)
        {
            try
            {
                Properties.Settings.Default.Save();
                return true;
            }
            catch (Exception ex) when (
                ex is System.Configuration.ConfigurationErrorsException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                DebugLog.WriteException(context, ex);
                return false;
            }
        }

        private static Dictionary<string, byte[]> CaptureImportedRoleSettings(
            XDocument document,
            IDictionary<string, string> collectionNameMap)
        {
            var snapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var root = document?.Root?.Element("CursorRoleSettings");
            if (root == null)
            {
                return snapshots;
            }

            foreach (var collectionElement in root.Elements("Collection"))
            {
                var sourceName = SanitizeCollectionName(collectionElement.Attribute("name")?.Value);
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    continue;
                }

                string targetName;
                if (!collectionNameMap.TryGetValue(sourceName, out targetName))
                {
                    targetName = sourceName;
                }

                if (!Directory.Exists(GetCollectionPath(targetName)))
                {
                    continue;
                }

                var path = GetRoleSettingsPath(targetName);
                if (!snapshots.ContainsKey(path))
                {
                    snapshots[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
                }
            }

            return snapshots;
        }

        private static void RestoreRoleSettings(IDictionary<string, byte[]> snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                try
                {
                    if (snapshot.Value == null)
                    {
                        File.Delete(snapshot.Key);
                    }
                    else
                    {
                        File.WriteAllBytes(snapshot.Key, snapshot.Value);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    DebugLog.WriteException("Imported role settings rollback failed: " + snapshot.Key, ex);
                }
            }
        }

        private static void TryReloadSettings()
        {
            try
            {
                Properties.Settings.Default.Reload();
            }
            catch (Exception ex) when (
                ex is System.Configuration.ConfigurationErrorsException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                DebugLog.WriteException("Settings reload after failed import failed", ex);
            }
        }

        private static void TryNotifyCollectionChanged()
        {
            try
            {
                NotifyCollectionChanged();
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Collection rollback notification failed", ex);
            }
        }

        private static void TryDeleteDirectory(string path, string context)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteException(context + ": " + path, ex);
            }
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
