﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfMedia = System.Windows.Media;  // Alias for WPF media
using Windows.Media;
using Windows.Media.Playback;
using CoverArt = TagLib;
using static phone_utils.SetupControl;
using Windows.Storage.Streams;
using Windows.Storage;
using System.Windows.Media.Animation;

namespace phone_utils
{
    public partial class MainWindow : Window
    {
        #region Fields
        public static string ADB_PATH;
        private string SCRCPY_PATH;
        private AppConfig config; // class-level
        private string wifiDevice;
        private string currentDevice;
        private DispatcherTimer connectionCheckTimer;
        private HashSet<int> shownBatteryWarnings = new HashSet<int>();
        private bool wasCharging = false;
        public bool devmode;
        public bool MusicPresence;
        public static bool debugmode;

        private MediaController mediaController;
        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();

            // Initialize media controller (handles MediaPlayer/SMTC)
            mediaController = new MediaController(Dispatcher, () => currentDevice, async () => await UpdateCurrentSongAsync());
            mediaController.Initialize();

            LoadConfiguration();
            // Move device detection to the Loaded event so we can await it properly
            this.Loaded += MainWindow_Loaded;
            UpdateBackgroundImage();

            connectionCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            connectionCheckTimer.Tick += ConnectionCheckTimer_Tick;
            connectionCheckTimer.Start();
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Await initial device detection to ensure any exceptions are observed
            try
            {
                await DetectDeviceAsync();
            }
            catch (Exception ex)
            {
                Debugger.show($"Error during initial device detection: {ex.Message}");
            }
        }
        #endregion

        #region Configuration & Setup
        private void LoadConfiguration()
        {
            Debugger.show("Loading configuration...");

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "config.json"
            );

            config = SetupControl.ConfigManager.Load(configPath);

            Debugger.show($"Config loaded from {configPath}");

            ADB_PATH = string.IsNullOrEmpty(config.Paths.Adb)
                ? Path.Combine(exeDir, "adb.exe")
                : config.Paths.Adb;

            Debugger.show($"ADB Path: {ADB_PATH}");

            SCRCPY_PATH = string.IsNullOrEmpty(config.Paths.Scrcpy)
                ? Path.Combine(exeDir, "scrcpy.exe")
                : config.Paths.Scrcpy;

            Debugger.show($"Scrcpy Path: {SCRCPY_PATH}");

            wifiDevice = config.SelectedDeviceWiFi;
            devmode = config.SpecialOptions != null && config.SpecialOptions.DevMode;
            debugmode = config.SpecialOptions != null && config.SpecialOptions.DebugMode;
            MusicPresence = config.SpecialOptions != null && config.SpecialOptions.MusicPresence;

            Debugger.show($"Selected Wi-Fi device: {wifiDevice}");
            Debugger.show($"Dev mode: {devmode}, Debug mode: {debugmode}");

            // Load button colors from config
            try
            {
                Application.Current.Resources["ButtonBackground"] =
                    (SolidColorBrush)new BrushConverter().ConvertFromString(config.ButtonStyle.Background);
                Application.Current.Resources["ButtonForeground"] =
                    (SolidColorBrush)new BrushConverter().ConvertFromString(config.ButtonStyle.Foreground);
                Application.Current.Resources["ButtonHover"] =
                    (SolidColorBrush)new BrushConverter().ConvertFromString(config.ButtonStyle.Hover);

                Debugger.show("Button colors loaded successfully");
            }
            catch
            {
                Debugger.show("Failed to load button colors, using defaults");
                Application.Current.Resources["ButtonBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5539cc"));
                Application.Current.Resources["ButtonForeground"] = new SolidColorBrush(Colors.White);
                Application.Current.Resources["ButtonHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#553fff"));
            }
        }

        public async Task ReloadConfiguration()
        {
            LoadConfiguration();
            EnableButtons(true);
            StatusText.Foreground = new SolidColorBrush(Colors.Red);
            await DetectDeviceAsync();
        }

        public string GetPincode() => config.SelectedDevicePincode;
        #endregion

        #region Background
        private void UpdateBackgroundImage()
        {
            string imagePath = string.IsNullOrEmpty(config.Paths.Background)
                ? ""
                : config.Paths.Background;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                if (imagePath.StartsWith("http://") || imagePath.StartsWith("https://"))
                {
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                }
                else if (File.Exists(imagePath))
                {
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                }
                else
                {
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    return;
                }

                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                BackgroundImage.Source = bitmap;
                BackgroundImage.Visibility = Visibility.Visible;
            }
            catch
            {
                BackgroundImage.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        #region Device Detection & Status
        private async void ConnectionCheckTimer_Tick(object sender, EventArgs e) => await DetectDeviceAsync();

        private async Task DetectDeviceAsync()
        {
            Debugger.show("Starting device detection...");

            if (!File.Exists(ADB_PATH))
            {
                SetStatus("Please add device under device settings.", Colors.Red);
                Debugger.show("ADB executable not found");
                return;
            }

            if (config.SelectedDeviceWiFi != "None")
            {
                Debugger.show($"Connecting to Wi-Fi device: {wifiDevice}");
                await AdbHelper.RunAdbAsync($"connect {wifiDevice}");
            }

            var devices = await AdbHelper.RunAdbCaptureAsync("devices");
            Debugger.show($"ADB devices output:\n{devices}");

            var deviceList = devices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (await CheckUsbDeviceAsync(deviceList)) return;
            if (await CheckWifiDeviceAsync(deviceList) && config.SelectedDeviceWiFi != "None") return;

            SetStatus("No selected device found!", Colors.Red);
            EnableButtons(false);
            Debugger.show("No device detected");
        }

        private async Task<bool> CheckUsbDeviceAsync(string[] deviceList)
        {
            if (string.IsNullOrEmpty(config.SelectedDeviceUSB)) return false;

            bool usbConnected = deviceList.Any(l => l.StartsWith(config.SelectedDeviceUSB) && l.EndsWith("device"));
            if (!usbConnected) return false;

            SetStatus($"USB device connected: {config.SelectedDeviceName}", Colors.Green);
            currentDevice = config.SelectedDeviceUSB;

            Debugger.show($"USB device {currentDevice} connected");

            if (config.SelectedDeviceWiFi != "None")
            {
                Debugger.show("Setting up Wi-Fi over USB...");
                await SetupWifiOverUsbAsync(deviceList);
            }

            EnableButtons(true);
            await UpdateBatteryStatusAsync();
            await UpdateForegroundAppAsync();

            if (ContentHost.Content == null) ShowNotificationsAsDefault();

            return true;
        }

        private async Task SetupWifiOverUsbAsync(string[] deviceList)
        {
            if (string.IsNullOrEmpty(config.SelectedDeviceWiFi)) return;

            bool wifiAlreadyConnected = deviceList.Any(l => l.StartsWith(config.SelectedDeviceWiFi));
            if (wifiAlreadyConnected)
            {
                Debugger.show("Wi-Fi device already connected via USB setup");
                return;
            }

            Debugger.show("Enabling TCP/IP mode on USB device");
            await AdbHelper.RunAdbAsync($"-s {config.SelectedDeviceUSB} tcpip 5555");
            var connectResult = await AdbHelper.RunAdbCaptureAsync($"connect {config.SelectedDeviceWiFi}");
            Debugger.show($"Wi-Fi connection result: {connectResult}");

            StatusText.Text += connectResult.Contains("connected")
                ? " | Wi-Fi port has been set up."
                : " | Failed to connect Wi-Fi device.";
        }

        private async Task<bool> CheckWifiDeviceAsync(string[] deviceList)
        {
            if (string.IsNullOrEmpty(config.SelectedDeviceWiFi)) return false;

            bool wifiConnected = deviceList.Any(l => l.StartsWith(config.SelectedDeviceWiFi));
            if (!wifiConnected) return false;

            SetStatus($"Wi-Fi device connected: {config.SelectedDeviceName}", Colors.CornflowerBlue);
            currentDevice = config.SelectedDeviceWiFi;
            EnableButtons(true);
            await UpdateBatteryStatusAsync();
            await UpdateForegroundAppAsync();
            if (ContentHost.Content == null) ShowNotificationsAsDefault();
            return true;
        }



        private void SetStatus(string message, Color color)
        {
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(color);
        }


        private async Task UpdateBatteryStatusAsync()
        {
            if (string.IsNullOrEmpty(currentDevice))
            {
                SetBatteryStatus("N/A", Colors.Gray);
                Debugger.show("No current device to check battery");
                return;
            }

            try
            {
                Debugger.show("Fetching battery info...");
                var output = await AdbHelper.RunAdbCaptureAsync($"-s {currentDevice} shell dumpsys battery");
                var batteryInfo = ParseBatteryInfo(output);

                Debugger.show($"Battery parsed: Level={batteryInfo.Level}, Charging={batteryInfo.IsCharging}, Wattage={batteryInfo.Wattage}");

                if (batteryInfo.Level < 0)
                {
                    SetBatteryStatus("N/A", Colors.Gray);
                    return;
                }

                string displayText = batteryInfo.IsCharging && batteryInfo.Wattage > 0
                    ? $"Battery: {batteryInfo.Level}% ({batteryInfo.ChargingStatus}) - {batteryInfo.Wattage:F1} W"
                    : $"Battery: {batteryInfo.Level}% ({batteryInfo.ChargingStatus})";

                SetBatteryStatus(displayText, GetBatteryColor(batteryInfo.Level));
                CheckBatteryWarnings(batteryInfo.Level, batteryInfo.IsCharging);
            }
            catch (Exception ex)
            {
                SetBatteryStatus("Error", Colors.Gray);
                Debugger.show($"Failed to update battery status: {ex.Message}");
            }
        }

        private (int Level, string ChargingStatus, bool IsCharging, double Wattage) ParseBatteryInfo(string output)
        {
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int level = ParseIntFromLine(lines, "level");
            int status = ParseIntFromLine(lines, "status");
            long currentMicroA = ParseLongFromLine(lines, "Max charging current");
            long voltageMicroV = ParseLongFromLine(lines, "Max charging voltage");

            string chargingStatus = status switch
            {
                2 => "Charging",
                3 => "Discharging",
                4 => "Not charging",
                5 => "Full",
                _ => "Unknown"
            };

            bool isCharging = status == 2;
            double amps = currentMicroA / 1_000_000.0;
            double volts = voltageMicroV / 1_000_000.0;
            double wattage = amps * volts;

            return (level, chargingStatus, isCharging, wattage);
        }

        private int ParseIntFromLine(string[] lines, string prefix)
        {
            var line = lines.FirstOrDefault(l => l.Trim().StartsWith(prefix));
            return line != null && int.TryParse(new string(line.Where(char.IsDigit).ToArray()), out int value) ? value : -1;
        }

        private long ParseLongFromLine(string[] lines, string prefix)
        {
            var line = lines.FirstOrDefault(l => l.Trim().StartsWith(prefix));
            return line != null && long.TryParse(new string(line.Where(char.IsDigit).ToArray()), out long value) ? value : 0;
        }

        private void SetBatteryStatus(string text, Color color)
        {
            BatteryText.Text = $"{text}";
            BatteryText.Foreground = new SolidColorBrush(color);
        }

        private Color GetBatteryColor(int level)
        {
            return level switch
            {
                >= 90 => Colors.Green,
                >= 40 => Colors.CornflowerBlue,
                >= 20 => Colors.Orange,
                > 0 => Colors.Red,
                _ => Colors.Gray
            };
        }

        private void CheckBatteryWarnings(int level, bool isCharging)
        {
            // Reset warnings when charging starts
            if (isCharging && !wasCharging)
            {
                shownBatteryWarnings.Clear();
            }

            // Only trigger at specific thresholds
            if ((level == 20 || level == 10 || level == 5 || level == 1) && !shownBatteryWarnings.Contains(level))
            {
                if (level == 1)
                {
                    MessageBox.Show(
                        "Battery critically low at 1%! Connect your charger immediately.",
                        "Critical Battery",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                else
                {
                    MessageBox.Show(
                        $"Battery is at {level}%. Please charge your device.",
                        "Low Battery",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }

                shownBatteryWarnings.Add(level);
            }

            // Update last charging state
            wasCharging = isCharging;
        }




        private async Task UpdateForegroundAppAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(currentDevice))
                {
                    DeviceStatusText.Text = "No device selected.";
                    mediaController?.Clear();
                    return;
                }

                if (config.SpecialOptions != null && config.SpecialOptions.MusicPresence)
                {
                    // DevMode: detect currently playing song in Musicolet
                    await UpdateCurrentSongAsync();
                }
                else
                {
                    // Normal mode: detect active foreground app
                    await DisplayAppActivity();
                }
            }
            catch (Exception ex)
            {
                DeviceStatusText.Text = $"Error retrieving info: {ex.Message}";
                mediaController?.Clear();
            }
        }

        private async Task UpdateCurrentSongAsync()
        {
            Debugger.show("Updating current song from Musicolet...");

            string output = await AdbHelper.RunAdbCaptureAsync($"-s {currentDevice} shell dumpsys media_session");

            bool foundActiveSong = false;

            if (!string.IsNullOrWhiteSpace(output))
            {
                var sessionBlocks = output.Split(new[] { "queueTitle=" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in sessionBlocks)
                {
                    if (!block.Contains("package=in.krosbits.musicolet") || !block.Contains("active=true"))
                        continue;

                    // Extract metadata: title, artist, album
                    var metaMatch = Regex.Match(block,
                        @"metadata:\s+size=\d+,\s+description=(.+?),\s+(.+?),\s+(.+)",
                        RegexOptions.Singleline);

                    if (!metaMatch.Success)
                        continue;

                    string title = metaMatch.Groups[1].Value.Trim();
                    string artist = metaMatch.Groups[2].Value.Trim();
                    string album = metaMatch.Groups[3].Value.Trim();

                    // Extract playback state and position
                    var stateMatch = Regex.Match(block, @"state=PlaybackState\s*\{[^}]*state=(\w+)\((\d+)\),\s*position=(\d+)", RegexOptions.Singleline);

                    bool isPlaying = false;
                    long position = 0;

                    if (stateMatch.Success)
                    {
                        string stateText = stateMatch.Groups[1].Value.Trim().ToUpper(); // PLAYING, PAUSED, etc.
                        isPlaying = stateText == "PLAYING";
                        long.TryParse(stateMatch.Groups[3].Value.Trim(), out position);
                    }


                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist))
                    {
                        DeviceStatusText.Text = $"Song: {title} by {artist}";
                        Debugger.show($"Now playing: {title} by {artist} ({album}) at {position} ms — Playing: {isPlaying}");

                        await mediaController.UpdateMediaControlsAsync(title, artist, album, isPlaying);
                        foundActiveSong = true;
                        break;
                    }
                }
            }
        }



        private async Task DisplayAppActivity()
        {
            var sleepState = await AdbHelper.RunAdbCaptureAsync($"-s {currentDevice} shell dumpsys power");
            var match = Regex.Match(sleepState, @"mWakefulness\s*=\s*(\w+)", RegexOptions.IgnoreCase);

            bool isAwake = match.Success && match.Groups[1].Value.Equals("Awake", StringComparison.OrdinalIgnoreCase);

            if (isAwake)
            {
                // Capture full dumpsys window output
                var input = await AdbHelper.RunAdbCaptureAsync($"-s {currentDevice} shell dumpsys window");

                // Find the line containing "mCurrentFocus"
                var currentFocusLine = input
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.Contains("mCurrentFocus"));

                if (currentFocusLine != null)
                {
                    // Apply your regex exactly as before
                    var match2 = Regex.Match(currentFocusLine, @"\su0\s([^\s/]+)");
                    if (match2.Success)
                    {
                        string packageName = match2.Groups[1].Value;
                        DeviceStatusText.Text = $"Current App: {packageName}";
                    }
                    else
                    {
                        DeviceStatusText.Text = $"Current app not found";
                    }
                }
                else
                {
                    DeviceStatusText.Text = $"Current app not found";
                }
            }
            else
            {
                DeviceStatusText.Text = $"Currently asleep";
            }
        }



        #endregion

        #region Button Handlers
        private void EnableButtons(bool enable)
        {
            bool adbAvailable = File.Exists(ADB_PATH);
            bool scrcpyAvailable = File.Exists(SCRCPY_PATH);

            BtnScrcpyOptions.IsEnabled = enable && scrcpyAvailable && adbAvailable;

            BtnSyncMusic.IsEnabled = enable && adbAvailable;
            Intent.IsEnabled = enable && adbAvailable;
            if(devmode == true)
            {
                Intent.Content = "Intent sender";
            }
            else
            {
                Intent.Content = "App Manager";
            }
        }
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // Trigger the spin animation
            var animation = (Storyboard)FindResource("SpinAnimation");
            animation.Begin();

            if (ContentHost.Content is SettingsControl)
            {
                ContentHost.Content = null;
                ShowNotificationsAsDefault();
            }
            else
            {
                ContentHost.Content = new SettingsControl(this);
            }
        }
        private void BtnScrcpyOptions_Click(object sender, RoutedEventArgs e)
        {
            if (ContentHost.Content is ScrcpyControl)
            {
                ContentHost.Content = null;
                ShowNotificationsAsDefault();
            }
            else
            {
                ContentHost.Content = new ScrcpyControl(this, currentDevice, SCRCPY_PATH, ADB_PATH, config);
            }
        }

        private void Intent_click(object sender, RoutedEventArgs e)
        {
            if(devmode == true)
            {
                if (ContentHost.Content is Intent_Sender)
                {
                    ContentHost.Content = null;
                    ShowNotificationsAsDefault();
                }
                else
                {
                    ContentHost.Content = new Intent_Sender(this, currentDevice);
                }
            }
            else
            {
                if (ContentHost.Content is AppManagerControl)
                {
                    ContentHost.Content = null;
                    ShowNotificationsAsDefault();
                }
                else
                {
                    ContentHost.Content = new AppManagerControl(currentDevice);
                }
            }
        }

        private void BtnSyncMusic_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentDevice)) return;

            if (ContentHost.Content is FileSync)
            {
                ContentHost.Content = null;
                ShowNotificationsAsDefault();
            }
            else
            {
                ContentHost.Content = new FileSync { CurrentDevice = currentDevice };
            }
        }

        private void BtnSetup_Click(object sender, RoutedEventArgs e)
        {
            if (ContentHost.Content is SetupControl)
            {
                ContentHost.Content = null;
                ShowNotificationsAsDefault();
            }
            else
            {
                ContentHost.Content = new SetupControl(this);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await DetectDeviceAsync();
        #endregion

        #region Utility Methods
        private void ShowNotificationsAsDefault() => ContentHost.Content = new NotificationControl(this, currentDevice);

        private void CloseAllAdbProcesses()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM adb.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Process.Start(psi).WaitForExit();
            }
            catch (Exception ex)
            {
                if(config.SpecialOptions != null && config.SpecialOptions.DebugMode)
                    MessageBox.Show($"Failed to close ADB processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Cleanup
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            try
            {
                CloseAllAdbProcesses();

                mediaController?.Clear();
            }
            catch { }
        }
        #endregion
    }
}
