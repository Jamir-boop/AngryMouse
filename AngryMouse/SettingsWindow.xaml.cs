using AngryMouse.Cursors;
using AngryMouse.Util;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private readonly DispatcherTimer _debounceTimer;
        private bool _needsCursorEditorUpdate;
        private bool _needsCursorStatusUpdate;
        private bool _needsRemoveCollectionButtonUpdate;
        private bool _needsCollectionUiAvailabilityUpdate;
        private bool _needsPrewarmRestart;
        private readonly Dictionary<Slider, ToolTip> _sliderValueToolTips = new Dictionary<Slider, ToolTip>();
        private readonly Dictionary<Slider, string> _sliderValueToolTipFormats = new Dictionary<Slider, string>();
        private readonly HashSet<Slider> _activeSliderValueToolTips = new HashSet<Slider>();
        private static readonly string[] CursorSettingNames =
        {
            "CursorSize",
            "CursorAnimationLength",
            "CursorVisibleDuration",
            "HideBuiltInCursor"
        };

        private static readonly string[] ShakeSettingNames =
        {
            "ShakeActivationEnabled",
            "HotkeyActivationEnabled",
            "HotkeyActivationMode",
            "HotkeyModifiers",
            "HotkeyKey",
            "ShakeTrackingInterval",
            "ShakeMinimumSpeed",
            "ShakeMinimumTurns"
        };

        public SettingsWindow()
        {
            InitializeComponent();
            LoadActivationComboItems();
            Title = App.DisplayNameWithVersion;
            VersionTextBlock.Text = "jamir-boop - AngryCursor " + App.DisplayVersion;
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;
            ConfigureSliderValueToolTips();
        }

        /// <summary>
        /// Called when the window is successfully loaded. Does view initialization.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CursorCollectionManager.InitializeDefaults();
            LoadSettingsToControls();
            _loading = false;
            ScheduleDebouncedUpdate();
            StartPrewarmSelectedCollection();
        }

        private void LoadSettingsToControls()
        {
            _loading = true;

            SelectCursorSourceMode(GetSavedCursorSourceMode());
            DarkModeCheckBox.IsChecked = AppTheme.IsDarkMode(Properties.Settings.Default.ThemeMode);
            DebugEnabledCheckBox.IsChecked = Properties.Settings.Default.DebugEnabled;
            LoadCollectionsToControls();
            SizeSlider.Value = Properties.Settings.Default.CursorSize;
            AnimationLengthSlider.Value = Properties.Settings.Default.CursorAnimationLength;
            VisibleDurationSlider.Value = Properties.Settings.Default.CursorVisibleDuration;
            HideBuiltInCursorCheckBox.IsChecked = Properties.Settings.Default.HideBuiltInCursor;
            SyncHideBuiltInCursorAvailability(saveWhenChanged: true);
            ShakeActivationEnabledCheckBox.IsChecked = Properties.Settings.Default.ShakeActivationEnabled;
            HotkeyActivationEnabledCheckBox.IsChecked = Properties.Settings.Default.HotkeyActivationEnabled;
            EnsureActivationSourceSelected();
            SelectComboBoxItemByTag(HotkeyActivationModeComboBox, NormalizeHotkeyActivationMode(Properties.Settings.Default.HotkeyActivationMode));
            SelectComboBoxItemByTag(HotkeyModifiersComboBox, NormalizeHotkeyModifiers(Properties.Settings.Default.HotkeyModifiers));
            SelectComboBoxItemByTag(HotkeyKeyComboBox, NormalizeHotkeyKey(Properties.Settings.Default.HotkeyKey));
            EnsureValidHotkeySelection();
            UpdateActivationUiAvailability();
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

        private void LoadActivationComboItems()
        {
            HotkeyActivationModeComboBox.Items.Clear();
            AddComboBoxItem(HotkeyActivationModeComboBox, "Hold", "Hold");
            AddComboBoxItem(HotkeyActivationModeComboBox, "Toggle", "Toggle");

            HotkeyModifiersComboBox.Items.Clear();
            AddComboBoxItem(HotkeyModifiersComboBox, "None", "None");
            AddComboBoxItem(HotkeyModifiersComboBox, "Control", "Control");
            AddComboBoxItem(HotkeyModifiersComboBox, "Alt", "Alt");
            AddComboBoxItem(HotkeyModifiersComboBox, "Shift", "Shift");
            AddComboBoxItem(HotkeyModifiersComboBox, "Control+Shift", "Control+Shift");
            AddComboBoxItem(HotkeyModifiersComboBox, "Control+Alt", "Control+Alt");
            AddComboBoxItem(HotkeyModifiersComboBox, "Alt+Shift", "Alt+Shift");
            AddComboBoxItem(HotkeyModifiersComboBox, "Control+Alt+Shift", "Control+Alt+Shift");

            HotkeyKeyComboBox.Items.Clear();
            AddComboBoxItem(HotkeyKeyComboBox, "None", "None");
            for (var key = 'A'; key <= 'Z'; key++)
            {
                AddComboBoxItem(HotkeyKeyComboBox, key.ToString(), key.ToString());
            }

            for (var key = 0; key <= 9; key++)
            {
                AddComboBoxItem(HotkeyKeyComboBox, key.ToString(), "D" + key.ToString(CultureInfo.InvariantCulture));
            }

            for (var key = 1; key <= 12; key++)
            {
                var name = "F" + key.ToString(CultureInfo.InvariantCulture);
                AddComboBoxItem(HotkeyKeyComboBox, name, name);
            }

            AddComboBoxItem(HotkeyKeyComboBox, "Space", "Space");
            AddComboBoxItem(HotkeyKeyComboBox, "Escape", "Escape");
        }

        private static void AddComboBoxItem(ComboBox comboBox, string content, string tag)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = content,
                Tag = tag
            });
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            foreach (var item in comboBox.Items)
            {
                var comboBoxItem = item as ComboBoxItem;
                if (string.Equals(comboBoxItem?.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }

        private static string GetSelectedComboBoxTag(ComboBox comboBox, string fallback)
        {
            var comboBoxItem = comboBox.SelectedItem as ComboBoxItem;
            return comboBoxItem?.Tag as string ?? fallback;
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

        private void EnsureActivationSourceSelected()
        {
            if (ShakeActivationEnabledCheckBox.IsChecked != true && HotkeyActivationEnabledCheckBox.IsChecked != true)
            {
                ShakeActivationEnabledCheckBox.IsChecked = true;
            }
        }

        private void EnsureValidHotkeySelection()
        {
            var modifiers = GetSelectedComboBoxTag(HotkeyModifiersComboBox, "Control");
            var key = GetSelectedComboBoxTag(HotkeyKeyComboBox, "None");
            if (string.Equals(modifiers, "None", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(key, "None", StringComparison.OrdinalIgnoreCase))
            {
                SelectComboBoxItemByTag(HotkeyModifiersComboBox, "Control");
            }
        }

        private void SaveActivationSettings()
        {
            EnsureActivationSourceSelected();
            EnsureValidHotkeySelection();

            Properties.Settings.Default.ShakeActivationEnabled = ShakeActivationEnabledCheckBox.IsChecked == true;
            Properties.Settings.Default.HotkeyActivationEnabled = HotkeyActivationEnabledCheckBox.IsChecked == true;
            Properties.Settings.Default.HotkeyActivationMode = GetSelectedComboBoxTag(HotkeyActivationModeComboBox, "Hold");
            Properties.Settings.Default.HotkeyModifiers = GetSelectedComboBoxTag(HotkeyModifiersComboBox, "Control");
            Properties.Settings.Default.HotkeyKey = GetSelectedComboBoxTag(HotkeyKeyComboBox, "None");
            SaveSettings();
            UpdateActivationUiAvailability();
        }

        private void UpdateActivationUiAvailability()
        {
            var shakeEnabled = ShakeActivationEnabledCheckBox.IsChecked == true;
            var hotkeyEnabled = HotkeyActivationEnabledCheckBox.IsChecked == true;

            HotkeyActivationModeComboBox.IsEnabled = hotkeyEnabled;
            HotkeyPanel.IsEnabled = hotkeyEnabled;
            ShakeTrackingIntervalSlider.IsEnabled = shakeEnabled;
            ShakeMinimumSpeedSlider.IsEnabled = shakeEnabled;
            ShakeMinimumTurnsSlider.IsEnabled = shakeEnabled;
        }

        private void CursorSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorSourceMode = GetCursorSourceMode();
            SaveSettings();
            SyncHideBuiltInCursorAvailability(saveWhenChanged: true);

            _needsCursorEditorUpdate = true;
            _needsCursorStatusUpdate = true;
            _needsRemoveCollectionButtonUpdate = true;
            _needsCollectionUiAvailabilityUpdate = true;
            _needsPrewarmRestart = true;

            ScheduleDebouncedUpdate();
        }

        private void CollectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorCollectionName = GetSelectedCollectionName();
            SaveSettings();

            _needsCursorEditorUpdate = true;
            _needsCursorStatusUpdate = true;
            _needsRemoveCollectionButtonUpdate = true;
            _needsPrewarmRestart = true;

            ScheduleDebouncedUpdate();
        }

        private void ImportCollectionButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import cursor folder",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder"
            };
            var initialDirectory = GetExistingDirectory(Properties.Settings.Default.LastImportCollectionFolder);
            if (initialDirectory != null)
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var selectedFolder = Directory.Exists(dialog.FileName)
                ? dialog.FileName
                : Path.GetDirectoryName(dialog.FileName);

            try
            {
                var collectionName = CursorCollectionManager.ImportCollectionFolder(selectedFolder);
                RememberFolder(selectedFolder, folder => Properties.Settings.Default.LastImportCollectionFolder = folder);
                Properties.Settings.Default.CursorCollectionName = collectionName;
                SaveSettings();

                _loading = true;
                try
                {
                    LoadCollectionsToControls();
                    SelectCollection(collectionName);
                }
                finally
                {
                    _loading = false;
                }

                UpdateCollectionUiAvailability();
                UpdateRemoveCollectionButton();
                UpdateCursorEditor(loadPreviews: true);
                UpdateCursorStatus();
                StartPrewarmSelectedCollection();

                MessageBox.Show(
                    this,
                    "Imported cursor folder \"" + collectionName + "\".",
                    "Import cursor folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Import cursor folder", "Import cursor folder", ex);
            }
        }

        private void ImportSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "AngryMouse settings package (*.zip)|*.zip",
                CheckFileExists = true
            };
            var initialDirectory = GetExistingDirectory(Properties.Settings.Default.LastImportSettingsFolder);
            if (initialDirectory != null)
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var result = CursorCollectionManager.ImportSettingsPackage(dialog.FileName, IncludeCollectionsCheckBox.IsChecked == true);
                RememberFolder(GetDialogFolder(dialog.FileName), folder => Properties.Settings.Default.LastImportSettingsFolder = folder);
                SaveSettings();
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
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Import settings", "Import settings package", ex);
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
            var initialDirectory = GetExistingDirectory(Properties.Settings.Default.LastExportSettingsFolder);
            if (initialDirectory != null)
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                CursorCollectionManager.ExportSettingsPackage(dialog.FileName, IncludeCollectionsCheckBox.IsChecked == true);
                RememberFolder(GetDialogFolder(dialog.FileName), folder => Properties.Settings.Default.LastExportSettingsFolder = folder);
                SaveSettings();
                MessageBox.Show(this, "Exported settings package.", "Export settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Export settings", "Export settings package", ex);
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
            try
            {
                CursorCollectionManager.RemoveCollection(collectionName);
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Remove cursor folder", "Remove cursor folder", ex);
                return;
            }

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
            CursorRoleDataGrid.Items.Refresh();
        }

        private void DebugEnabledCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.DebugEnabled = DebugEnabledCheckBox.IsChecked == true;
            SaveSettings();
            DebugLog.Write("Debug logging enabled from settings window.");
        }

        private void OpenDebugLogButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                DebugLog.OpenInNotepad();
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Open debug log", "Open debug log", ex);
            }
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
                Filter = "PNG files (*.png)|*.png",
                CheckFileExists = true,
                InitialDirectory = Directory.Exists(collectionPath) ? collectionPath : null
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var fileName = CursorCollectionManager.CopyFileIntoCollection(collectionName, dialog.FileName);
                var assignments = CursorCollectionManager.LoadAssignments(collectionName);
                assignments[row.RoleKey] = fileName;
                CursorCollectionManager.SaveAssignments(collectionName, assignments);
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Change cursor file", "Change cursor file", ex);
                return;
            }

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
            try
            {
                var assignments = CursorCollectionManager.LoadAssignments(collectionName);
                assignments.Remove(row.RoleKey);
                CursorCollectionManager.SaveAssignments(collectionName, assignments);
            }
            catch (Exception ex) when (IsExpectedUserFileException(ex))
            {
                ShowOperationError(this, "Clear cursor file", "Clear cursor file assignment", ex);
                return;
            }

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

            SyncHideBuiltInCursorAvailability(saveWhenChanged: false);
            if (!IsSystemMode())
            {
                Properties.Settings.Default.HideBuiltInCursor = HideBuiltInCursorCheckBox.IsChecked == true;
            }

            SaveSettings();
        }

        private void ActivationSourceCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            SaveActivationSettings();
        }

        private void HotkeyActivationModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            SaveActivationSettings();
        }

        private void HotkeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            SaveActivationSettings();
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
            var result = MessageBox.Show(
                this,
                "Reset all AngryMouse settings to defaults?",
                "Reset defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            Properties.Settings.Default.Reset();
            CursorCollectionManager.InitializeDefaults();
            Properties.Settings.Default.Save();
            AppTheme.ApplySavedTheme();
            LoadSettingsToControls();
            _loading = false;
            StartPrewarmSelectedCollection();
        }

        private void ResetCursorSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetSettingsToDefaults(CursorSettingNames);
            ReloadControlsAfterReset();
        }

        private void ResetShakeSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetSettingsToDefaults(ShakeSettingNames);
            ReloadControlsAfterReset();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            (Application.Current as App)?.EndCursorRolePreview();
            CancelPrewarm();
        }

        private void CursorRolePreview_OnMouseEnter(object sender, Input.MouseEventArgs e)
        {
            TryBeginCursorRolePreview((sender as FrameworkElement)?.DataContext as CursorRoleRow);
        }

        private void CursorRolePreview_OnMouseLeftButtonDown(object sender, Input.MouseButtonEventArgs e)
        {
            TryBeginCursorRolePreview((sender as FrameworkElement)?.DataContext as CursorRoleRow);
            e.Handled = true;
        }

        private void CursorRolePreview_OnMouseLeave(object sender, Input.MouseEventArgs e)
        {
            (Application.Current as App)?.EndCursorRolePreview();
        }

        private static void TryBeginCursorRolePreview(CursorRoleRow row)
        {
            if (row == null || !row.CanPreview || string.IsNullOrWhiteSpace(row.RoleKey))
            {
                return;
            }

            (Application.Current as App)?.BeginCursorRolePreview(row.RoleKey);
        }

        private void UpdateCursorEditor(bool loadPreviews)
        {
            (Application.Current as App)?.EndCursorRolePreview();

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
                    ZoomPreview = loadPreviews && exists ? CursorVisualCache.GetPreview(collectionName, role.Key, filePath) : null,
                    RoleCursor = GetRoleCursor(role.Key),
                    CanAdjust = exists,
                    CanPreview = exists
                });
            }

            if (Directory.Exists(collectionPath))
            {
                foreach (var file in Directory.GetFiles(collectionPath, "*.png", SearchOption.TopDirectoryOnly)
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
                            ZoomPreview = loadPreviews ? CursorVisualLoader.LoadPngPreview(file) : null,
                            RoleCursor = Input.Cursors.Arrow
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
            DebugLog.Write("Settings prewarm started: " + collectionName);

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

                        if (task.IsCanceled)
                        {
                            DebugLog.Write("Settings prewarm canceled: " + collectionName);
                            CursorRenderStatusTextBlock.Visibility = Visibility.Collapsed;
                            CursorRenderStatusTextBlock.Text = "";
                            return;
                        }

                        if (task.IsFaulted)
                        {
                            DebugLog.WriteException("Settings prewarm failed: " + collectionName, task.Exception?.GetBaseException());
                            var ignored = task.Exception;
                            CursorRenderStatusTextBlock.Visibility = Visibility.Visible;
                            CursorRenderStatusTextBlock.Text = "Cursor render failed.";
                            return;
                        }

                        DebugLog.Write("Settings prewarm completed: " + collectionName);
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

        private bool IsSystemMode()
        {
            return string.Equals(GetCursorSourceMode(), CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase);
        }

        private void SyncHideBuiltInCursorAvailability(bool saveWhenChanged)
        {
            if (!IsSystemMode())
            {
                HideBuiltInCursorCheckBox.IsEnabled = true;
                return;
            }

            HideBuiltInCursorCheckBox.IsEnabled = false;
            HideBuiltInCursorCheckBox.IsChecked = false;

            if (!Properties.Settings.Default.HideBuiltInCursor)
            {
                return;
            }

            Properties.Settings.Default.HideBuiltInCursor = false;
            if (saveWhenChanged)
            {
                SaveSettings();
            }
        }

        private void UpdateCollectionUiAvailability()
        {
            var collectionMode = IsCollectionMode();

            CollectionLabel.IsEnabled = collectionMode;
            CollectionComboBox.IsEnabled = collectionMode;
            ImportCollectionButton.IsEnabled = collectionMode;
            // Always enable import/export settings buttons, but we might want to adjust their tooltip or meaning
            ImportSettingsButton.IsEnabled = true;
            ExportSettingsButton.IsEnabled = true;
            RemoveCollectionComboBox.IsEnabled = collectionMode;
            CursorRenderPanel.Visibility = collectionMode ? Visibility.Visible : Visibility.Collapsed;
            CursorRoleDataGrid.Visibility = collectionMode ? Visibility.Visible : Visibility.Collapsed;
            // Always enable the checkbox, but maybe adjust its text/tooltip based on mode?
            IncludeCollectionsCheckBox.IsEnabled = true;

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

        private static Input.Cursor GetRoleCursor(string roleKey)
        {
            switch (roleKey)
            {
                case "arrow":
                    return Input.Cursors.Arrow;
                case "ibeam":
                    return Input.Cursors.IBeam;
                case "wait":
                    return Input.Cursors.Wait;
                case "appstarting":
                    return Input.Cursors.AppStarting;
                case "crosshair":
                    return Input.Cursors.Cross;
                case "uparrow":
                    return Input.Cursors.UpArrow;
                case "sizens":
                    return Input.Cursors.SizeNS;
                case "sizewe":
                    return Input.Cursors.SizeWE;
                case "sizenwse":
                    return Input.Cursors.SizeNWSE;
                case "sizenesw":
                    return Input.Cursors.SizeNESW;
                case "sizeall":
                    return Input.Cursors.SizeAll;
                case "no":
                    return Input.Cursors.No;
                case "hand":
                    return Input.Cursors.Hand;
                case "help":
                    return Input.Cursors.Help;
                default:
                    return Input.Cursors.Arrow;
            }
        }

        private void ReloadControlsAfterReset()
        {
            LoadSettingsToControls();
            _loading = false;
            StartPrewarmSelectedCollection();
        }

        private static void ResetSettingsToDefaults(IEnumerable<string> settingNames)
        {
            foreach (var settingName in settingNames)
            {
                Properties.Settings.Default[settingName] = GetSettingDefaultValue(settingName);
            }

            SaveSettings();
        }

        private void ConfigureSliderValueToolTips()
        {
            ConfigureSliderValueToolTip(SizeSlider, "0");
            ConfigureSliderValueToolTip(AnimationLengthSlider, "0");
            ConfigureSliderValueToolTip(VisibleDurationSlider, "0");
            ConfigureSliderValueToolTip(ShakeTrackingIntervalSlider, "0");
            ConfigureSliderValueToolTip(ShakeMinimumSpeedSlider, "0.0");
            ConfigureSliderValueToolTip(ShakeMinimumTurnsSlider, "0");
        }

        private void ConfigureSliderValueToolTip(Slider slider, string valueFormat)
        {
            slider.AutoToolTipPlacement = AutoToolTipPlacement.None;

            var textBlock = new TextBlock
            {
                Foreground = Brushes.Black
            };

            var toolTip = new ToolTip
            {
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Content = textBlock,
                HasDropShadow = true,
                Padding = new Thickness(8, 5, 8, 5),
                Placement = PlacementMode.Relative,
                PlacementTarget = slider,
                StaysOpen = true
            };

            _sliderValueToolTips[slider] = toolTip;
            _sliderValueToolTipFormats[slider] = valueFormat;
            slider.ToolTip = toolTip;

            UpdateSliderValueToolTip(slider);
            slider.ValueChanged += SliderValueToolTip_OnValueChanged;
            slider.MouseEnter += SliderValueToolTip_OnMouseEnter;
            slider.MouseMove += SliderValueToolTip_OnMouseMove;
            slider.MouseLeave += SliderValueToolTip_OnMouseLeave;
            slider.PreviewMouseLeftButtonDown += SliderValueToolTip_OnPreviewMouseLeftButtonDown;
            slider.PreviewMouseLeftButtonUp += SliderValueToolTip_OnPreviewMouseLeftButtonUp;
            slider.LostMouseCapture += SliderValueToolTip_OnLostMouseCapture;
        }

        private void SliderValueToolTip_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var slider = (Slider)sender;
            UpdateSliderValueToolTip(slider);
            if (_activeSliderValueToolTips.Contains(slider) || slider.IsMouseOver)
            {
                ShowSliderValueToolTip(slider);
            }
        }

        private void SliderValueToolTip_OnMouseEnter(object sender, Input.MouseEventArgs e)
        {
            ShowSliderValueToolTip((Slider)sender);
        }

        private void SliderValueToolTip_OnMouseMove(object sender, Input.MouseEventArgs e)
        {
            var slider = (Slider)sender;
            if (_activeSliderValueToolTips.Contains(slider) || IsSliderValueToolTipOpen(slider))
            {
                ShowSliderValueToolTip(slider);
            }
        }

        private void SliderValueToolTip_OnMouseLeave(object sender, Input.MouseEventArgs e)
        {
            var slider = (Slider)sender;
            if (!_activeSliderValueToolTips.Contains(slider))
            {
                HideSliderValueToolTip(slider);
            }
        }

        private void SliderValueToolTip_OnPreviewMouseLeftButtonDown(object sender, Input.MouseButtonEventArgs e)
        {
            var slider = (Slider)sender;
            _activeSliderValueToolTips.Add(slider);
            ShowSliderValueToolTip(slider);
        }

        private void SliderValueToolTip_OnPreviewMouseLeftButtonUp(object sender, Input.MouseButtonEventArgs e)
        {
            var slider = (Slider)sender;
            _activeSliderValueToolTips.Remove(slider);
            if (slider.IsMouseOver)
            {
                ShowSliderValueToolTip(slider);
                return;
            }

            HideSliderValueToolTip(slider);
        }

        private void SliderValueToolTip_OnLostMouseCapture(object sender, Input.MouseEventArgs e)
        {
            var slider = (Slider)sender;
            _activeSliderValueToolTips.Remove(slider);
            if (!slider.IsMouseOver)
            {
                HideSliderValueToolTip(slider);
            }
        }

        private void ShowSliderValueToolTip(Slider slider)
        {
            ToolTip toolTip;
            if (!_sliderValueToolTips.TryGetValue(slider, out toolTip))
            {
                return;
            }

            UpdateSliderValueToolTip(slider);
            if (!toolTip.IsOpen)
            {
                toolTip.IsOpen = true;
            }
        }

        private void HideSliderValueToolTip(Slider slider)
        {
            ToolTip toolTip;
            if (_sliderValueToolTips.TryGetValue(slider, out toolTip))
            {
                toolTip.IsOpen = false;
            }
        }

        private bool IsSliderValueToolTipOpen(Slider slider)
        {
            ToolTip toolTip;
            return _sliderValueToolTips.TryGetValue(slider, out toolTip) && toolTip.IsOpen;
        }

        private void UpdateSliderValueToolTip(Slider slider)
        {
            ToolTip toolTip;
            if (!_sliderValueToolTips.TryGetValue(slider, out toolTip))
            {
                return;
            }

            var textBlock = toolTip.Content as TextBlock;
            if (textBlock != null)
            {
                textBlock.Text = FormatSliderValueToolTipValue(slider);
            }

            toolTip.PlacementTarget = slider;
            toolTip.HorizontalOffset = GetSliderValueToolTipHorizontalOffset(slider, toolTip);
            toolTip.VerticalOffset = -36;
        }

        private string FormatSliderValueToolTipValue(Slider slider)
        {
            string valueFormat;
            if (!_sliderValueToolTipFormats.TryGetValue(slider, out valueFormat))
            {
                valueFormat = "0";
            }

            return slider.Value.ToString(valueFormat, CultureInfo.InvariantCulture);
        }

        private static double GetSliderValueToolTipHorizontalOffset(Slider slider, ToolTip toolTip)
        {
            var sliderWidth = slider.ActualWidth;
            if ((double.IsNaN(sliderWidth) || sliderWidth <= 0) && !double.IsNaN(slider.Width))
            {
                sliderWidth = slider.Width;
            }

            var range = slider.Maximum - slider.Minimum;
            if (double.IsNaN(sliderWidth) || sliderWidth <= 0 || range <= 0)
            {
                return 0;
            }

            var ratio = (slider.Value - slider.Minimum) / range;
            ratio = Math.Max(0, Math.Min(1, ratio));

            var toolTipWidth = toolTip.ActualWidth;
            if (double.IsNaN(toolTipWidth) || toolTipWidth <= 0)
            {
                toolTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                toolTipWidth = toolTip.DesiredSize.Width;
            }

            if (double.IsNaN(toolTipWidth) || toolTipWidth <= 0)
            {
                toolTipWidth = 32;
            }

            var maxOffset = Math.Max(0, sliderWidth - toolTipWidth);
            return Math.Max(0, Math.Min(maxOffset, (sliderWidth * ratio) - (toolTipWidth / 2)));
        }

        private static T GetSettingDefaultValue<T>(string settingName)
        {
            return (T)GetSettingDefaultValue(settingName);
        }

        private static object GetSettingDefaultValue(string settingName)
        {
            var property = Properties.Settings.Default.Properties[settingName];
            if (property == null)
            {
                throw new InvalidOperationException("Unknown setting: " + settingName);
            }

            if (property.PropertyType == typeof(string))
            {
                return property.DefaultValue as string ?? string.Empty;
            }

            return Convert.ChangeType(property.DefaultValue, property.PropertyType, CultureInfo.InvariantCulture);
        }

        private static Brush GetThemeBrush(string resourceKey)
        {
            var brush = Application.Current?.Resources[resourceKey] as Brush;
            return brush ?? Brushes.Black;
        }

        private static string GetExistingDirectory(string folder)
        {
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)
                ? folder
                : null;
        }

        private static string GetDialogFolder(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            return Directory.Exists(fileName) ? fileName : Path.GetDirectoryName(fileName);
        }

        private static void RememberFolder(string folder, Action<string> assignFolder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            assignFolder(folder);
        }

        private static bool IsExpectedUserFileException(Exception ex)
        {
            return ex is IOException ||
                   ex is UnauthorizedAccessException ||
                   ex is InvalidDataException ||
                   ex is InvalidOperationException ||
                   ex is System.Xml.XmlException ||
                   ex is ArgumentException ||
                   ex is NotSupportedException ||
                   ex is System.ComponentModel.Win32Exception ||
                   ex is System.Security.SecurityException;
        }

        private static void ShowOperationError(Window owner, string title, string action, Exception ex)
        {
            DebugLog.WriteException(title + " failed", ex);
            MessageBox.Show(
                owner,
                GetFriendlyOperationErrorMessage(action, ex),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static string GetFriendlyOperationErrorMessage(string action, Exception ex)
        {
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return action + " failed. Permission denied. Try running as administrator or choose a different folder.";
            }

            if (ex is DirectoryNotFoundException)
            {
                return action + " failed. Folder not found. It may have been moved or deleted.";
            }

            if (ex is FileNotFoundException)
            {
                return action + " failed. File not found. It may have been moved or deleted.";
            }

            if (ex is PathTooLongException)
            {
                return action + " failed. Path is too long. Choose a shorter folder path.";
            }

            if (ex is InvalidDataException || ex is System.Xml.XmlException)
            {
                return action + " failed. ZIP package is invalid or corrupted. Export it again or choose a different file.";
            }

            if (ex is IOException)
            {
                return action + " failed. File is busy or storage failed. Close other apps using it and try again.";
            }

            if (ex is InvalidOperationException)
            {
                return action + " failed. Selected file or folder is not valid for this operation.";
            }

            if (ex is ArgumentException || ex is NotSupportedException)
            {
                return action + " failed. Path is not valid. Choose a different file or folder.";
            }

            if (ex is System.ComponentModel.Win32Exception)
            {
                return action + " failed. Windows could not open the requested app.";
            }

            return action + " failed.";
        }

        private static void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private void ScheduleDebouncedUpdate()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (_loading) return;

            if (_needsCursorEditorUpdate)
            {
                _needsCursorEditorUpdate = false;
                UpdateCursorEditor(loadPreviews: false);
            }

            if (_needsCursorStatusUpdate)
            {
                _needsCursorStatusUpdate = false;
                UpdateCursorStatus();
            }

            if (_needsRemoveCollectionButtonUpdate)
            {
                _needsRemoveCollectionButtonUpdate = false;
                UpdateRemoveCollectionButton();
            }

            if (_needsCollectionUiAvailabilityUpdate)
            {
                _needsCollectionUiAvailabilityUpdate = false;
                UpdateCollectionUiAvailability();
            }

            if (_needsPrewarmRestart)
            {
                _needsPrewarmRestart = false;
                StartPrewarmSelectedCollection();
            }
        }

        private sealed class CursorRoleRow
        {
            public string RoleKey { get; set; }

            public string Role { get; set; }

            public string FilePath { get; set; }

            public string AssignedFile { get; set; }

            public string Status { get; set; }

            public Brush StatusForeground
            {
                get
                {
                    if (string.Equals(Status, "Valid", StringComparison.OrdinalIgnoreCase))
                    {
                        return GetThemeBrush("Theme.SuccessBrush");
                    }

                    if (string.Equals(Status, "Missing", StringComparison.OrdinalIgnoreCase))
                    {
                        return GetThemeBrush("Theme.AccentRedBrush");
                    }

                    return GetThemeBrush("Theme.SecondaryForegroundBrush");
                }
            }

            public BitmapSource ZoomPreview { get; set; }

            public Input.Cursor RoleCursor { get; set; }

            public bool CanEditRole => !string.IsNullOrWhiteSpace(RoleKey);

            public bool CanAdjust { get; set; }

            public bool CanPreview { get; set; }

            public bool CanClear => CanEditRole && !string.IsNullOrWhiteSpace(AssignedFile);
        }
    }
}
