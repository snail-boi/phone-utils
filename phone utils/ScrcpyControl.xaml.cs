﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static phone_utils.SetupControl;

namespace phone_utils
{
    public partial class ScrcpyControl : UserControl, IDisposable
    {
        private readonly MainWindow _main;
        private readonly string _device;
        private readonly string _SCRCPY_PATH;
        private readonly string _ADB_PATH;
        private readonly AppConfig _appConfig;
        private GlobalHotkeyManager _hotkeyManager;
        private bool _hotkeysEnabled = true;
        private bool _suppressEvents = false;

        public ScrcpyControl(MainWindow main, string device, string scrcpyPath, string adb, AppConfig config)
        {
            InitializeComponent();
            _main = main;
            _device = device;
            _SCRCPY_PATH = scrcpyPath;
            _ADB_PATH = adb;
            _appConfig = config;

            BtnStartScrcpy.Click += BtnStartScrcpy_Click;
            this.Loaded += ScrcpyControl_Loaded;


            Debugger.show("ScrcpyControl initialized with device: " + _device); // Added to trace initialization
            LoadSavedSettings();
        }

        private void ScrcpyControl_Loaded(object sender, RoutedEventArgs e)
        {
            Debugger.show("ScrcpyControl loaded, hotkeysEnabled: " + _hotkeysEnabled); // Trace control load
            if (_hotkeysEnabled) InitializeGlobalHotkeys();
            LoadInstalledApps();
        }

        #region Hotkeys

        private void InitializeGlobalHotkeys()
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow == null) 
                {
                    Debugger.show("Parent window is null, skipping hotkey initialization."); // Trace null parent
                    return;
                }

                _hotkeyManager?.Dispose();
                _hotkeyManager = new GlobalHotkeyManager(parentWindow);

                _hotkeyManager.MediaPlayPause += () => RunAdbKeyEvent(85);
                _hotkeyManager.MediaNext += () => RunAdbKeyEvent(87);
                _hotkeyManager.MediaPrev += () => RunAdbKeyEvent(88);
                _hotkeyManager.ShiftVolumeUp += () => RunAdbKeyEvent(24);
                _hotkeyManager.ShiftVolumeDown += () => RunAdbKeyEvent(25);
                _hotkeyManager.InsertPressed += OnInsertPressed;
                _hotkeyManager.PageUpPressed += () => LaunchApp("anddea.youtube");
                _hotkeyManager.PageDownPressed += () => LaunchApp("in.krosbits.musicolet");
                _hotkeyManager.EndPressed += () => LaunchApp("com.anilab.android");

                Debugger.show("Global hotkeys initialized."); // Confirm hotkey setup
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize hotkeys: {ex.Message}", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Debugger.show("Hotkey initialization error: " + ex.Message); // Trace hotkey exception
            }
        }

        private void UnregisterGlobalHotkeys()
        {
            Debugger.show("Unregistering global hotkeys."); // Trace hotkey cleanup
            _hotkeyManager?.Dispose();
            _hotkeyManager = null;
        }

        private async Task RunAdbKeyEvent(int keyCode)
        {
            Debugger.show("RunAdbKeyEvent called with keyCode: " + keyCode); // Trace key events
            if (!_hotkeysEnabled || string.IsNullOrEmpty(_device)) return;
            await AdbHelper.RunAdbAsync($"-s {_device} shell input keyevent {keyCode}");
        }

        private async void OnInsertPressed()
        {
            Debugger.show("unlocking phone."); // Trace Insert pressed
            if (!_hotkeysEnabled || string.IsNullOrEmpty(_device) || _device.Contains(":")) return;
            await AdbHelper.RunAdbAsync($"-s {_device} shell input text {_appConfig.SelectedDevicePincode}");
        }

        private async Task LaunchApp(string packageName)
        {
            Debugger.show("Launching app: " + packageName); // Trace which app is being launched
            if (!_hotkeysEnabled || string.IsNullOrEmpty(_device)) return;
            await AdbHelper.RunAdbAsync($"-s {_device} shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1");
        }

        private void ChkEnableHotkeys_Checked(object sender, RoutedEventArgs e) => ToggleHotkeys(true);
        private void ChkEnableHotkeys_Unchecked(object sender, RoutedEventArgs e) => ToggleHotkeys(false);

        private void ToggleHotkeys(bool enabled)
        {
            Debugger.show("ToggleHotkeys called. Enabled: " + enabled); // Trace toggle action
            _hotkeysEnabled = enabled;
            if (enabled) InitializeGlobalHotkeys();
            else UnregisterGlobalHotkeys();
        }

        #endregion

        #region AudioCheckboxes

        private void ChkAudio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            _suppressEvents = true;
            Debugger.show("ChkAudio_Checked fired for: " + (sender as CheckBox)?.Name); // Trace which checkbox triggered

            switch ((sender as CheckBox)?.Name)
            {
                case "ChkNoAudio":
                    ChkPlaybackAudio.IsChecked = false;
                    ChkAudioOnly.IsChecked = false;
                    Chkaudiobuffer.IsChecked = false;
                    Chkaudiobuffer.IsEnabled = false;
                    break;
                case "ChkPlaybackAudio":
                    ChkNoAudio.IsChecked = false;
                    ChkAudioOnly.IsChecked = false;
                    Chkaudiobuffer.IsEnabled = true;
                    break;
                case "ChkAudioOnly":
                    ChkNoAudio.IsChecked = false;
                    ChkPlaybackAudio.IsChecked = false;
                    Chkaudiobuffer.IsChecked = false;
                    Chkaudiobuffer.IsEnabled = false;
                    break;
            }

            _suppressEvents = false;
        }

        private void ChkAudio_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            _suppressEvents = true;
            Debugger.show("ChkAudio_Unchecked fired for: " + (sender as CheckBox)?.Name); // Trace checkbox uncheck

            if (ChkNoAudio.IsChecked == false && ChkPlaybackAudio.IsChecked == false && ChkAudioOnly.IsChecked == false)
            {
                var chk = sender as CheckBox;
                chk.IsChecked = true;
                Debugger.show("Reverting unchecked to true for: " + chk.Name); // Trace auto-reset
            }

            _suppressEvents = false;
        }

        #endregion

        #region Settings

        private void LoadSavedSettings()
        {
            Debugger.show("Loading saved settings..."); // Trace loading settings
            var settings = _appConfig.ScrcpySettings;

            ChkAudioOnly.IsChecked = settings.AudioOnly;
            ChkNoAudio.IsChecked = settings.NoAudio;
            ChkPlaybackAudio.IsChecked = settings.PlaybackAudio;
            ChkMaxSize.IsChecked = settings.LimitMaxSize;
            TxtMaxSize.Text = settings.MaxSize.ToString();
            ChkStayAwake.IsChecked = settings.StayAwake;
            ChkTop.IsChecked = settings.Top;
            ChkTurnScreenOff.IsChecked = settings.TurnScreenOff;
            ChkLockAfterExit.IsChecked = settings.LockPhone;
            ChkEnableHotkeys.IsChecked = settings.EnableHotkeys;
            Chkaudiobuffer.IsChecked = settings.audiobuffer;
            Chkvideobuffer.IsChecked = settings.videobuffer;
            TxtAudioBuffer.Text = settings.AudioBufferSize.ToString();
            TxtVideoBuffer.Text = settings.VideoBufferSize.ToString();
            CmbAndroidApps.Text = settings.VirtualDisplayApp ?? "";
            CmbCameraList.SelectedIndex = settings.CameraType;

            _hotkeysEnabled = settings.EnableHotkeys;

            // Apply button colors
            Application.Current.Resources["ButtonBackground"] =
                (SolidColorBrush)new BrushConverter().ConvertFromString(_appConfig.ButtonStyle.Background);
            Application.Current.Resources["ButtonForeground"] =
                (SolidColorBrush)new BrushConverter().ConvertFromString(_appConfig.ButtonStyle.Foreground);
            Application.Current.Resources["ButtonHover"] =
                (SolidColorBrush)new BrushConverter().ConvertFromString(_appConfig.ButtonStyle.Hover);

            Debugger.show("Saved settings applied."); // Confirm settings applied
        }

        private void SaveCurrentSettings()
        {
            Debugger.show("Saving current settings..."); // Trace saving settings
            var settings = _appConfig.ScrcpySettings;

            settings.AudioOnly = ChkAudioOnly.IsChecked == true;
            settings.NoAudio = ChkNoAudio.IsChecked == true;
            settings.PlaybackAudio = ChkPlaybackAudio.IsChecked == true;
            settings.LimitMaxSize = ChkMaxSize.IsChecked == true;
            settings.MaxSize = int.TryParse(TxtMaxSize.Text, out int maxSize) ? maxSize : 2440;
            settings.StayAwake = ChkStayAwake.IsChecked == true;
            settings.TurnScreenOff = ChkTurnScreenOff.IsChecked == true;
            settings.Top = ChkTop.IsChecked == true;
            settings.LockPhone = ChkLockAfterExit.IsChecked == true;
            settings.EnableHotkeys = ChkEnableHotkeys.IsChecked == true;
            settings.audiobuffer = Chkaudiobuffer.IsChecked == true;
            settings.videobuffer = Chkvideobuffer.IsChecked == true;
            settings.AudioBufferSize = int.TryParse(TxtAudioBuffer.Text, out int abuffer) ? abuffer : 50;
            settings.VideoBufferSize = int.TryParse(TxtVideoBuffer.Text, out int vbuffer) ? vbuffer : 50;
            settings.VirtualDisplayApp = CmbAndroidApps.Text;
            settings.CameraType = CmbCameraList.SelectedIndex;

            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phone Utils", "config.json");
            if (!Directory.Exists(Path.GetDirectoryName(configPath))) Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            ConfigManager.Save(configPath, _appConfig);
            Debugger.show("Settings saved to " + configPath); // Confirm where settings were saved
        }

        #endregion

        #region Scrcpy Launch

        private async Task LaunchScrcpyAsync(List<string> args)
        {
            Debugger.show("Launching scrcpy with args: " + string.Join(" ", args)); // Trace scrcpy launch args
            SaveCurrentSettings();
            string finalArgs = string.Join(" ", args);
            await RunScrcpyAsync(finalArgs);
        }

        private async void BtnDisplay_click(object sender, RoutedEventArgs e)
        {
            var args = BuildScrcpyArgs(display: true);
            await LaunchScrcpyAsync(args);
        }

        private async void BtnCamera_click(object sender, RoutedEventArgs e)
        {
            var args = BuildScrcpyArgs(camera: true);
            await LaunchScrcpyAsync(args);
        }

        private async void BtnStartScrcpy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_device))
            {
                MessageBox.Show("No device connected!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Debugger.show("BtnStartScrcpy_Click aborted: no device."); // Trace missing device
                return;
            }

            var args = BuildScrcpyArgs();
            await LaunchScrcpyAsync(args);
        }

        private List<string> BuildScrcpyArgs(bool display = false, bool camera = false)
        {
            var args = new List<string>();
            args.Add($"--window-title=\"{_appConfig.SelectedDeviceName}\"");

            Debugger.show("Building scrcpy args. display=" + display + ", camera=" + camera); // Trace arg build

            if (ChkAudioOnly.IsChecked == true && !camera)
            {
                args.Add("--no-video");
                args.Add("--no-window");
                args.Add("--audio-source=playback");
            }

            if (ChkNoAudio.IsChecked == true) args.Add("--no-audio");
            if (ChkPlaybackAudio.IsChecked == true) args.Add("--audio-source=playback");
            if (Chkaudiobuffer.IsChecked == true && int.TryParse(TxtAudioBuffer.Text, out int audioBuffer) && audioBuffer > 0)
                args.Add($"--audio-buffer={audioBuffer}");
            else if (Chkaudiobuffer.IsChecked == true)
                MessageBox.Show("Invalid Audio Buffer value.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (Chkvideobuffer.IsChecked == true && int.TryParse(TxtVideoBuffer.Text, out int videoBuffer) && videoBuffer > 0)
                args.Add($"--video-buffer={videoBuffer}");
            else if (Chkvideobuffer.IsChecked == true)
                MessageBox.Show("Invalid Audio Buffer value.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ChkStayAwake.IsChecked == true && !camera) args.Add("--stay-awake");
            if (ChkTurnScreenOff.IsChecked == true && !camera) args.Add("--turn-screen-off");
            if (ChkLockAfterExit.IsChecked == true && !camera && !display) args.Add("--power-off-on-close");
            if (ChkTop.IsChecked == true) args.Add("--always-on-top");
            if (ChkMaxSize.IsChecked == true && int.TryParse(TxtMaxSize.Text, out int maxSize) && maxSize > 0)
                args.Add($"--max-size={maxSize}");
            else if (ChkMaxSize.IsChecked == true)
                MessageBox.Show("Invalid Max Size value.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (display && int.TryParse(TxtMaxSize.Text, out int width))
            {
                args.Add($"--new-display={width}x{width * 9 / 16}");
                if (!string.IsNullOrWhiteSpace(CmbAndroidApps.Text))
                    args.Add($"--start-app={CmbAndroidApps.Text}");
            }


            if (camera)
            {
                args.Add("--video-source=camera");
                switch (CmbCameraList.Text)
                {
                    case "Front": args.Add("--camera-facing=front"); break;
                    case "Back": args.Add("--camera-facing=back"); break;
                    case "External": args.Add("--camera-facing=external"); break;
                }
            }

            Debugger.show("Final scrcpy args: " + string.Join(" ", args)); // Trace final args list
            return args;
        }

        public Task RunScrcpyAsync(string args)
        {
            Debugger.show("RunScrcpyAsync called with args: " + args); // Trace scrcpy async launch
            _main.DeviceStatusText.Text = args.Contains("--no-audio")
                ? "Device Status: Casting without audio"
                : "Device Status: Casting with audio";

            return Task.Run(async () =>
            {
                if (!File.Exists(_SCRCPY_PATH))
                {
                    Debugger.show("scrcpy.exe not found at path: " + _SCRCPY_PATH); // Trace missing executable
                    MessageBox.Show("scrcpy.exe not found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {

                    var psi = new ProcessStartInfo
                    {
                        FileName = _SCRCPY_PATH,
                        Arguments = $"-s {_device} {args}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Debugger.show("Starting scrcpy process with command: " + psi.FileName + " " + psi.Arguments); // Trace process start

                    var scrcpyProcess = Process.Start(psi);
                    await Task.Delay(1000);
                    Dispatcher.Invoke(() => _main.DeviceStatusText.Text += " - Ready");
                    Debugger.show("scrcpy process started successfully."); // Confirm process start

                    while (!scrcpyProcess.HasExited)
                        await Task.Delay(500);


                    Debugger.show("scrcpy process exited."); // Trace exit
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"scrcpy launch failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Debugger.show("scrcpy launch exception: " + ex.Message); // Trace exception
                }
            });
        }
        #endregion

        #region Installed Apps

        private async void LoadInstalledApps()
        {
            Debugger.show("Loading installed apps..."); // Trace app loading start
            if (string.IsNullOrEmpty(_device))
            {
                Debugger.show("LoadInstalledApps aborted: no device."); // Trace missing device
                return;
            }


            try
            {
                var output = await AdbHelper.RunAdbCaptureAsync($"-s {_device} shell pm list packages");
                var packages = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(l => l.StartsWith("package:"))
                                     .Select(l => l.Substring(8))
                                     .OrderBy(l => l)
                                     .ToList();

                CmbAndroidApps.ItemsSource = packages;
                Debugger.show("Installed apps loaded: " + packages.Count + " apps."); // Confirm loaded apps
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load installed apps: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debugger.show("LoadInstalledApps exception: " + ex.Message); // Trace exception
            }
        }

        #endregion

        public void Dispose()
        {
            Debugger.show("Disposing ScrcpyControl, unregistering hotkeys."); // Trace dispose
            UnregisterGlobalHotkeys();
        }
    }
}
