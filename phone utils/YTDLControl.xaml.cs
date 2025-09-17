﻿using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using phone_utils;
using System.Windows.Media.Media3D;

namespace YTDLApp
{
    public partial class YTDLControl : UserControl
    {
        public static string _ADB_PATH;
        private MainWindow _main;
        private string _currentDevice;

        public YTDLControl(MainWindow main, string device, string ADB)
        {
            InitializeComponent();
            _main = main;
            _ADB_PATH = ADB;
            _currentDevice = device;
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "config.json"
            );

            var config = SetupControl.ConfigManager.Load(configPath);

            cmbType.SelectedIndex = config.YTDL.DownloadType;
            chkBackground.IsChecked = config.YTDL.BackgroundCheck;
        }


        public Task RunAdbAsync(string args)
        {
            return AdbHelper.RunAdbAsync(args);
        }

        private async void btnSend_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings();
            string url = txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a YouTube URL.");
                return;
            }

            string type = "video";
            if (cmbType.SelectedItem is ComboBoxItem selectedItem)
                type = selectedItem.Content.ToString().ToLower();

            bool background = chkBackground.IsChecked == true;

            // Send the intent to YTDL
            string args = $"shell am start -a android.intent.action.SEND " +
                          $"-c android.intent.category.DEFAULT " +
                          $"-t text/plain " +
                          $"-n com.deniscerri.ytdl/.receiver.ShareActivity " +
                          $"--es android.intent.extra.TEXT \"{url}\" " +
                          $"--es TYPE \"{type}\" " +
                          $"--es BACKGROUND \"{background.ToString().ToLower()}\"";

            await RunAdbAsync($"-s {_currentDevice} {args}");

            // Wait a short moment to let the app open
            await Task.Delay(1000); // 1 second delay

            // Tap the download button at coordinates (800, 700)
            string tapArgs = "shell input tap 800 700";
            await RunAdbAsync($"-s {_currentDevice} {tapArgs}");
        }

        private void SaveCurrentSettings()
        {
            // User-writable folder for config
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Phone Utils",
                "config.json"
            );

            // Ensure folder exists before saving
            string folder = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var config = SetupControl.ConfigManager.Load(configPath);

            config.YTDL.DownloadType = cmbType.SelectedIndex;
            config.YTDL.BackgroundCheck = chkBackground.IsChecked == true;

            SetupControl.ConfigManager.Save(configPath, config);
        }

    }
}
