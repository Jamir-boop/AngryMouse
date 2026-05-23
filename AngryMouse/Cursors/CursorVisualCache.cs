using AngryMouse.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    internal static class CursorVisualCache
    {
        private const int RuntimeTargetHeight = (int)CursorVisualLoader.BuiltInCursorHeight;
        private const int PreviewTargetHeight = 32;
        private const string CacheVersion = "v2";
        private const int MaxRuntimeCacheSize = 100;
        private const int MaxPreviewCacheSize = 100;
        private const int DiskCacheCleanupDays = 30;

        private static readonly object LockObject = new object();
        private static readonly object RuntimeLock = new object();
        private static readonly object PreviewLock = new object();
        private static readonly LinkedList<string> RuntimeCacheOrder = new LinkedList<string>();
        private static readonly Dictionary<string, CursorVisualInfo> RuntimeCache =
            new Dictionary<string, CursorVisualInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> PreviewCacheOrder = new LinkedList<string>();
        private static readonly Dictionary<string, BitmapSource> PreviewCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        static CursorVisualCache()
        {
            CleanupDiskCache();
        }

        private static void CleanupDiskCache()
        {
            try
            {
                var cacheRoot = GetDiskCacheRoot();
                if (Directory.Exists(cacheRoot))
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-DiskCacheCleanupDays);
                    var files = Directory.EnumerateFiles(cacheRoot, "*.png", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        try
                        {
                            var lastWriteTime = File.GetLastWriteTimeUtc(file);
                            if (lastWriteTime < cutoffDate)
                            {
                                File.Delete(file);
                                // Also delete associated metadata file
                                var metadataFile = file + ".txt";
                                if (File.Exists(metadataFile))
                                {
                                    File.Delete(metadataFile);
                                }
                            }
                        }
                        catch (IOException)
                        {
                            // File might be in use, skip it
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip files we can't access
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Cursor disk cache cleanup failed", ex);
                // If cleanup fails, continue without it
            }
        }

        public static CursorVisualInfo GetRuntimeVisual(string roleKey)
        {
            return GetRuntimeVisual(Properties.Settings.Default.CursorCollectionName, roleKey);
        }

        public static CursorVisualInfo GetRuntimeVisual(string collectionName, string roleKey)
        {
            var role = CursorCollectionManager.GetRole(roleKey);
            var path = CursorCollectionManager.ResolveRoleFilePath(collectionName, role.Key);
            var roleSettings = CursorCollectionManager.GetRoleSettings(collectionName, role.Key);

            return GetRuntimeVisual(collectionName, roleKey, path, roleSettings);
        }

        public static CursorVisualInfo GetRuntimeVisual(
            string collectionName,
            string roleKey,
            string path,
            CursorRoleRenderSettings roleSettings)
        {
            var role = CursorCollectionManager.GetRole(roleKey);

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return CursorVisualLoader.BuiltIn("Cursor collection file unavailable. Using built-in cursor.");
            }

            var settings = roleSettings ?? new CursorRoleRenderSettings();
            var key = "runtime|" + collectionName + "|" + role.Key + "|" +
                      CreateVisualCacheKey(path, RuntimeTargetHeight, role.Key, settings);

            lock (RuntimeLock)
            {
                CursorVisualInfo cached;
                if (RuntimeCache.TryGetValue(key, out cached))
                {
                    // Move to end of LRU list
                    RuntimeCacheOrder.Remove(key);
                    RuntimeCacheOrder.AddLast(key);
                    return cached;
                }
            }

            try
            {
                var cachedBitmap = LoadRuntimeBitmap(path, collectionName, role, settings);
                if (cachedBitmap == null || cachedBitmap.Bitmap == null)
                {
                    return CursorVisualLoader.BuiltIn("Cursor PNG failed to load. Using built-in cursor.");
                }

                var hotspot = ScaleHotspot(path, role.Hotspot, settings, cachedBitmap);
                var visual = new CursorVisualInfo(
                    cachedBitmap.Bitmap,
                    hotspot,
                    "Using cursor collection: " + Path.GetFileName(path));

                lock (RuntimeLock)
                {
                    // Evict if cache is full
                    if (RuntimeCache.Count >= MaxRuntimeCacheSize)
                    {
                        var oldestKey = RuntimeCacheOrder.First.Value;
                        RuntimeCache.Remove(oldestKey);
                        RuntimeCacheOrder.RemoveFirst();
                    }

                    RuntimeCache[key] = visual;
                    RuntimeCacheOrder.AddLast(key);
                }

                return visual;
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Cursor runtime visual failed: " + path, ex);
                return CursorVisualLoader.BuiltIn("Cursor PNG failed to load. Using built-in cursor.");
            }
        }

        public static BitmapSource GetPreview(string path)
        {
            return GetPreview(path, "preview", new CursorRoleRenderSettings());
        }

        public static BitmapSource GetPreview(string collectionName, string roleKey, string path)
        {
            return GetPreview(path, roleKey, CursorCollectionManager.GetRoleSettings(collectionName, roleKey));
        }

        public static CursorVisualInfo GetPreviewVisual(
            string collectionName,
            string roleKey,
            string path,
            CursorRoleRenderSettings roleSettings)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return CursorVisualLoader.BuiltIn("Cursor collection file unavailable. Using built-in cursor.");
            }

            var role = CursorCollectionManager.GetRole(roleKey);
            var settings = roleSettings ?? new CursorRoleRenderSettings();
            var cachedBitmap = GetCachedSvgBitmap(path, PreviewTargetHeight, role.Key, settings);
            var hotspot = ScaleHotspot(path, role.Hotspot, settings, cachedBitmap);
            return new CursorVisualInfo(cachedBitmap.Bitmap, hotspot, "Preview");
        }

        public static CursorCachedBitmap GetPreviewBitmap(string path, bool trimTransparentPadding)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var settings = new CursorRoleRenderSettings(0, 0, trimTransparentPadding);
            return GetCachedSvgBitmap(path, PreviewTargetHeight, "preview", settings);
        }

        public static Point GetPreviewHotspot(
            string path,
            Point roleHotspot,
            CursorRoleRenderSettings roleSettings,
            CursorCachedBitmap cachedBitmap)
        {
            return ScaleHotspot(path, roleHotspot, roleSettings ?? new CursorRoleRenderSettings(), cachedBitmap);
        }

        public static void ClearMemory()
        {
            lock (LockObject)
            {
                RuntimeCache.Clear();
                PreviewCache.Clear();
            }
        }

        private static BitmapSource GetPreview(string path, string roleKey, CursorRoleRenderSettings roleSettings)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var key = "preview|" + CreateBitmapCacheKey(path, PreviewTargetHeight, roleSettings);

            lock (PreviewLock)
            {
                BitmapSource cached;
                if (PreviewCache.TryGetValue(key, out cached))
                {
                    // Move to end of LRU list
                    PreviewCacheOrder.Remove(key);
                    PreviewCacheOrder.AddLast(key);
                    return cached;
                }
            }

            try
            {
                var bitmap = GetCachedSvgBitmap(path, PreviewTargetHeight, roleKey, roleSettings).Bitmap;

                lock (PreviewLock)
                {
                    // Evict if cache is full
                    if (PreviewCache.Count >= MaxPreviewCacheSize)
                    {
                        var oldestKey = PreviewCacheOrder.First.Value;
                        PreviewCache.Remove(oldestKey);
                        PreviewCacheOrder.RemoveFirst();
                    }

                    PreviewCache[key] = bitmap;
                    PreviewCacheOrder.AddLast(key);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Cursor preview failed: " + path, ex);
                return null;
            }
        }

        private static CursorCachedBitmap LoadRuntimeBitmap(
            string path,
            string collectionName,
            CursorRoleDefinition role,
            CursorRoleRenderSettings roleSettings)
        {
            var bundledPng = GetBundledRenderedPng(path, collectionName, role, roleSettings);
            if (!string.IsNullOrWhiteSpace(bundledPng) && File.Exists(bundledPng))
            {
                var bitmap = LoadPng(bundledPng);
                return new CursorCachedBitmap(bitmap, 0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            }

            return GetCachedSvgBitmap(path, RuntimeTargetHeight, role.Key, roleSettings);
        }

        private static CursorCachedBitmap GetCachedSvgBitmap(
            string path,
            int targetHeight,
            string roleKey,
            CursorRoleRenderSettings roleSettings)
        {
            var cachePath = GetDiskCachePath(path, targetHeight, roleKey, roleSettings);
            var metadataPath = cachePath + ".txt";
            lock (LockObject)
            {
                if (File.Exists(cachePath) && File.Exists(metadataPath))
                {
                    return LoadCachedPng(cachePath, metadataPath);
                }
            }

            BitmapSource bitmap = null;
            int cropLeft = 0;
            int cropTop = 0;
            int uncroppedWidth = 0;
            int uncroppedHeight = 0;

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    var frame = decoder.Frames.FirstOrDefault();
                    if (frame == null)
                    {
                        return null;
                    }

                    if (targetHeight <= 0)
                    {
                        bitmap = frame;
                        uncroppedWidth = frame.PixelWidth;
                        uncroppedHeight = frame.PixelHeight;
                    }
                    else
                    {
                        var scale = targetHeight / (double)frame.PixelHeight;
                        var width = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
                        var height = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));

                        bitmap = new TransformedBitmap(
                            frame,
                            new ScaleTransform(scale, scale));
                        uncroppedWidth = width;
                        uncroppedHeight = height;
                    }

                    bitmap.Freeze();
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Cursor bitmap load failed: " + path, ex);
                return null;
            }

            if (bitmap == null)
            {
                return null;
            }

            lock (LockObject)
            {
                if (File.Exists(cachePath) && File.Exists(metadataPath))
                {
                    return LoadCachedPng(cachePath, metadataPath);
                }

                SavePng(cachePath, bitmap);
                SaveMetadata(metadataPath, cropLeft, cropTop, uncroppedWidth, uncroppedHeight);
            }

            return new CursorCachedBitmap(
                bitmap,
                cropLeft,
                cropTop,
                uncroppedWidth,
                uncroppedHeight);
        }

        private static Point ScaleHotspot(
            string path,
            Point hotspot,
            CursorRoleRenderSettings roleSettings,
            CursorCachedBitmap cachedBitmap)
        {
            // For PNG files, use the image dimensions as source width/height
            var sourceWidth = cachedBitmap?.UncroppedWidth ?? 24;
            var sourceHeight = cachedBitmap?.UncroppedHeight ?? 24;

            if (sourceWidth <= 0 || sourceHeight <= 0 || cachedBitmap == null)
            {
                return hotspot;
            }

            var settings = roleSettings ?? new CursorRoleRenderSettings();
            return new Point(
                (hotspot.X + settings.HotspotOffsetX) * cachedBitmap.UncroppedWidth / sourceWidth - cachedBitmap.CropLeft,
                (hotspot.Y + settings.HotspotOffsetY) * cachedBitmap.UncroppedHeight / sourceHeight - cachedBitmap.CropTop);
        }

        private static string GetBundledRenderedPng(
            string path,
            string collectionName,
            CursorRoleDefinition role,
            CursorRoleRenderSettings roleSettings)
        {
            if (roleSettings.TrimTransparentPadding)
            {
                return null;
            }

            if (!string.Equals(collectionName, CursorCollectionManager.BundledAdwaitaName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(Path.GetFileName(path), role.DefaultFileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bundledSvg = CursorCollectionManager.GetBundledCollectionFilePath(
                CursorCollectionManager.BundledAdwaitaName,
                role.DefaultFileName);
            if (!File.Exists(bundledSvg) ||
                new FileInfo(path).Length != new FileInfo(bundledSvg).Length)
            {
                return null;
            }

            return CursorCollectionManager.GetBundledRenderedPngPath(role.DefaultFileName);
        }

        private static CursorCachedBitmap LoadCachedPng(string cachePath, string metadataPath)
        {
            var bitmap = LoadPng(cachePath);
            var metadata = LoadMetadata(metadataPath, bitmap.PixelWidth, bitmap.PixelHeight);
            return new CursorCachedBitmap(
                bitmap,
                metadata.CropLeft,
                metadata.CropTop,
                metadata.UncroppedWidth,
                metadata.UncroppedHeight);
        }

        private static BitmapSource LoadPng(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static void SavePng(string cachePath, BitmapSource bitmap)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (var stream = File.Create(tempPath))
                {
                    encoder.Save(stream);
                }

                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                File.Move(tempPath, cachePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void SaveMetadata(
            string metadataPath,
            int cropLeft,
            int cropTop,
            int uncroppedWidth,
            int uncroppedHeight)
        {
            var lines = new[]
            {
                "cropLeft=" + cropLeft.ToString(CultureInfo.InvariantCulture),
                "cropTop=" + cropTop.ToString(CultureInfo.InvariantCulture),
                "uncroppedWidth=" + uncroppedWidth.ToString(CultureInfo.InvariantCulture),
                "uncroppedHeight=" + uncroppedHeight.ToString(CultureInfo.InvariantCulture)
            };

            File.WriteAllLines(metadataPath, lines);
        }

        private static CacheMetadata LoadMetadata(string metadataPath, int fallbackWidth, int fallbackHeight)
        {
            var metadata = new CacheMetadata
            {
                CropLeft = 0,
                CropTop = 0,
                UncroppedWidth = fallbackWidth,
                UncroppedHeight = fallbackHeight
            };

            foreach (var line in File.ReadAllLines(metadataPath))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separatorIndex).Trim();
                var value = line.Substring(separatorIndex + 1).Trim();
                int intValue;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                {
                    continue;
                }

                if (string.Equals(key, "cropLeft", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.CropLeft = intValue;
                }
                else if (string.Equals(key, "cropTop", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.CropTop = intValue;
                }
                else if (string.Equals(key, "uncroppedWidth", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.UncroppedWidth = intValue;
                }
                else if (string.Equals(key, "uncroppedHeight", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.UncroppedHeight = intValue;
                }
            }

            return metadata;
        }

        private static string GetDiskCachePath(
            string path,
            int targetHeight,
            string roleKey,
            CursorRoleRenderSettings roleSettings)
        {
            return Path.Combine(GetDiskCacheRoot(), CreateBitmapCacheKey(path, targetHeight, roleSettings) + ".png");
        }

        private static string GetDiskCacheRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AngryMouse",
                "CursorBitmapCache",
                CacheVersion);
        }

        private static string CreateBitmapCacheKey(
            string path,
            int targetHeight,
            CursorRoleRenderSettings roleSettings)
        {
            var info = new FileInfo(path);
            var settings = roleSettings ?? new CursorRoleRenderSettings();
            var rawKey = string.Join(
                "|",
                Path.GetFullPath(path).ToLowerInvariant(),
                info.Length.ToString(CultureInfo.InvariantCulture),
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                targetHeight.ToString(CultureInfo.InvariantCulture),
                settings.TrimTransparentPadding.ToString());

            return ComputeHash(rawKey);
        }

        private static string CreateVisualCacheKey(
            string path,
            int targetHeight,
            string roleKey,
            CursorRoleRenderSettings roleSettings)
        {
            var info = new FileInfo(path);
            var settings = roleSettings ?? new CursorRoleRenderSettings();
            var rawKey = string.Join(
                "|",
                Path.GetFullPath(path).ToLowerInvariant(),
                info.Length.ToString(CultureInfo.InvariantCulture),
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                targetHeight.ToString(CultureInfo.InvariantCulture),
                roleKey ?? string.Empty,
                settings.TrimTransparentPadding.ToString(),
                settings.HotspotOffsetX.ToString("R", CultureInfo.InvariantCulture),
                settings.HotspotOffsetY.ToString("R", CultureInfo.InvariantCulture));

            return ComputeHash(rawKey);
        }

        private static string ComputeHash(string rawKey)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private struct CacheMetadata
        {
            public int CropLeft;
            public int CropTop;
            public int UncroppedWidth;
            public int UncroppedHeight;
        }
    }
}
