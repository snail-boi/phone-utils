using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using YourApp;
using static phone_utils.SetupControl;

namespace phone_utils
{
    public partial class SettingsControl : UserControl
    {
        private readonly MainWindow _main;
        private AppConfig _config;
        private readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Phone Utils",
            "config.json"
        );

        private bool _isInitializing = true;

        public SettingsControl(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            Debugger.show("SettingsControl initialized. Loading configuration from: " + configPath);
            _config = ConfigManager.Load(configPath);
            ApplyConfigToUI();
            InitializeUpdateIntervalUI();
            _isInitializing = false;
            LoadThemesIntoComboBox();
        }

        private void InitializeUpdateIntervalUI()
        {
            // Ensure UI element exists in XAML: we'll look for CmbUpdateInterval
            try
            {
                if (this.FindName("CmbUpdateInterval") is ComboBox cmb)
                {
                    cmb.ItemsSource = new[] { "Extreme (1s)", "Fast (5s)", "Medium (15s)", "Slow (30s)", "No automatic update" };
                    int mode = (int)_config.UpdateIntervalMode;
                    if (mode < 1 || mode > 5) mode = 3;
                    cmb.SelectedIndex = mode - 1;
                    cmb.SelectionChanged += CmbUpdateInterval_SelectionChanged;
                }
            }
            catch { }
        }

        private void CmbUpdateInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (!(sender is ComboBox cmb)) return;
            int sel = cmb.SelectedIndex;
            if (sel < 0) return;
            _config.UpdateIntervalMode = (UpdateIntervalMode)(sel + 1);
            SaveConfig(false);
        }

        private void LoadThemesIntoComboBox()
        {
            if (_config?.Themes == null || _config.Themes.Count == 0)
            {
                CmbThemes.ItemsSource = null;
                return;
            }

            CmbThemes.ItemsSource = _config.Themes;
            CmbThemes.DisplayMemberPath = "Name";

            // Select the theme that matches the current ButtonStyle
            var currentTheme = _config.Themes.FirstOrDefault(t =>
                t.Background == _config.ButtonStyle.Background &&
                t.Foreground == _config.ButtonStyle.Foreground &&
                t.Hover == _config.ButtonStyle.Hover);

            if (currentTheme != null)
                CmbThemes.SelectedItem = currentTheme;
            else
                CmbThemes.SelectedIndex = 0; // fallback if no match
        }

        private void ApplyConfigToUI()
        {
            Debugger.show("Applying configuration to UI in SettingsControl.");

            // Apply button colors
            ApplyButtonColors(_config.ButtonStyle);

            // Set background path
            TxtBackground.Text = _config.Paths.Background;

            // Set checkboxes
            ChkDevmode.IsChecked = _config.SpecialOptions.DevMode;
            ChkMusicPresence.IsChecked = _config.SpecialOptions.MusicPresence;
            ChkDebugMode.IsChecked = _config.SpecialOptions.DebugMode;
        }


        private void ApplyButtonColors(ButtonStyleConfig style)
        {
            Debugger.show("Applying button colors.");
            Application.Current.Resources["ButtonBackground"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Background);
            Application.Current.Resources["ButtonForeground"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Foreground);
            Application.Current.Resources["ButtonHover"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Hover);

            BtnPickBackground.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Background));
            BtnPickForeground.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Foreground));
            BtnPickHover.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Hover));
        }

        private void BtnSaveTheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ThemeNameDialog
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                var newTheme = new ThemesConfig
                {
                    Name = dialog.ThemeName,
                    Foreground = _config.ButtonStyle.Foreground,
                    Background = _config.ButtonStyle.Background,
                    Hover = _config.ButtonStyle.Hover
                };

                // ✅ Add the new theme to your theme list
                _config.Themes ??= new List<ThemesConfig>();
                _config.Themes.Add(newTheme);

                MessageBox.Show($"Theme '{newTheme.Name}' saved successfully!", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                SaveConfig(false);
            }
        }

        private void CmbThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbThemes.SelectedItem is ThemesConfig selectedTheme)
            {
                _config.ButtonStyle.Foreground = selectedTheme.Foreground;
                _config.ButtonStyle.Background = selectedTheme.Background;
                _config.ButtonStyle.Hover = selectedTheme.Hover;
                SaveConfig(false);
                ApplyTheme(selectedTheme);
                _main.ReloadConfiguration();
                LoadThemesIntoComboBox();
            }
        }

        private void ApplyTheme(ThemesConfig theme)
        {
            if (theme == null) return;

            // Just copy the values to ButtonStyle
            _config.ButtonStyle.Foreground = theme.Foreground;
            _config.ButtonStyle.Background = theme.Background;
            _config.ButtonStyle.Hover = theme.Hover;
        }


        private void BtnDeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            if (CmbThemes.SelectedItem is ThemesConfig selectedTheme)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the theme '{selectedTheme.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    // Remove the selected theme
                    _config.Themes.Remove(selectedTheme);

                    // Save config
                    SaveConfig(false);

                    // Reload the themes in ComboBox
                    LoadThemesIntoComboBox();

                    // Optionally, reset button style to default if the deleted theme was selected
                    if (_config.ButtonStyle.Background == selectedTheme.Background &&
                        _config.ButtonStyle.Foreground == selectedTheme.Foreground &&
                        _config.ButtonStyle.Hover == selectedTheme.Hover)
                    {
                        // Reset to first theme or defaults
                        if (_config.Themes.Count > 0)
                        {
                            ApplyTheme(_config.Themes[0]);
                        }
                        else
                        {
                            _config.ButtonStyle = new ButtonStyleConfig
                            {
                                Background = "#FFFFFF",
                                Foreground = "#000000",
                                Hover = "#CCCCCC"
                            };
                            ApplyButtonColors(_config.ButtonStyle);
                        }
                    }

                    MessageBox.Show($"Theme '{selectedTheme.Name}' deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadThemesIntoComboBox();
                }
            }
            else
            {
                MessageBox.Show("Please select a theme to delete.", "No Theme Selected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }



        #region Color Pickers
        private void PickColor(SolidColorBrush currentBrush, Action<Color> apply)
        {
            Debugger.show("Opening color picker.");
            var picker = new ColorPickerWindow(currentBrush.Color);
            if (picker.ShowDialog() == true)
            {
                apply(picker.SelectedColor);
                SaveConfig(false);
            }
        }

        private void BtnPickBackground_Click(object sender, RoutedEventArgs e) =>
            PickColor(BtnPickBackground.Background as SolidColorBrush, c =>
            {
                Application.Current.Resources["ButtonBackground"] = new SolidColorBrush(c);
                BtnPickBackground.Background = new SolidColorBrush(c);
                _config.ButtonStyle.Background = c.ToString();
            });

        private void BtnPickForeground_Click(object sender, RoutedEventArgs e) =>
            PickColor(BtnPickForeground.Background as SolidColorBrush, c =>
            {
                Application.Current.Resources["ButtonForeground"] = new SolidColorBrush(c);
                BtnPickForeground.Background = new SolidColorBrush(c);
                _config.ButtonStyle.Foreground = c.ToString();
            });

        private void BtnPickHover_Click(object sender, RoutedEventArgs e) =>
            PickColor(BtnPickHover.Background as SolidColorBrush, c =>
            {
                Application.Current.Resources["ButtonHover"] = new SolidColorBrush(c);
                BtnPickHover.Background = new SolidColorBrush(c);
                _config.ButtonStyle.Hover = c.ToString();
            });
        #endregion

        #region Background Image
        private void BrowseBackground(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Supported Images|*.jpg;*.png|Jpeg file(*.jpg)|*.jpg|Png file(*.png)|*.png|All files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtBackground.Text = dlg.FileName;
                Debugger.show("Background selected: " + TxtBackground.Text);
                _config.Paths.Background = TxtBackground.Text;
                SaveConfig(false);
            }
        }
        #endregion

        #region DevMode
        private void ChkDevmode_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _config.SpecialOptions.DevMode = true;
            var result = MessageBox.Show(
                "Are you sure you want to enable devmode\n this has extra features, but they only work when specific apps are installed",
                "",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            Debugger.show("DevMode checked. User confirmation: " + result);

            if (result == MessageBoxResult.Yes)
            {
                SaveConfig(false);
            }
            else
            {
                ChkDevmode.IsChecked = false;
                _config.SpecialOptions.DevMode = false;
            }
        }
        private void ChkDevmode_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _config.SpecialOptions.DevMode = false;
            Debugger.show("DevMode unchecked.");
            SaveConfig(false);
        }
        #endregion
        #region music presence
        private void ChkMusicPresence_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _config.SpecialOptions.MusicPresence = true;
            Debugger.show("MusicPresence checked.");
            SaveConfig(false);
        }

        private void ChkMusicPresence_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _config.SpecialOptions.MusicPresence = false;
            Debugger.show("MusicPresence unchecked.");
            SaveConfig(false);
        }
        #endregion
        #region debug mode
        private void ChkDebugMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _config.SpecialOptions.DebugMode = true;
            Debugger.show("DebugMode checked.");
            SaveConfig(false);
        }

        private void ChkDebugMode_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _config.SpecialOptions.DebugMode = false;
            Debugger.show("DebugMode unchecked.");
            SaveConfig(false);
        }
        #endregion



        #region Config Save
        private void SaveConfig(bool showmessage)
        {
            try
            {
                string folder = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                ConfigManager.Save(configPath, _config);
                Debugger.show("Configuration saved to: " + configPath);
                _main.ReloadConfiguration();

                if (showmessage)
                    MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debugger.show("SaveConfig exception: " + ex.Message);
            }
        }
        #endregion
    }
}