using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace phone_utils
{
    public partial class RemoteFolderPicker : Window
    {
        public string SelectedFolder { get; private set; } = "/";

        private readonly string _device;

        public RemoteFolderPicker(string device)
        {
            InitializeComponent();
            _device = device;
            LoadRoot();
        }

        private void LoadRoot()
        {
            // Start at /storage/emulated/0 instead of /
            string initialPath = "/storage/emulated/0";

            var rootNode = new TreeViewItem
            {
                Header = "Internal Storage", // nicer display name
                Tag = initialPath
            };

            rootNode.Items.Add(null); // Dummy item for expand arrow
            rootNode.Expanded += FolderTreeItem_Expanded; // Hook the expanded event
            FolderTree.Items.Add(rootNode);
        }



        private async void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Items.Count == 1 && item.Items[0] == null)
            {
                item.Items.Clear();
                string path = (string)item.Tag;
                var subdirs = await GetRemoteDirectoriesAsync(path);
                foreach (var dir in subdirs)
                {
                    var node = new TreeViewItem { Header = dir, Tag = path + "/" + dir };
                    node.Items.Add(null); // Dummy for expand
                    node.Expanded += FolderTreeItem_Expanded;
                    item.Items.Add(node);
                }
            }
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (FolderTree.SelectedItem is TreeViewItem item)
            {
                SelectedFolder = (string)item.Tag;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async Task<List<string>> GetRemoteDirectoriesAsync(string path)
        {
            // Use adb shell ls -d */ to get directories only
            string cmd = $"shell ls -d \"{path}\"/*/";
            string output = await RunAdbCaptureAsync($"-s {_device} {cmd}");

            var dirs = new List<string>();
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = line.Trim().TrimEnd('/');
                if (!string.IsNullOrEmpty(name))
                    dirs.Add(System.IO.Path.GetFileName(name));
            }
            return dirs;
        }

        private static Task<string> RunAdbCaptureAsync(string args)
        {
            return AdbHelper.RunAdbCaptureAsync(args);
        }

    }
}
