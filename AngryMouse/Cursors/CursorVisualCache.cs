using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    internal static class CursorVisualCache
    {
        private const int RuntimeTargetHeight = (int)CursorVisualLoader.BuiltInCursorHeight;
        private const int PreviewTargetHeight = 32;
        private const string CacheVersion = "v2";

        private static readonly object LockObject = new object();
        private static readonly Dictionary<string, CursorVisualInfo> RuntimeCache =
            new Dictionary<string, CursorVisualInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BitmapSource> PreviewCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public static CursorVisualInfo GetRuntimeVisual(string roleKey)
        {
            return GetRuntimeVisual(Properties.Settings.Default.CursorCollectionName, roleKey);
        }

        public static CursorVisualInfo GetRuntimeVisual(string collectionName, string roleKey)
        {
            var role = CursorCollectionManager.GetRole(roleKey);
            var path = CursorCollectionManager.ResolveRoleFilePath(collectionName, role.Key);

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return CursorVisualLoader.BuiltIn("Cursor collection file unavailable. Using built-in cursor.");
            }

            var roleSettings = CursorCollectionManager.GetRoleSettings(collectionName, role.Key);
            var key = "runtime|" + collectionName + "|" + role.Key + "|" +
                      CreateVisualCacheKey(path, RuntimeTargetHeight, role.Key, roleSettings);

            lock (LockObject)
            {
                CursorVisualInfo cached;
                if (RuntimeCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            try
            {
                var cachedBitmap = LoadRuntimeBitmap(path, collectionName, role, roleSettings);
                var hotspot = ScaleHotspot(path, role.Hotspot, roleSettings, cachedBitmap);
                var visual = new CursorVisualInfo(
                    cachedBitmap.Bitmap,
                    hotspot,
                    "Using cursor collection: " + Path.GetFileName(path));

                lock (LockObject)
                {
                    RuntimeCache[key] = visual;
                }

                return visual;
            }
            catch (Exception)
            {
                return CursorVisualLoader.BuiltIn("Cursor SVG failed to load. Using built-in cursor.");
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

            lock (LockObject)
            {
                BitmapSource cached;
                if (PreviewCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            try
            {
                var bitmap = GetCachedSvgBitmap(path, PreviewTargetHeight, roleKey, roleSettings).Bitmap;

                lock (LockObject)
                {
                    PreviewCache[key] = bitmap;
                }

                return bitmap;
            }
            catch (Exception)
            {
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

            var result = CursorVisualLoader.RenderSvgBitmap(path, targetHeight, roleSettings.TrimTransparentPadding);

            lock (LockObject)
            {
                if (File.Exists(cachePath) && File.Exists(metadataPath))
                {
                    return LoadCachedPng(cachePath, metadataPath);
                }

                SavePng(cachePath, result.Bitmap);
                SaveMetadata(metadataPath, result.CropLeft, result.CropTop, result.UncroppedWidth, result.UncroppedHeight);
            }

            return new CursorCachedBitmap(
                result.Bitmap,
                result.CropLeft,
                result.CropTop,
                result.UncroppedWidth,
                result.UncroppedHeight);
        }

        private static Point ScaleHotspot(
            string path,
            Point hotspot,
            CursorRoleRenderSettings roleSettings,
            CursorCachedBitmap cachedBitmap)
        {
            var viewport = CursorVisualLoader.ReadSvgViewport(path);
            var sourceWidth = viewport?.Width ?? 24;
            var sourceHeight = viewport?.Height ?? 24;

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
