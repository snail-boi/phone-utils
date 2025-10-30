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
using System.Threading.Tasks;

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

            Debugger.show("SetupControl initialized. Loading configuration from: " + configPath);
            _config = ConfigManager.Load(configPath);
            ApplyConfigToUI();
        }

        #region UI Initialization
        private void ApplyConfigToUI()
        {
            Debugger.show("Applying configuration to UI.");
            string resourcesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "Resources"
            );

            // Default paths
            _config.Paths.Adb = string.IsNullOrEmpty(_config.Paths.Adb) || _config.Paths.Adb.Contains("PhoneUtils")
                ? Path.Combine(resourcesDir, "adb.exe")
                : _config.Paths.Adb;
            Debugger.show("ADB Path set to: " + _config.Paths.Adb);

            _config.Paths.Scrcpy = string.IsNullOrEmpty(_config.Paths.Scrcpy) || _config.Paths.Scrcpy.Contains("PhoneUtils")
                ? Path.Combine(resourcesDir, "scrcpy.exe")
                : _config.Paths.Scrcpy;
            Debugger.show("Scrcpy Path set to: " + _config.Paths.Scrcpy);

            TxtAdbPath.Text = _config.Paths.Adb;
            TxtScrcpyPath.Text = _config.Paths.Scrcpy;

            DeviceSelector.ItemsSource = _config.SavedDevices;
            if (!string.IsNullOrEmpty(_config.SelectedDeviceUSB))
            {
                DeviceSelector.SelectedValue = _config.SelectedDeviceUSB;
                TxtPincode.Password = _config.SavedDevices
                    .FirstOrDefault(d => d.UsbSerial == _config.SelectedDeviceUSB)?.Pincode ?? string.Empty;
                Debugger.show("Selected device set: " + _config.SelectedDeviceUSB);
            }

            ToggleDevMode(_config.SpecialOptions.DevMode);
        }

        private void ToggleDevMode(bool enabled)
        {
            Debugger.show("ToggleDevMode called. Enabled: " + enabled);
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
            var result = dlg.ShowDialog() == true ? dlg.FileName : string.Empty;
            Debugger.show("BrowseFile selected: " + result);
            return result;
        }

        private void BrowseAdb(object sender, RoutedEventArgs e) => TxtAdbPath.Text = BrowseFile("ADB Executable|adb.exe");
        private void BrowseScrcpy(object sender, RoutedEventArgs e) => TxtScrcpyPath.Text = BrowseFile("Scrcpy Executable|scrcpy.exe");
        #endregion

        #region Config Save / Reload
        private async void SaveConfiguration(object sender, RoutedEventArgs e)
        {
            Debugger.show("SaveConfiguration triggered.");
            UpdateConfigFromUI();
            await SaveConfig(true);
        }

        private void UpdateConfigFromUI()
        {
            Debugger.show("Updating config from UI.");
            _config.Paths.Adb = TxtAdbPath.Text;
            _config.Paths.Scrcpy = TxtScrcpyPath.Text;

            if (DeviceSelector.SelectedItem is DeviceConfig selDev)
            {
                _config.SelectedDeviceUSB = selDev.UsbSerial;
                _config.SelectedDeviceName = selDev.Name;
                _config.SelectedDeviceWiFi = selDev.TcpIp;
                _config.SelectedDevicePincode = selDev.Pincode;
                Debugger.show("Device selection updated: " + selDev.UsbSerial);
            }
        }

        private async Task SaveConfig(bool showmessage)
        {
            try
            {
                string folder = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                ConfigManager.Save(configPath, _config);
                Debugger.show("Configuration saved to: " + configPath);

                // Ensure the main window reloads configuration and wait for completion
                await _main.ReloadConfiguration();

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

        #region Device Management
        private async Task<string> GetDeviceIpAsync(string serial)
        {
            Debugger.show("Getting IP for device: " + serial);
            string output = await AdbHelper.RunAdbCaptureAsync($"-s {serial} shell ip addr show wlan0").ConfigureAwait(false);

            var match = Regex.Match(output, @"inet (\d+\.\d+\.\d+\.\d+)/");
            Debugger.show("IP result: " + (match.Success ? match.Groups[1].Value : "null"));
            return match.Success ? match.Groups[1].Value : null;
        }

        private async Task<string> GetFirstUsbSerialAsync()
        {
            string output = await AdbHelper.RunAdbCaptureAsync("devices").ConfigureAwait(false);
            Debugger.show("ADB devices output:\n" + output);

            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                // Expected format: <serial>\tdevice
                if (trimmed.EndsWith("\tdevice"))
                {
                    var parts = trimmed.Split('\t');
                    if (parts.Length > 0)
                    {
                        Debugger.show("First USB device found: " + parts[0]);
                        return parts[0];
                    }
                }
            }

            Debugger.show("No USB device found.");
            return null;
        }

        private async void SaveCurrentDevice(object sender, RoutedEventArgs e)
        {
            try
            {
                string adbPath = TxtAdbPath.Text;
                if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
                {
                    MessageBox.Show("Please select a valid adb.exe path first.", "ADB Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Debugger.show("Invalid ADB path: " + adbPath);
                    return;
                }

                string serial = await GetFirstUsbSerialAsync();
                if (serial == null)
                {
                    MessageBox.Show("No USB device found. Ensure USB debugging is enabled.", "Device Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Try to get IP (Wi-Fi)
                string ip = await GetDeviceIpAsync(serial);
                string tcpIpWithPort;

                if (string.IsNullOrEmpty(ip))
                {
                    // Ask user if they want to save as USB-only
                    var result = MessageBox.Show(
                        "No Wi-Fi IP detected for this device. Do you want to save it as USB only?",
                        "IP Not Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    tcpIpWithPort = "None";
                    Debugger.show($"Device IP: {ip}, TCP: {tcpIpWithPort}");
                }
                else
                {
                    tcpIpWithPort = $"{ip}:5555";
                    Debugger.show($"Device IP: {ip}, TCP: {tcpIpWithPort}");
                }

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
                Debugger.show("New device created: " + newDevice.UsbSerial + ", Name: " + newDevice.Name);

                // Remove existing device with same serial
                var existing = _config.SavedDevices.FirstOrDefault(d => d.UsbSerial == serial);
                if (existing != null)
                {
                    _config.SavedDevices.Remove(existing);
                    Debugger.show("Removed existing device with same serial: " + serial);
                }

                _config.SavedDevices.Add(newDevice);
                UpdateSelectedDevice(newDevice);

                // Refresh ComboBox
                DeviceSelector.SelectionChanged -= DeviceSelector_SelectionChanged;
                DeviceSelector.ItemsSource = null;
                DeviceSelector.ItemsSource = _config.SavedDevices;
                DeviceSelector.SelectedValue = serial;
                DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;

                await SaveConfig(false);

                string msg = tcpIpWithPort == "None"
                    ? "Device saved successfully as USB only."
                    : $"Device saved successfully. Detected IP: {tcpIpWithPort}";

                Debugger.show(msg);
                MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debugger.show("SaveCurrentDevice exception: " + ex.Message);
            }
        }

        private void UpdateSelectedDevice(DeviceConfig device)
        {
            Debugger.show("Updating selected device: " + device.UsbSerial);
            _config.SelectedDeviceUSB = device.UsbSerial;
            _config.SelectedDeviceName = device.Name;
            _config.SelectedDeviceWiFi = device.TcpIp;
            _config.SelectedDevicePincode = device.Pincode;
            TxtPincode.Password = device.Pincode;
        }

        private async void DeleteSelectedDevice(object sender, RoutedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is not DeviceConfig selectedDevice)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the device '{selectedDevice.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result != MessageBoxResult.Yes) return;

            _config.SavedDevices.Remove(selectedDevice);
            Debugger.show("Deleted device: " + selectedDevice.UsbSerial);

            if (_config.SelectedDeviceUSB == selectedDevice.UsbSerial)
            {
                TxtPincode.Password = string.Empty;
                _config.SelectedDeviceUSB = string.Empty;
                _config.SelectedDeviceName = string.Empty;
                _config.SelectedDeviceWiFi = string.Empty;
                _config.SelectedDevicePincode = string.Empty;
                Debugger.show("Cleared current selected device as it was deleted.");
            }

            DeviceSelector.SelectionChanged -= DeviceSelector_SelectionChanged;
            DeviceSelector.ItemsSource = null;
            DeviceSelector.ItemsSource = _config.SavedDevices;
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;

            await SaveConfig(false);
        }

        private async void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is DeviceConfig selectedDevice)
            {
                Debugger.show("DeviceSelector changed to: " + selectedDevice.UsbSerial);
                UpdateSelectedDevice(selectedDevice);
                UpdateConfigFromUI();
                await SaveConfig(false);
            }
        }
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
        public class ThemesConfig
        {
            public string Name { get; set; } = string.Empty;
            public string Foreground { get; set; } = "White";
            public string Background { get; set; } = "#5539cc";
            public string Hover { get; set; } = "#553fff";
        }
        public class ButtonStyleConfig
        {
            public string Foreground { get; set; } = "White";
            public string Background { get; set; } = "#5539cc";
            public string Hover { get; set; } = "#553fff";
        }

        public class SpecialOptionsConfig
        {
            public bool MusicPresence { get; set; } = false;
            public bool DebugMode { get; set; } = false;
            public bool DevMode { get; set; } = false;
        }

        public class AppConfig
        {
            public PathsConfig Paths { get; set; } = new PathsConfig();
            public FileSyncConfig FileSync { get; set; } = new FileSyncConfig();
            public ScrcpyConfig ScrcpySettings { get; set; } = new ScrcpyConfig();
            public YTDLConfig YTDL { get; set; } = new YTDLConfig();
            public List<ThemesConfig> Themes { get; set; } = new List<ThemesConfig>();
            public ButtonStyleConfig ButtonStyle { get; set; } = new ButtonStyleConfig();
            public List<DeviceConfig> SavedDevices { get; set; } = new List<DeviceConfig>();
            public SpecialOptionsConfig SpecialOptions { get; set; } = new SpecialOptionsConfig();

            // Update interval mode for automatic updates in MainWindow
            // 1 = Extreme (1s), 2 = Fast (5s), 3 = Medium (15s), 4 = Slow (30s), 5 = No automatic update
            public int UpdateIntervalMode { get; set; } = 3;
            
            public string SelectedDeviceUSB { get; set; } = string.Empty;
            public string SelectedDeviceName { get; set; } = string.Empty;
            public string SelectedDeviceWiFi { get; set; } = string.Empty;
            public string SelectedDevicePincode { get; set; } = string.Empty;
        }

        public class PathsConfig
        {
            public string Adb { get; set; } = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "Resources",
                "adb.exe");
            public string Scrcpy { get; set; } = string.Empty;
            public string Background { get; set; } = string.Empty;
        }
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
            public bool Top { get; set; } = false;
            public bool EnableHotkeys { get; set; } = true;
            public bool audiobuffer { get; set; } = false;
            public bool videobuffer { get; set; } = false;
            public int AudioBufferSize { get; set; } = 50;
            public int VideoBufferSize { get; set; } = 50;
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
                    AppConfig config;
                    if (!File.Exists(path))
                    {
                        config = new AppConfig();
                    }
                    else
                    {
                        string json = File.ReadAllText(path);
                        config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    }

                    // Ensure default themes exist if none are defined
                    if (config.Themes == null || config.Themes.Count == 0)
                    {
                        config.Themes = new List<SetupControl.ThemesConfig>
                {
                    new SetupControl.ThemesConfig { Name = "Default Blurple", Foreground = "White", Background = "#5539cc", Hover = "#553fff" },
                    new SetupControl.ThemesConfig { Name = "SnailDev Red", Foreground = "#FF000000", Background = "#FFC30000", Hover = "#FF8D0000" },
                    new SetupControl.ThemesConfig { Name = "Grayscale", Foreground = "#FFFFFFFF", Background = "#FF323232", Hover = "#FF282828" },
                    new SetupControl.ThemesConfig { Name = "White", Foreground = "#FF000000", Background = "#FFFFFFFF", Hover = "#FFC8C8C8" },
                    new SetupControl.ThemesConfig { Name = "Rissoe", Foreground = "#FF82FF5E", Background = "#FF0743A0", Hover = "#FF003282" }
                };
                    }

                    return config;
                }
                catch
                {
                    return new AppConfig();
                }
            }

            public static void Save(string path, AppConfig config)
            {
                try
                {
                    File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
                    Debugger.show("ConfigManager.Save completed for: " + path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Debugger.show("ConfigManager.Save exception: " + ex.Message);
                }
            }

        }
        #endregion
    }
}