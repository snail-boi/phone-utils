using System;
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
        public static bool debugmode;

        private Windows.Media.Playback.MediaPlayer mediaPlayer;
        private Windows.Media.SystemMediaTransportControls smtcControls;
        private Windows.Media.SystemMediaTransportControlsDisplayUpdater smtcDisplayUpdater;
        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            InitializeMediaPlayer();

            LoadConfiguration();
            DetectDeviceAsync();
            UpdateBackgroundImage();

            connectionCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            connectionCheckTimer.Tick += ConnectionCheckTimer_Tick;
            connectionCheckTimer.Start();
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
            debugmode = config.SpecialOptions != null && config.SpecialOptions.ShowDebugMessages;

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
                    ClearMediaControls();
                    return;
                }

                if (config.SpecialOptions != null && config.SpecialOptions.DevMode)
                {
                    // DevMode: detect currently playing song in Musicolet
                    await UpdateCurrentSongAsync();
                }
                else
                {
                    // Normal mode: detect active foreground app
                    DisplayAppActivity();
                }
            }
            catch (Exception ex)
            {
                DeviceStatusText.Text = $"Error retrieving info: {ex.Message}";
                ClearMediaControls();
            }
        }

        private async Task UpdateCurrentSongAsync()
        {
            Debugger.show("Updating current song from Musicolet...");

            string output = await AdbHelper.RunAdbCaptureAsync($"-s {currentDevice} shell dumpsys media_session");
            Debugger.show($"Media session output:\n{output}");

            bool foundActiveSong = false;

            if (!string.IsNullOrWhiteSpace(output))
            {
                var sessionBlocks = output.Split(new[] { "queueTitle=" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in sessionBlocks)
                {
                    if (!block.Contains("package=in.krosbits.musicolet") || !block.Contains("active=true"))
                        continue;

                    var match = Regex.Match(block,
                        @"metadata:\s+size=\d+,\s+description=(.+),\s+(.+),\s+(.+)$",
                        RegexOptions.Singleline);

                    if (!match.Success)
                        continue;

                    string title = match.Groups[1].Value.Trim();
                    string artist = match.Groups[2].Value.Trim();
                    string album = match.Groups[3].Value.Trim();

                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist))
                    {
                        DeviceStatusText.Text = $"Song: {title} by {artist}";
                        Debugger.show($"Now playing: {title} by {artist} ({album})");

                        UpdateMediaControls(title, artist, album);
                        foundActiveSong = true;
                        break; // exit after first active song
                    }
                }
            }

            if (!foundActiveSong)
            {
                DeviceStatusText.Text = "No song currently playing in Musicolet.";
                Debugger.show("No active song found");
                ClearMediaControls(); // ensures SMTC is cleared
            }
        }


        private async void DisplayAppActivity()
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

        #region Media Controls
        private async void InitializeMediaPlayer()
        {
            try
            {
                mediaPlayer = new Windows.Media.Playback.MediaPlayer();

                smtcControls = mediaPlayer.SystemMediaTransportControls;
                smtcDisplayUpdater = smtcControls.DisplayUpdater;

                smtcControls.IsEnabled = true;
                smtcControls.IsPlayEnabled = true;
                smtcControls.IsPauseEnabled = true;
                smtcControls.IsNextEnabled = true;
                smtcControls.IsPreviousEnabled = true;

                smtcControls.ButtonPressed += smtcControls_ButtonPressed;

                smtcDisplayUpdater.Type = MediaPlaybackType.Music;


            }
            catch (Exception ex)
            {
                Debugger.show($"MediaPlayer initialization failed: {ex.Message}");
            }
        }

        private async void smtcControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            Debugger.show($"SMTC Button Pressed: {args.Button}");
            // SMTC events come in on a background thread
            await Dispatcher.InvokeAsync(() =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        PlayTrack();
                        break;

                    case SystemMediaTransportControlsButton.Pause:
                        PauzeTrack();
                        break;

                    case SystemMediaTransportControlsButton.Next:
                        PlayNextTrack();
                        break;

                    case SystemMediaTransportControlsButton.Previous:
                        PlayPreviousTrack();
                        break;
                }
            });
        }

        #region Media Control Actions
        private async void PlayTrack()
        {
            await AdbHelper.RunAdbAsync($"-s {currentDevice} shell input keyevent 85");
            smtcControls.PlaybackStatus = MediaPlaybackStatus.Playing;
            Debugger.show("Play requested");
        }
        private async void PauzeTrack()
        {
            await AdbHelper.RunAdbAsync($"-s {currentDevice} shell input keyevent 85");
            smtcControls.PlaybackStatus = MediaPlaybackStatus.Paused;
            Debugger.show("Pause requested.");
        }
        private async void PlayNextTrack()
        {
            await AdbHelper.RunAdbAsync($"-s {currentDevice} shell input keyevent 87");
            Thread.Sleep(500);
            await UpdateCurrentSongAsync();
            Debugger.show("Next track requested.");
        }
        private async void PlayPreviousTrack()
        {
            await AdbHelper.RunAdbAsync($"-s {currentDevice} shell input keyevent 88");
            Thread.Sleep(500);
            await UpdateCurrentSongAsync();
            Debugger.show("Previous track requested.");
        }


        #endregion

        #region Media Controls Helper Methods

        private async Task SetSMTCImageAsync(string fileNameWithoutExtension, string artist)
        {
            if (mediaPlayer == null || smtcDisplayUpdater == null)
            {
                InitializeMediaPlayer();
                if (mediaPlayer == null || smtcDisplayUpdater == null)
                {
                    Debugger.show("Failed to initialize media player");
                    return;
                }
            }

            string folderPath = @"C:\Users\wille\Desktop\misc\images";
            string[] audioExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".webp", ".png", ".jpg", ".jpeg" };
            string[] imageExtensions = { ".webp", ".png", ".jpg", ".jpeg" };

            try
            {
                Debugger.show($"Starting cover art search for: '{fileNameWithoutExtension}' by '{artist}'");

                // === STEP 1: SONG SEARCH (partial match) ===
                Debugger.show("Step 1.1: Searching for audio files (partial match)...");

                List<string> matchingFiles = new List<string>();
                foreach (var ext in audioExtensions)
                {
                    var files = Directory.GetFiles(folderPath, "*" + ext, SearchOption.AllDirectories)
                        .Where(f => Path.GetFileNameWithoutExtension(f)
                        .IndexOf(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    matchingFiles.AddRange(files);
                }

                if (matchingFiles.Count == 0)
                {
                    Debugger.show("Step 1.1: No audio files found (partial match failed)");
                    await SetDefaultImage();
                    return;
                }

                Debugger.show($"Step 1.1: Found {matchingFiles.Count} file(s) matching partially");

                // === STEP 2: OPTIONAL ARTIST FILTER (partial match) ===
                List<string> artistMatches = new List<string>();

                if (!string.IsNullOrEmpty(artist) && matchingFiles.Count > 1)
                {
                    Debugger.show($"Step 1.2: Filtering by artist (partial match): '{artist}'");

                    artistMatches = matchingFiles
                        .Where(f => f.IndexOf(artist, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    foreach (var file in artistMatches)
                    {
                        Debugger.show($"Step 1.2: Artist partial match: {Path.GetFileName(file)}");
                    }
                }

                List<string> filesToProcess = artistMatches.Count > 0 ? artistMatches : matchingFiles;

                // Prioritize subfolder files
                var sortedFiles = filesToProcess.OrderByDescending(f =>
                {
                    string fileDir = Path.GetDirectoryName(f);
                    return !fileDir.Equals(folderPath, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                Debugger.show($"Step 2: Processing {sortedFiles.Count} file(s) (subfolder priority)");

                // === STEP 3: TRY EACH FILE UNTIL COVER ART FOUND ===
                foreach (var filePath in sortedFiles)
                {
                    Debugger.show($"Step 3: Processing file: {Path.GetFileName(filePath)}");

                    StorageFile imageFile = await TryGetCoverArtForFile(filePath, folderPath, imageExtensions);

                    if (imageFile != null)
                    {
                        smtcDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(imageFile);
                        smtcDisplayUpdater.Update();
                        Debugger.show("SMTC image updated successfully");
                        return;
                    }

                    Debugger.show($"Step 3: No cover art found for {Path.GetFileName(filePath)}, trying next file...");
                }

                Debugger.show("Step 3: All files exhausted, using default image");
                await SetDefaultImage();
            }
            catch (Exception ex)
            {
                Debugger.show($"Critical error in SetSMTCImageAsync: {ex.Message}");
                await SetDefaultImage();
            }
        }


        private async Task<StorageFile> TryGetCoverArtForFile(string filePath, string folderPath, string[] imageExtensions)
        {
            string fileDirectory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            bool isInSubfolder = !fileDirectory.Equals(folderPath, StringComparison.OrdinalIgnoreCase);

            // === FILES IN SUBFOLDERS ===
            if (isInSubfolder)
            {
                Debugger.show($"Step 2.2: File is in subfolder: {Path.GetFileName(fileDirectory)}");

                // Try cover.jpg and cover.png first
                string coverJpg = Path.Combine(fileDirectory, "cover.jpg");
                string coverPng = Path.Combine(fileDirectory, "cover.png");

                if (File.Exists(coverJpg))
                {
                    Debugger.show("Step 2.2: Found cover.jpg in subfolder");
                    return await StorageFile.GetFileFromPathAsync(coverJpg);
                }
                else if (File.Exists(coverPng))
                {
                    Debugger.show("Step 2.2: Found cover.png in subfolder");
                    return await StorageFile.GetFileFromPathAsync(coverPng);
                }

                Debugger.show("Step 2.2: No cover.jpg or cover.png found, trying TagLib");

                // Fall back to TagLib for subfolder files
                var tagLibImage = await TryExtractCoverFromTagLib(filePath);
                if (tagLibImage != null)
                {
                    return tagLibImage;
                }
            }
            // === FILES IN TOP-LEVEL FOLDER ===
            else
            {
                Debugger.show("Step 2.1: File is in top-level folder, searching for image with same name");

                foreach (var imgExt in imageExtensions)
                {
                    string imagePath = Path.Combine(fileDirectory, fileName + imgExt);
                    if (File.Exists(imagePath))
                    {
                        Debugger.show($"Step 2.1: Cover art found: {Path.GetFileName(imagePath)}");
                        return await StorageFile.GetFileFromPathAsync(imagePath);
                    }
                }

                Debugger.show("Step 2.1: No cover art image found, trying TagLib");

                // Fall back to TagLib for top-level files
                var tagLibImage = await TryExtractCoverFromTagLib(filePath);
                if (tagLibImage != null)
                {
                    return tagLibImage;
                }
            }

            return null;
        }

        private async Task<StorageFile> TryExtractCoverFromTagLib(string filePath)
        {
            Debugger.show("Step 2.3: Attempting to extract cover art from file metadata using TagLib");

            try
            {
                var tagFile = CoverArt.File.Create(filePath);
                if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                {
                    var pictureData = tagFile.Tag.Pictures[0].Data.Data;
                    string tempPath = Path.Combine(Path.GetTempPath(), "smtc_cover.jpg");
                    await File.WriteAllBytesAsync(tempPath, pictureData);
                    Debugger.show("Step 2.3: Cover art extracted from metadata successfully");
                    return await StorageFile.GetFileFromPathAsync(tempPath);
                }
                else
                {
                    Debugger.show("Step 2.3: No embedded cover art found in metadata");
                }
            }
            catch (Exception tagEx)
            {
                Debugger.show($"Step 2.3: TagLib extraction failed: {tagEx.Message}");
            }

            return null;
        }

        private async Task SetDefaultImage()
        {
            try
            {
                string defaultImagePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Phone Utils", "Resources", "logo.png"
                );

                Debugger.show("Step 2.4: Using default image");
                var imageFile = await StorageFile.GetFileFromPathAsync(defaultImagePath);
                smtcDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(imageFile);
                smtcDisplayUpdater.Update();
                Debugger.show("SMTC updated with default image");
            }
            catch (Exception ex)
            {
                Debugger.show($"Failed to set default image: {ex.Message}");
            }
        }







        private void UpdateMediaControls(string title, string artist, string album)
        {
            SetSMTCImageAsync(title, artist);
            if (mediaPlayer == null || smtcDisplayUpdater == null)
                return;

            try
            {
                var musicProperties = smtcDisplayUpdater.MusicProperties;
                musicProperties.Title = title;
                musicProperties.Artist = artist;
                musicProperties.AlbumTitle = album;

                smtcDisplayUpdater.Update();
                smtcControls.PlaybackStatus = MediaPlaybackStatus.Playing;

            }
            catch (Exception ex)
            {
            }
        }

        private void ClearMediaControls()
        {
            try
            {
                if (smtcDisplayUpdater != null)
                {
                    smtcDisplayUpdater.ClearAll();
                    smtcDisplayUpdater.Update();
                }

                if (mediaPlayer != null)
                {
                    mediaPlayer.Pause();
                    mediaPlayer.Dispose();
                    mediaPlayer = null;
                }

                smtcControls = null;
                smtcDisplayUpdater = null;
            }
            catch (Exception ex)
            {
                Debugger.show($"Failed to clear media controls: {ex.Message}");
            }
        }


        #endregion
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

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => DetectDeviceAsync();
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
                if(config.SpecialOptions != null && config.SpecialOptions.ShowDebugMessages)
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

                // Changed from discordClient.Dispose():
                if (smtcDisplayUpdater != null)
                {
                    smtcDisplayUpdater.ClearAll();
                }

                if (mediaPlayer != null)
                {
                    mediaPlayer.Dispose();
                }
            }
            catch { }
        }
        #endregion
    }
}
