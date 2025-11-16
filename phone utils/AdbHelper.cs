using System.Diagnostics;
using System.Text;

namespace phone_utils
{
    public static class AdbHelper
    {
        /// <summary>
        /// Runs ADB command without capturing output
        /// </summary>
        /// <param name="args">ADB command arguments</param>
        /// <returns>Task representing the async operation</returns>
        public static Task RunAdbAsync(string args)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(MainWindow.ADB_PATH, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    var proc = Process.Start(psi);
                    proc.WaitForExit();
                }
                catch (Exception ex)
                {
                    Debugger.show($"ADB error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Runs ADB command and captures the output
        /// </summary>
        /// <param name="args">ADB command arguments</param>
        /// <returns>Task with the command output as string</returns>
        public static Task<string> RunAdbCaptureAsync(string args)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(MainWindow.ADB_PATH, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                try
                {
                    var proc = Process.Start(psi);
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    return output;
                }
                catch (Exception ex)
                {
                    Debugger.show($"ADB error: {ex.Message}");
                    return "";
                }
            });
        }
    }
}