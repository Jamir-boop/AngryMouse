using AngryMouse.Cursors;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AngryMouse
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow
    {
        private bool _loading = true;

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
            LoadSettingsToControls();
            _loading = false;
        }

        private void LoadSettingsToControls()
        {
            _loading = true;

            SelectCursorSourceMode(Properties.Settings.Default.CursorSourceMode);
            CustomCursorPathTextBox.Text = Properties.Settings.Default.CustomCursorPath;
            CustomCursorHotspotXTextBox.Text = Properties.Settings.Default.CustomCursorHotspotX.ToString();
            CustomCursorHotspotYTextBox.Text = Properties.Settings.Default.CustomCursorHotspotY.ToString();
            SizeSlider.Value = Properties.Settings.Default.CursorSize;
            AnimationLengthSlider.Value = Properties.Settings.Default.CursorAnimationLength;
            ShakeTrackingIntervalSlider.Value = Properties.Settings.Default.ShakeTrackingInterval;
            ShakeMinimumSpeedSlider.Value = Properties.Settings.Default.ShakeMinimumSpeed;
            ShakeMinimumTurnsSlider.Value = Properties.Settings.Default.ShakeMinimumTurns;

            UpdateCustomCursorControls();
            UpdateCursorStatus();
        }

        private void SelectCursorSourceMode(string mode)
        {
            foreach (var item in CursorSourceComboBox.Items)
            {
                var comboBoxItem = item as ComboBoxItem;
                if (comboBoxItem?.Tag as string == mode)
                {
                    CursorSourceComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            CursorSourceComboBox.SelectedIndex = 0;
        }

        private string GetCursorSourceMode()
        {
            var comboBoxItem = CursorSourceComboBox.SelectedItem as ComboBoxItem;
            return comboBoxItem?.Tag as string ?? "System";
        }

        private void CursorSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CursorSourceMode = GetCursorSourceMode();
            SaveSettings();
            UpdateCustomCursorControls();
            UpdateCursorStatus();
        }

        private void CustomCursorPathTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            Properties.Settings.Default.CustomCursorPath = CustomCursorPathTextBox.Text;
            SaveSettings();
            UpdateCustomCursorControls();
            UpdateCursorStatus();
        }

        private void BrowseCustomCursorButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Cursor files (*.png;*.ico;*.cur)|*.png;*.ico;*.cur|PNG (*.png)|*.png|ICO (*.ico)|*.ico|CUR (*.cur)|*.cur",
                CheckFileExists = true
            };

            var currentPath = CustomCursorPathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                var directory = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog(this) == true)
            {
                CustomCursorPathTextBox.Text = dialog.FileName;
                if (GetCursorSourceMode() != "Custom")
                {
                    SelectCursorSourceMode("Custom");
                    Properties.Settings.Default.CursorSourceMode = "Custom";
                    SaveSettings();
                }
            }
        }

        private void CustomCursorHotspotXTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            int value;
            if (TryReadNonNegativeInt(CustomCursorHotspotXTextBox, out value))
            {
                Properties.Settings.Default.CustomCursorHotspotX = value;
                SaveSettings();
                UpdateCursorStatus();
            }
        }

        private void CustomCursorHotspotYTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;

            int value;
            if (TryReadNonNegativeInt(CustomCursorHotspotYTextBox, out value))
            {
                Properties.Settings.Default.CustomCursorHotspotY = value;
                SaveSettings();
                UpdateCursorStatus();
            }
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
            Properties.Settings.Default.Save();
            LoadSettingsToControls();
            _loading = false;
        }

        private static bool TryReadNonNegativeInt(TextBox textBox, out int value)
        {
            if (int.TryParse(textBox.Text, out value) && value >= 0)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private void UpdateCustomCursorControls()
        {
            var isCustom = GetCursorSourceMode() == "Custom";
            var extension = Path.GetExtension(CustomCursorPathTextBox.Text);
            var usesFileHotspot = isCustom && !string.Equals(extension, ".cur", StringComparison.OrdinalIgnoreCase);

            CustomCursorPathTextBox.IsEnabled = isCustom;
            BrowseCustomCursorButton.IsEnabled = isCustom;
            CustomCursorHotspotXTextBox.IsEnabled = usesFileHotspot;
            CustomCursorHotspotYTextBox.IsEnabled = usesFileHotspot;
        }

        private void UpdateCursorStatus()
        {
            CursorVisualInfo info;
            var mode = GetCursorSourceMode();

            if (mode == "System")
            {
                info = CursorVisualLoader.LoadSystemCursor();
                CursorStatusTextBlock.Text = info.HasBitmap
                    ? "Using active Windows cursor."
                    : "System cursor unavailable. Using built-in arrow.";
            }
            else if (mode == "Custom")
            {
                info = CursorVisualLoader.LoadCustomCursor(
                    Properties.Settings.Default.CustomCursorPath,
                    Properties.Settings.Default.CustomCursorHotspotX,
                    Properties.Settings.Default.CustomCursorHotspotY);
                CursorStatusTextBlock.Text = GetCustomCursorStatus(info);
            }
            else
            {
                CursorStatusTextBlock.Text = "Using built-in arrow.";
            }
        }

        private static string GetCustomCursorStatus(CursorVisualInfo info)
        {
            var path = Properties.Settings.Default.CustomCursorPath;

            if (string.IsNullOrWhiteSpace(path))
            {
                return "Choose a PNG, ICO, or CUR file. Using built-in arrow.";
            }

            if (!File.Exists(path))
            {
                return "Custom file missing. Using built-in arrow.";
            }

            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".cur", StringComparison.OrdinalIgnoreCase))
            {
                return "Unsupported file type. Use PNG, ICO, or CUR. Using built-in arrow.";
            }

            if (info.HasBitmap)
            {
                return "Using custom cursor: " + Path.GetFileName(path);
            }

            return "Custom cursor failed to load. Using built-in arrow.";
        }

        private static void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }
    }
}
