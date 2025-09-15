using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace phone_utils
{
    public partial class SetupControl : UserControl
    {
        private readonly MainWindow _main;
        private AppConfig _config;
        private readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Phone Utils",
            "config.json"
        );

        public SetupControl(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            _config = ConfigManager.Load(configPath);
            ApplyConfigToUI();
        }

        #region UI Initialization
        private void ApplyConfigToUI()
        {
            string resourcesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "Resources"
            );

            // Default paths
            _config.Paths.Adb = string.IsNullOrEmpty(_config.Paths.Adb) || _config.Paths.Adb.Contains("PhoneUtils")
                ? Path.Combine(resourcesDir, "adb.exe")
                : _config.Paths.Adb;

            _config.Paths.Scrcpy = string.IsNullOrEmpty(_config.Paths.Scrcpy) || _config.Paths.Scrcpy.Contains("PhoneUtils")
                ? Path.Combine(resourcesDir, "scrcpy.exe")
                : _config.Paths.Scrcpy;

            TxtAdbPath.Text = _config.Paths.Adb;
            TxtScrcpyPath.Text = _config.Paths.Scrcpy;

            ApplyButtonColors(_config.ButtonStyle);

            DeviceSelector.ItemsSource = _config.SavedDevices;
            if (!string.IsNullOrEmpty(_config.SelectedDeviceUSB))
            {
                DeviceSelector.SelectedValue = _config.SelectedDeviceUSB;
                TxtPincode.Password = _config.SavedDevices
                    .FirstOrDefault(d => d.UsbSerial == _config.SelectedDeviceUSB)?.Pincode ?? string.Empty;
            }

            ToggleDevMode(_config.SpecialOptions.DevMode);
        }

        private void ApplyButtonColors(ButtonStyleConfig style)
        {
            Application.Current.Resources["ButtonBackground"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Background);
            Application.Current.Resources["ButtonForeground"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Foreground);
            Application.Current.Resources["ButtonHover"] = (SolidColorBrush)new BrushConverter().ConvertFromString(style.Hover);

            BtnPickBackground.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Background));
            BtnPickForeground.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Foreground));
            BtnPickHover.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Hover));
        }

        private void ToggleDevMode(bool enabled)
        {
            Visibility devVisibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            AdbPathPanel.Visibility = devVisibility;
            ScrcpyPathPanel.Visibility = devVisibility;
            SaveButton.Visibility = devVisibility;
        }
        #endregion

        #region File Browsing
        private string BrowseFile(string filter)
        {
            var dlg = new OpenFileDialog { Filter = filter };
            return dlg.ShowDialog() == true ? dlg.FileName : string.Empty;
        }

        private void BrowseAdb(object sender, RoutedEventArgs e) => TxtAdbPath.Text = BrowseFile("ADB Executable|adb.exe");
        private void BrowseScrcpy(object sender, RoutedEventArgs e) => TxtScrcpyPath.Text = BrowseFile("Scrcpy Executable|scrcpy.exe");
        #endregion

        #region Config Save / Reload
        private void SaveConfiguration(object sender, RoutedEventArgs e)
        {
            UpdateConfigFromUI();
            SaveConfig(true);
        }

        private void UpdateConfigFromUI()
        {
            _config.Paths.Adb = TxtAdbPath.Text;
            _config.Paths.Scrcpy = TxtScrcpyPath.Text;

            if (DeviceSelector.SelectedItem is DeviceConfig selDev)
            {
                _config.SelectedDeviceUSB = selDev.UsbSerial;
                _config.SelectedDeviceName = selDev.Name;
                _config.SelectedDeviceWiFi = selDev.TcpIp;
                _config.SelectedDevicePincode = selDev.Pincode;
            }
        }

        private void SaveConfig(bool showmessage)
        {
            try
            {
                string folder = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                ConfigManager.Save(configPath, _config);
                _main.ReloadConfiguration();
                if (showmessage == true)
                    MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Device Management
        private string GetDeviceIp(string adbPath, string serial)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = $"-s {serial} shell ip -f inet addr show wlan0",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var match = Regex.Match(output, @"inet (\d+\.\d+\.\d+\.\d+)/");
                if (match.Success) return match.Groups[1].Value;
            }
            catch { }

            return null;
        }

        private string GetFirstUsbSerial(string adbPath)
        {
            try
            {
                var devicesInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = devicesInfo };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    if (line.EndsWith("\tdevice")) return line.Split('\t')[0];
            }
            catch { }

            return null;
        }

        private void SaveCurrentDevice(object sender, RoutedEventArgs e)
        {
            string adbPath = TxtAdbPath.Text;
            if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
            {
                MessageBox.Show("Please select a valid adb.exe path first.", "ADB Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string serial = GetFirstUsbSerial(adbPath);
            if (serial == null)
            {
                MessageBox.Show("No USB device found. Ensure USB debugging is enabled.", "Device Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string ip = GetDeviceIp(adbPath, serial);
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("Could not detect IP address. Ensure device is connected to Wi-Fi.", "IP Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tcpIpWithPort = $"{ip}:5555";

            string name = TxtDeviceName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a device name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newDevice = new DeviceConfig
            {
                Name = name,
                UsbSerial = serial,
                TcpIp = tcpIpWithPort,
                LastConnected = DateTime.Now,
                Pincode = TxtPincode.Password
            };

            // Remove existing device with same serial to avoid duplicates
            var existing = _config.SavedDevices.FirstOrDefault(d => d.UsbSerial == serial);
            if (existing != null)
                _config.SavedDevices.Remove(existing);

            _config.SavedDevices.Add(newDevice);
            UpdateSelectedDevice(newDevice);

            // Update ComboBox safely
            DeviceSelector.SelectionChanged -= DeviceSelector_SelectionChanged;
            DeviceSelector.ItemsSource = null;
            DeviceSelector.ItemsSource = _config.SavedDevices;
            DeviceSelector.SelectedValue = serial;
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;

            SaveConfig(false);

            MessageBox.Show($"Device saved successfully. Detected IP: {tcpIpWithPort}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private void UpdateSelectedDevice(DeviceConfig device)
        {
            _config.SelectedDeviceUSB = device.UsbSerial;
            _config.SelectedDeviceName = device.Name;
            _config.SelectedDeviceWiFi = device.TcpIp;
            _config.SelectedDevicePincode = device.Pincode;
            TxtPincode.Password = device.Pincode;
        }

        private void DeleteSelectedDevice(object sender, RoutedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is not DeviceConfig selectedDevice)
                return; // exit silently if nothing is selected

            var result = MessageBox.Show(
                $"Are you sure you want to delete the device '{selectedDevice.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result != MessageBoxResult.Yes) return;

            _config.SavedDevices.Remove(selectedDevice);

            if (_config.SelectedDeviceUSB == selectedDevice.UsbSerial)
            {
                TxtPincode.Password = string.Empty;
                _config.SelectedDeviceUSB = string.Empty;
                _config.SelectedDeviceName = string.Empty;
                _config.SelectedDeviceWiFi = string.Empty;
                _config.SelectedDevicePincode = string.Empty;
            }

            DeviceSelector.SelectionChanged -= DeviceSelector_SelectionChanged;
            DeviceSelector.ItemsSource = null;
            DeviceSelector.ItemsSource = _config.SavedDevices;
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;

            SaveConfig(false);
        }


        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is DeviceConfig selectedDevice)
            {
                UpdateSelectedDevice(selectedDevice);
                UpdateConfigFromUI();
                SaveConfig(false);
            }
        }
        #endregion

        #region Color Pickers
        private void PickColor(SolidColorBrush currentBrush, Action<Color> apply)
        {
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

        #region Config Classes
        public class DeviceConfig
        {
            public string Name { get; set; } = string.Empty;
            public string UsbSerial { get; set; } = string.Empty;
            public string TcpIp { get; set; } = string.Empty;
            public string Pincode { get; set; } = string.Empty;
            public DateTime LastConnected { get; set; } = DateTime.Now;

            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? base.ToString() : Name;
        }

        public class ButtonStyleConfig
        {
            public string Foreground { get; set; } = "White";
            public string Background { get; set; } = "#5539cc";
            public string Hover { get; set; } = "#553fff";
        }

        public class SpecialOptionsConfig
        {
            public bool ShowDebugMessages { get; set; } = false;
            public bool DevMode { get; set; } = false;
        }

        public class AppConfig
        {
            public PathsConfig Paths { get; set; } = new PathsConfig();
            public FileSyncConfig FileSync { get; set; } = new FileSyncConfig();
            public ScrcpyConfig ScrcpySettings { get; set; } = new ScrcpyConfig();
            public YTDLConfig YTDL { get; set; } = new YTDLConfig();
            public ButtonStyleConfig ButtonStyle { get; set; } = new ButtonStyleConfig();
            public List<DeviceConfig> SavedDevices { get; set; } = new List<DeviceConfig>();
            public SpecialOptionsConfig SpecialOptions { get; set; } = new SpecialOptionsConfig();

            public string SelectedDeviceUSB { get; set; } = string.Empty;
            public string SelectedDeviceName { get; set; } = string.Empty;
            public string SelectedDeviceWiFi { get; set; } = string.Empty;
            public string SelectedDevicePincode { get; set; } = string.Empty;
        }

        public class PathsConfig { public string Adb { get; set; } = string.Empty; public string Scrcpy { get; set; } = string.Empty; }
        public class FileSyncConfig { public string LocalDir { get; set; } = ""; public string RemoteDir { get; set; } = ""; public bool recursion { get; set; } = true; }
        public class ScrcpyConfig
        {
            public bool AudioOnly { get; set; } = false;
            public bool NoAudio { get; set; } = true;
            public bool PlaybackAudio { get; set; } = false;
            public bool LimitMaxSize { get; set; } = true;
            public int MaxSize { get; set; } = 2440;
            public bool StayAwake { get; set; } = true;
            public bool TurnScreenOff { get; set; } = true;
            public bool LockPhone { get; set; } = true;
            public bool EnableHotkeys { get; set; } = true;
            public bool audiobuffer { get; set; } = false;
            public int AudioBufferSize { get; set; } = 100;
            public int CameraType { get; set; } = 0;
            public string VirtualDisplayApp { get; set; } = "";
        }

        public class YTDLConfig { public int DownloadType { get; set; } = 1; public bool BackgroundCheck { get; set; } = false; }

        public static class ConfigManager
        {
            public static AppConfig Load(string path)
            {
                try
                {
                    if (!File.Exists(path)) return new AppConfig();
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                catch { return new AppConfig(); }
            }

            public static void Save(string path, AppConfig config)
            {
                try { File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented)); }
                catch (Exception ex) { MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        #endregion
    }
}
