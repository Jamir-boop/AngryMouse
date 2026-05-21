using AngryMouse.Cursors;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Input = System.Windows.Input;

namespace AngryMouse
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow
    {
        private bool _loading = true;
        private CancellationTokenSource _prewarmCancellation;

        public SettingsWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called when the window is successfully loaded. Does view initialization.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CursorCollectionManager.InitializeDefaults();
            CursorTestItemsControl.ItemsSource = CreateCursorTestTiles();
            LoadSettingsToControls();
            _loading = false;
            StartPrewarmSelectedCollection();
        }

        private void LoadSettingsToControls()
        {
            _loading = true;

            SelectCursorSourceMode(GetSavedCursorSourceMode());
            DarkModeCheckBox.IsChecked = AppTheme.IsDarkMode(Properties.Settings.Default.ThemeMode);
            LoadCollectionsToControls();
            SizeSlider.Value = Properties.Settings.Default.CursorSize;
            AnimationLengthSlider.Value = Properties.Settings.Default.CursorAnimationLength;
            VisibleDurationSlider.Value = Properties.Settings.Default.CursorVisibleDuration;
            HideBuiltInCursorCheckBox.IsChecked = Properties.Settings.Default.HideBuiltInCursor;
            ShakeTrackingIntervalSlider.Value = Properties.Settings.Default.ShakeTrackingInterval;
            ShakeMinimumSpeedSlider.Value = Properties.Settings.Default.ShakeMinimumSpeed;
            ShakeMinimumTurnsSlider.Value = Properties.Settings.Default.ShakeMinimumTurns;

            UpdateCursorEditor(loadPreviews: false);
            UpdateCursorStatus();
            UpdateRemoveCollectionButton();
            UpdateCollectionUiAvailability();
        }

        private void LoadCollectionsToControls()
        {
            var collectionNames = CursorCollectionManager.GetCollectionNames();

            CollectionComboBox.Items.Clear();
            foreach (var collectionName in collectionNames)
            {
                CollectionComboBox.Items.Add(collectionName);
            }

            SelectCollection(Properties.Settings.Default.CursorCollectionName);
            LoadRemoveCollections(collectionNames, null);
        }

        private void LoadRemoveCollections(IEnumerable<string> collectionNames, string preferredCollectionName)
        {
            RemoveCollectionComboBox.Items.Clear();
            foreach (var collectionName in collectionNames.Where(CursorCollectionManager.CanRemoveCollection))
            {
                RemoveCollectionComboBox.Items.Add(collectionName);
            }

            SelectRemoveCollection(preferredCollectionName);
            UpdateRemoveCollectionButton();
        }

        private void SelectCursorSourceMode(string mode)
        {
            foreach (var item in CursorSourceComboBox.Items)
            {
                var comboBoxItem = item as ComboBoxItem;
                if (string.Equals(comboBoxItem?.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
                {
                    CursorSourceComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            CursorSourceComboBox.SelectedIndex = 0;
        }

        private void SelectCollection(string collectionName)
        {
            foreach (var item in CollectionComboBox.Items)
            {
                var itemText = item as string;
                if (string.Equals(itemText, collectionName, StringComparison.OrdinalIgnoreCase))
                {
                    CollectionComboBox.SelectedItem = itemText;
                    return;
                }
            }

            if (CollectionComboBox.Items.Count > 0)
            {
                CollectionComboBox.SelectedIndex = 0;
            }
        }

        private void SelectRemoveCollection(string collectionName)
        {
            if (!string.IsNullOrWhiteSpace(collectionName))
            {
                foreach (var item in RemoveCollectionComboBox.Items)
                {
                    var itemText = item as string;
                    if (string.Equals(itemText, collectionName, StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveCollectionComboBox.SelectedItem = itemText;
                        return;
                    }
                }
            }

            RemoveCollectionComboBox.SelectedIndex = RemoveCollectionComboBox.Items.Count > 0 ? 0 : -1;
        }

        private string GetCursorSourceMode()
        {
            var comboBoxItem = CursorSourceComboBox.SelectedItem as ComboBoxItem;
            return comboBoxItem?.Tag as string ?? CursorCollectionManager.CollectionMode;
        }

        private static string GetSavedCursorSourceMode()
        {
            return string.Equals(Properties.Settings.Default.CursorSourceMode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase)
                ? CursorCollectionManager.SystemMode
                : CursorCollectionManager.CollectionMode;
        }

        private string GetSelectedCollectionName()
        {
            return CollectionComboBox.SelectedItem as string ?? CursorCollectionManager.BundledAdwaitaName;
        }

        private string GetSelectedRemoveCollectionName()
        {
            return RemoveCollectionComboBox.SelectedItem as string;
        }

        private void CursorSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorSourceMode = GetCursorSourceMode();
            SaveSettings();
            UpdateCollectionUiAvailability();
            if (IsCollectionMode())
            {
                UpdateCursorEditor(loadPreviews: false);
            }

            UpdateCursorStatus();
            StartPrewarmSelectedCollection();
        }

        private void CollectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorCollectionName = GetSelectedCollectionName();
            SaveSettings();
            UpdateCursorEditor(loadPreviews: false);
            UpdateCursorStatus();
            UpdateRemoveCollectionButton();
            StartPrewarmSelectedCollection();
        }

        private void ImportCollectionButton_OnClick(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Import SVG cursor collection";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }

                var collectionName = CursorCollectionManager.ImportCollectionFolder(dialog.SelectedPath);
                Properties.Settings.Default.CursorCollectionName = collectionName;
                Properties.Settings.Default.CursorSourceMode = CursorCollectionManager.CollectionMode;
                SaveSettings();

                LoadSettingsToControls();
                _loading = false;
                StartPrewarmSelectedCollection();
            }
        }

        private void ImportSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "AngryMouse settings package (*.zip)|*.zip",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var result = CursorCollectionManager.ImportSettingsPackage(dialog.FileName);
                LoadSettingsToControls();
                _loading = false;
                StartPrewarmSelectedCollection();

                MessageBox.Show(
                    this,
                    "Imported settings and " + result.ImportedCollectionCount + " collection(s).",
                    "Import settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidDataException ||
                ex is InvalidOperationException ||
                ex is System.Xml.XmlException)
            {
                MessageBox.Show(this, "Import failed: " + ex.Message, "Import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "AngryMouse settings package (*.zip)|*.zip",
                DefaultExt = ".zip",
                FileName = "AngryMouse-settings.zip",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                CursorCollectionManager.ExportSettingsPackage(dialog.FileName);
                MessageBox.Show(this, "Exported settings package.", "Export settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException)
            {
                MessageBox.Show(this, "Export failed: " + ex.Message, "Export settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveCollectionButton_OnClick(object sender, RoutedEventArgs e)
        {
            var collectionName = GetSelectedRemoveCollectionName();
            if (!CursorCollectionManager.CanRemoveCollection(collectionName))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                "Remove cursor folder \"" + collectionName + "\"?",
                "Remove cursor folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var removedActiveCollection = string.Equals(
                collectionName,
                Properties.Settings.Default.CursorCollectionName,
                StringComparison.OrdinalIgnoreCase);
            CursorCollectionManager.RemoveCollection(collectionName);
            LoadSettingsToControls();
            _loading = false;
            if (removedActiveCollection)
            {
                StartPrewarmSelectedCollection();
            }
        }

        private void RemoveCollectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            UpdateRemoveCollectionButton();
        }

        private void DarkModeCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.ThemeMode = DarkModeCheckBox.IsChecked == true
                ? AppTheme.DarkMode
                : AppTheme.LightMode;
            SaveSettings();
            AppTheme.ApplySavedTheme();
        }

        private void TestTabButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedItem = TestTabItem;
        }

        private void CursorTestTile_OnMouseEnter(object sender, Input.MouseEventArgs e)
        {
            var tile = (sender as FrameworkElement)?.DataContext as CursorTestTile;
            if (tile == null)
            {
                return;
            }

            (Application.Current as App)?.BeginCursorTestPreview(tile.RoleKey);
        }

        private void CursorTestTile_OnMouseLeave(object sender, Input.MouseEventArgs e)
        {
            (Application.Current as App)?.EndCursorTestPreview();
        }

        private void ChangeRoleButton_OnClick(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as CursorRoleRow;
            if (row == null || string.IsNullOrWhiteSpace(row.RoleKey))
            {
                return;
            }

            var collectionName = GetSelectedCollectionName();
            var collectionPath = CursorCollectionManager.GetCollectionPath(collectionName);
            var dialog = new OpenFileDialog
            {
                Filter = "SVG files (*.svg)|*.svg",
                CheckFileExists = true,
                InitialDirectory = Directory.Exists(collectionPath) ? collectionPath : null
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var fileName = CursorCollectionManager.CopyFileIntoCollection(collectionName, dialog.FileName);
            var assignments = CursorCollectionManager.LoadAssignments(collectionName);
            assignments[row.RoleKey] = fileName;
            CursorCollectionManager.SaveAssignments(collectionName, assignments);

            UpdateCursorEditor(loadPreviews: false);
            UpdateCursorStatus();
            StartPrewarmSelectedCollection();
        }

        private void ClearRoleButton_OnClick(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as CursorRoleRow;
            if (row == null || string.IsNullOrWhiteSpace(row.RoleKey))
            {
                return;
            }

            var collectionName = GetSelectedCollectionName();
            var assignments = CursorCollectionManager.LoadAssignments(collectionName);
            assignments.Remove(row.RoleKey);
            CursorCollectionManager.SaveAssignments(collectionName, assignments);

            UpdateCursorEditor(loadPreviews: false);
            UpdateCursorStatus();
            StartPrewarmSelectedCollection();
        }

        private void AdjustRoleButton_OnClick(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as CursorRoleRow;
            if (row == null || string.IsNullOrWhiteSpace(row.RoleKey) || string.IsNullOrWhiteSpace(row.FilePath))
            {
                return;
            }

            var dialog = new CursorRoleAdjustWindow(GetSelectedCollectionName(), row.RoleKey, row.FilePath)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            UpdateCursorEditor(loadPreviews: false);
            UpdateCursorStatus();
            StartPrewarmSelectedCollection();
        }

        private void SizeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorSize = e.NewValue;
            SaveSettings();
        }

        private void AnimationLengthSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorAnimationLength = (int)e.NewValue;
            SaveSettings();
        }

        private void VisibleDurationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorVisibleDuration = (int)e.NewValue;
            SaveSettings();
        }

        private void HideBuiltInCursorCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.HideBuiltInCursor = HideBuiltInCursorCheckBox.IsChecked == true;
            SaveSettings();
        }

        private void ShakeTrackingIntervalSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.ShakeTrackingInterval = (int)e.NewValue;
            SaveSettings();
        }

        private void ShakeMinimumSpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.ShakeMinimumSpeed = Math.Round(e.NewValue, 1);
            SaveSettings();
        }

        private void ShakeMinimumTurnsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            Properties.Settings.Default.ShakeMinimumTurns = (int)e.NewValue;
            SaveSettings();
        }

        private void ResetDefaultsButton_OnClick(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Reset();
            CursorCollectionManager.InitializeDefaults();
            Properties.Settings.Default.Save();
            AppTheme.ApplySavedTheme();
            LoadSettingsToControls();
            _loading = false;
            StartPrewarmSelectedCollection();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            (Application.Current as App)?.EndCursorTestPreview();
            CancelPrewarm();
        }

        private void UpdateCursorEditor(bool loadPreviews)
        {
            if (!IsCollectionMode())
            {
                CursorRoleDataGrid.ItemsSource = null;
                return;
            }

            var collectionName = GetSelectedCollectionName();
            var collectionPath = CursorCollectionManager.GetCollectionPath(collectionName);
            var assignments = CursorCollectionManager.LoadAssignments(collectionName);
            var rows = new List<CursorRoleRow>();
            var assignedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var role in CursorCollectionManager.Roles)
            {
                string assignedFile;
                assignments.TryGetValue(role.Key, out assignedFile);

                var filePath = string.IsNullOrWhiteSpace(assignedFile)
                    ? null
                    : Path.Combine(collectionPath, Path.GetFileName(assignedFile));
                var exists = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
                if (exists)
                {
                    assignedFiles.Add(Path.GetFileName(filePath));
                }

                rows.Add(new CursorRoleRow
                {
                    RoleKey = role.Key,
                    Role = role.DisplayName,
                    FilePath = exists ? filePath : null,
                    AssignedFile = string.IsNullOrWhiteSpace(assignedFile) ? "" : Path.GetFileName(assignedFile),
                    Status = GetRoleStatus(assignedFile, exists),
                    Preview = loadPreviews && exists ? CursorVisualCache.GetPreview(collectionName, role.Key, filePath) : null,
                    CanAdjust = exists
                });
            }

            if (Directory.Exists(collectionPath))
            {
                foreach (var file in Directory.GetFiles(collectionPath, "*.svg", SearchOption.TopDirectoryOnly)
                             .OrderBy(Path.GetFileName))
                {
                    var fileName = Path.GetFileName(file);
                    if (!assignedFiles.Contains(fileName))
                    {
                        rows.Add(new CursorRoleRow
                        {
                            Role = "Unassigned",
                            AssignedFile = fileName,
                            Status = "Unassigned",
                            Preview = loadPreviews ? CursorVisualLoader.LoadSvgPreview(file) : null
                        });
                    }
                }
            }

            CursorRoleDataGrid.ItemsSource = rows;
        }

        private void StartPrewarmSelectedCollection()
        {
            CancelPrewarm();

            if (!IsCollectionMode())
            {
                HideCursorRenderStatus();
                return;
            }

            var collectionName = GetSelectedCollectionName();
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return;
            }

            var source = new CancellationTokenSource();
            _prewarmCancellation = source;

            CursorRenderProgressBar.Visibility = Visibility.Visible;
            CursorRenderProgressBar.IsIndeterminate = true;
            CursorRenderProgressBar.Value = 0;
            CursorRenderStatusTextBlock.Visibility = Visibility.Visible;
            CursorRenderStatusTextBlock.Text = "Rendering cursors 0/0";

            var progress = new Progress<CursorPrewarmProgress>(item =>
            {
                if (source.IsCancellationRequested)
                {
                    return;
                }

                CursorRenderProgressBar.IsIndeterminate = item.Total <= 0;
                CursorRenderProgressBar.Maximum = Math.Max(1, item.Total);
                CursorRenderProgressBar.Value = Math.Min(item.Completed, Math.Max(1, item.Total));
                CursorRenderStatusTextBlock.Text = "Rendering cursors " + item.Completed + "/" + item.Total;
            });

            CursorRenderPrewarmer.PrewarmCollectionAsync(collectionName, progress, source.Token)
                .ContinueWith(task =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var isCurrent = ReferenceEquals(_prewarmCancellation, source);
                        source.Dispose();

                        if (!isCurrent)
                        {
                            return;
                        }

                        _prewarmCancellation = null;
                        CursorRenderProgressBar.Visibility = Visibility.Collapsed;

                        if (task.IsFaulted)
                        {
                            var ignored = task.Exception;
                            CursorRenderStatusTextBlock.Visibility = Visibility.Visible;
                            CursorRenderStatusTextBlock.Text = "Cursor render failed.";
                            return;
                        }

                        CursorRenderStatusTextBlock.Visibility = Visibility.Collapsed;
                        CursorRenderStatusTextBlock.Text = "";
                        UpdateCursorEditor(loadPreviews: true);
                        UpdateCursorStatus();
                    }));
                }, TaskScheduler.Default);
        }

        private void CancelPrewarm()
        {
            var source = _prewarmCancellation;
            if (source == null)
            {
                return;
            }

            _prewarmCancellation = null;
            source.Cancel();
        }

        private void UpdateRemoveCollectionButton()
        {
            RemoveCollectionButton.IsEnabled = IsCollectionMode() && CursorCollectionManager.CanRemoveCollection(GetSelectedRemoveCollectionName());
        }

        private bool IsCollectionMode()
        {
            return string.Equals(GetCursorSourceMode(), CursorCollectionManager.CollectionMode, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateCollectionUiAvailability()
        {
            var collectionMode = IsCollectionMode();

            CollectionLabel.IsEnabled = collectionMode;
            CollectionComboBox.IsEnabled = collectionMode;
            ImportCollectionButton.IsEnabled = collectionMode;
            ImportSettingsButton.IsEnabled = collectionMode;
            ExportSettingsButton.IsEnabled = collectionMode;
            CollectionHelpTextBlock.Visibility = collectionMode ? Visibility.Visible : Visibility.Collapsed;
            RemoveCollectionLabel.IsEnabled = collectionMode;
            RemoveCollectionComboBox.IsEnabled = collectionMode;
            CursorRenderPanel.Visibility = collectionMode ? Visibility.Visible : Visibility.Collapsed;
            CursorRoleDataGrid.Visibility = collectionMode ? Visibility.Visible : Visibility.Collapsed;

            if (!collectionMode)
            {
                CancelPrewarm();
                HideCursorRenderStatus();
                CursorRoleDataGrid.ItemsSource = null;
            }

            UpdateRemoveCollectionButton();
        }

        private void HideCursorRenderStatus()
        {
            CursorRenderProgressBar.Visibility = Visibility.Collapsed;
            CursorRenderStatusTextBlock.Visibility = Visibility.Collapsed;
            CursorRenderStatusTextBlock.Text = "";
        }

        private void UpdateCursorStatus()
        {
            var mode = GetCursorSourceMode();
            if (string.Equals(mode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase))
            {
                var info = CursorVisualLoader.LoadSystemCursor();
                CursorStatusTextBlock.Text = info.HasBitmap
                    ? "Using active Windows cursor."
                    : "System cursor unavailable. Using selected collection fallback.";
                return;
            }

            var collectionName = GetSelectedCollectionName();
            var assignments = CursorCollectionManager.LoadAssignments(collectionName);
            var validCount = CursorCollectionManager.Roles.Count(role =>
            {
                string assignedFile;
                if (!assignments.TryGetValue(role.Key, out assignedFile) || string.IsNullOrWhiteSpace(assignedFile))
                {
                    return false;
                }

                return File.Exists(Path.Combine(CursorCollectionManager.GetCollectionPath(collectionName), Path.GetFileName(assignedFile)));
            });

            CursorStatusTextBlock.Text = "Using collection: " + collectionName + " (" + validCount + "/" + CursorCollectionManager.Roles.Length + " roles assigned).";
        }

        private static string GetRoleStatus(string assignedFile, bool exists)
        {
            if (string.IsNullOrWhiteSpace(assignedFile))
            {
                return "Unassigned";
            }

            return exists ? "Valid" : "Missing";
        }

        private static void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private static List<CursorTestTile> CreateCursorTestTiles()
        {
            return new List<CursorTestTile>
            {
                Tile("Arrow", "arrow", Input.Cursors.Arrow),
                Tile("I-beam", "ibeam", Input.Cursors.IBeam),
                Tile("Wait", "wait", Input.Cursors.Wait),
                Tile("App starting", "appstarting", Input.Cursors.AppStarting),
                Tile("Crosshair", "crosshair", Input.Cursors.Cross),
                Tile("Up arrow", "uparrow", Input.Cursors.UpArrow),
                Tile("Size NS", "sizens", Input.Cursors.SizeNS),
                Tile("Size WE", "sizewe", Input.Cursors.SizeWE),
                Tile("Size NWSE", "sizenwse", Input.Cursors.SizeNWSE),
                Tile("Size NESW", "sizenesw", Input.Cursors.SizeNESW),
                Tile("Size all", "sizeall", Input.Cursors.SizeAll),
                Tile("No", "no", Input.Cursors.No),
                Tile("Hand", "hand", Input.Cursors.Hand),
                Tile("Help", "help", Input.Cursors.Help)
            };
        }

        private static CursorTestTile Tile(string name, string roleKey, Input.Cursor cursor)
        {
            return new CursorTestTile
            {
                Name = name,
                RoleKey = roleKey,
                Cursor = cursor
            };
        }

        private sealed class CursorRoleRow
        {
            public string RoleKey { get; set; }

            public string Role { get; set; }

            public string FilePath { get; set; }

            public string AssignedFile { get; set; }

            public string Status { get; set; }

            public BitmapSource Preview { get; set; }

            public bool CanEditRole => !string.IsNullOrWhiteSpace(RoleKey);

            public bool CanAdjust { get; set; }

            public bool CanClear => CanEditRole && !string.IsNullOrWhiteSpace(AssignedFile);
        }

        private sealed class CursorTestTile
        {
            public string Name { get; set; }

            public string RoleKey { get; set; }

            public Input.Cursor Cursor { get; set; }
        }
    }
}
