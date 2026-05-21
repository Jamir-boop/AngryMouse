using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AngryMouse.Cursors
{
    internal static class CursorRenderPrewarmer
    {
        public static Task PrewarmCollectionAsync(
            string collectionName,
            IProgress<CursorPrewarmProgress> progress,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<object>();
            var thread = new Thread(() =>
            {
                try
                {
                    PrewarmCollection(collectionName, progress, cancellationToken);
                    completion.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.Name = "AngryMouse cursor prewarm";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return completion.Task;
        }

        private static void PrewarmCollection(
            string collectionName,
            IProgress<CursorPrewarmProgress> progress,
            CancellationToken cancellationToken)
        {
            var items = CreatePrewarmItems(collectionName);
            var total = items.Count;
            var completed = 0;

            Report(progress, completed, total, "", false);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    item.Render();
                }
                catch (Exception)
                {
                    // Bad cursor file should not stop other cursors from caching.
                }

                completed++;
                Report(progress, completed, total, item.Name, completed >= total);
            }
        }

        private static List<PrewarmItem> CreatePrewarmItems(string collectionName)
        {
            var items = new List<PrewarmItem>();
            var collectionPath = CursorCollectionManager.GetCollectionPath(collectionName);
            var assignments = CursorCollectionManager.LoadAssignments(collectionName);
            var assignedPreviewFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var role in CursorCollectionManager.Roles)
            {
                var roleKey = role.Key;
                items.Add(new PrewarmItem(role.DisplayName, () => CursorVisualCache.GetRuntimeVisual(collectionName, roleKey)));

                string assignedFile;
                if (!assignments.TryGetValue(role.Key, out assignedFile) || string.IsNullOrWhiteSpace(assignedFile))
                {
                    continue;
                }

                var assignedPath = Path.Combine(collectionPath, Path.GetFileName(assignedFile));
                if (!File.Exists(assignedPath))
                {
                    continue;
                }

                assignedPreviewFiles.Add(Path.GetFullPath(assignedPath));
                items.Add(new PrewarmItem(role.DisplayName + " preview", () => CursorVisualCache.GetPreview(collectionName, roleKey, assignedPath)));
            }

            if (Directory.Exists(collectionPath))
            {
                foreach (var file in Directory.GetFiles(collectionPath, "*.svg", SearchOption.TopDirectoryOnly)
                             .OrderBy(Path.GetFileName))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (assignedPreviewFiles.Contains(fullPath))
                    {
                        continue;
                    }

                    items.Add(new PrewarmItem(Path.GetFileName(file), () => CursorVisualCache.GetPreview(file)));
                }
            }

            return items;
        }

        private static void Report(
            IProgress<CursorPrewarmProgress> progress,
            int completed,
            int total,
            string currentItem,
            bool isComplete)
        {
            if (progress != null)
            {
                progress.Report(new CursorPrewarmProgress(completed, total, currentItem, isComplete));
            }
        }

        private sealed class PrewarmItem
        {
            public PrewarmItem(string name, Action render)
            {
                Name = name;
                Render = render;
            }

            public string Name { get; }

            public Action Render { get; }
        }
    }
}
