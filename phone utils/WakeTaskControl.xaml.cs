using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace phone_utils
{
    public partial class WakeTaskControl : UserControl
    {
        private const string TaskPrefix = "WakePC_";
        public ObservableCollection<ScheduledTask> Tasks { get; set; } = new ObservableCollection<ScheduledTask>();

        public WakeTaskControl()
        {
            InitializeComponent();
            DataContext = this; // Important for binding
            LoadScheduledTasks();
        }

        private void LoadScheduledTasks()
        {
            Tasks.Clear();

            try
            {
                var psi = new ProcessStartInfo("schtasks")
                {
                    Arguments = "/Query /FO LIST /V",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.Default
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    string taskName = null;
                    string nextRun = null;
                    string lastRun = null;

                    foreach (var line in output.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                    {
                        if (line.StartsWith("TaskName:", StringComparison.InvariantCultureIgnoreCase))
                        {
                            // Before starting new task, process previous one
                            if (!string.IsNullOrEmpty(taskName))
                            {
                                ProcessTaskBlock(taskName, nextRun, lastRun);
                            }

                            // Start new task block
                            taskName = line.Substring("TaskName:".Length).Trim().TrimStart('\\');
                            nextRun = null;
                            lastRun = null;
                        }
                        else if (line.StartsWith("Next Run Time:", StringComparison.InvariantCultureIgnoreCase))
                        {
                            nextRun = line.Substring("Next Run Time:".Length).Trim();
                        }
                        else if (line.StartsWith("Last Run Time:", StringComparison.InvariantCultureIgnoreCase))
                        {
                            lastRun = line.Substring("Last Run Time:".Length).Trim();
                        }
                    }

                    // Process the last block
                    if (!string.IsNullOrEmpty(taskName))
                    {
                        ProcessTaskBlock(taskName, nextRun, lastRun);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading scheduled tasks: " + ex.Message);
            }
        }

        private void ProcessTaskBlock(string taskName, string nextRun, string lastRun)
        {
            if (!taskName.StartsWith(TaskPrefix, StringComparison.InvariantCultureIgnoreCase))
                return;

            // Only delete tasks that ran (NextRun = N/A and LastRun exists)
            if (!string.IsNullOrEmpty(nextRun) && nextRun.Equals("N/A", StringComparison.InvariantCultureIgnoreCase)
                && !string.IsNullOrEmpty(lastRun) && !lastRun.Equals("N/A", StringComparison.InvariantCultureIgnoreCase))
            {
                try
                {
                    var delPsi = new ProcessStartInfo("schtasks")
                    {
                        Arguments = $"/Delete /TN \"{taskName}\" /F",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    Process.Start(delPsi)?.WaitForExit();
                }
                catch { /* ignore errors */ }
                return; // skip adding to list
            }

            // Add to list
            Tasks.Add(new ScheduledTask
            {
                Name = taskName.Substring(TaskPrefix.Length),
                NextRun = string.IsNullOrEmpty(nextRun) || nextRun.Equals("N/A", StringComparison.InvariantCultureIgnoreCase)
                    ? "Already run" : nextRun
            });
        }






        private void CreateTaskButton_Click(object sender, RoutedEventArgs e)
        {
            string customName = TaskNameTextBox.Text.Trim();
            string hour = HourTextBox.Text.Trim().PadLeft(2, '0');
            string minute = MinuteTextBox.Text.Trim().PadLeft(2, '0');

            if (string.IsNullOrEmpty(customName) || string.IsNullOrEmpty(hour) || string.IsNullOrEmpty(minute))
            {
                MessageBox.Show("Please enter task name, hour, and minute.");
                return;
            }

            string fullTaskName = TaskPrefix + customName;

            try
            {
                string psCommand = "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{SHIFT}')";
                string schtasksArgs = $"/Create /TN \"{fullTaskName}\" /TR \"powershell.exe -Command \\\"{psCommand}\\\"\" /SC ONCE /ST {hour}:{minute} /RL HIGHEST /F";

                var psi = new ProcessStartInfo("schtasks", schtasksArgs)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                Process.Start(psi)?.WaitForExit();
                Thread.Sleep(2000);

                var psiPS = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas",
                    Arguments = $"-Command \"$t = Get-ScheduledTask -TaskName '{fullTaskName}'; $s = $t.Settings; $s.WakeToRun = $true; Set-ScheduledTask -TaskName '{fullTaskName}' -Settings $s\""
                };
                Process.Start(psiPS)?.WaitForExit();

                InfoText.Text = $"Task \"{customName}\" scheduled at {hour}:{minute}.";
                LoadScheduledTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error creating task: " + ex.Message);
            }
        }

        private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (TaskListView.SelectedItem is ScheduledTask selected)
            {
                string fullTaskName = TaskPrefix + selected.Name;

                try
                {
                    var psi = new ProcessStartInfo("schtasks")
                    {
                        Arguments = $"/Delete /TN \"{fullTaskName}\" /F",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    Process.Start(psi)?.WaitForExit();

                    InfoText.Text = $"Task \"{selected.Name}\" deleted successfully.";
                    LoadScheduledTasks();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error deleting task: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Please select a task to delete.");
            }
        }
    }

    public class ScheduledTask : INotifyPropertyChanged
    {
        private string _name;
        private string _nextRun;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged("Name"); }
        }

        public string NextRun
        {
            get => _nextRun;
            set { _nextRun = value; OnPropertyChanged("NextRun"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
