using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            _isInitializing = false;
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