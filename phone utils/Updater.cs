using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;



/// <summary>
/// very important reminder as to how the updater works
/// it first checks numerically eg 1.2.0 or 1.3.0 (this takes priority)
/// if it finds multiple matching version eg 1.2-beta17 or 1.2-beta16 (-betanumber get's cuttoff) it will switch checking lexographically with beta
/// </summary>
public static class Updater
{
    private const string RepoOwner = "snail-boi";
    private const string RepoName = "Vermilia-phone-utils";
    private const string InstallerPrefix = "Vermilia.Phone.Utils.Installer"; // Updated prefix

    /// <summary>
    /// Call this at app startup (Option 1: fire-and-forget) or in Loaded event.
    /// </summary>
    /// <param name="currentVersion">Your current app version string, e.g., "v1.2-beta 10"</param>
    public static async Task CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PhoneUtilsUpdater/1.0");

            string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            string json = await client.GetStringAsync(apiUrl);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return;

            // Find the release with the highest numeric version
            JsonElement? latestRelease = null;
            Version latestNumericVersion = null;

            foreach (JsonElement release in root.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out JsonElement tagElem))
                    continue;

                string tagName = tagElem.GetString();
                if (string.IsNullOrEmpty(tagName))
                    continue;

                // Extract numeric version (e.g., "1.2.11")
                var match = Regex.Match(tagName, @"\d+(\.\d+)*");
                if (!match.Success) continue;

                if (!Version.TryParse(match.Value.Replace("-", "."), out Version releaseVersion))
                    continue;

                if (latestNumericVersion == null || releaseVersion > latestNumericVersion)
                {
                    latestNumericVersion = releaseVersion;
                    latestRelease = release;
                }
            }

            if (latestRelease == null)
                return;

            string latestVersion = latestRelease.Value.GetProperty("tag_name").GetString();
            if (!IsNewerVersion(latestVersion, currentVersion))
                return;

            // Find installer in assets
            if (!latestRelease.Value.TryGetProperty("assets", out JsonElement assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                return;

            JsonElement? installerAsset = null;
            foreach (JsonElement asset in assetsElem.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out JsonElement nameElem))
                    continue;

                string name = nameElem.GetString();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset = asset;
                    break;
                }
            }

            if (installerAsset == null)
                return;

            string installerName = installerAsset.Value.GetProperty("name").GetString();
            string downloadUrl = installerAsset.Value.GetProperty("browser_download_url").GetString();

            // Fetch patch notes from release body
            string patchNotes = GetReleaseNotes(latestRelease.Value);

            // Ask user to update, showing patch notes
            var result = MessageBox.Show(
                $"A new version {latestVersion} is available!\n\nPatch notes:\n\n{patchNotes}\n\nDo you want to download and install it?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            // Download installer to temp folder
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), installerName);
            byte[] data = await client.GetByteArrayAsync(downloadUrl);
            await System.IO.File.WriteAllBytesAsync(tempPath, data);

            // Launch installer
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update check failed:\n{ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Helper to extract release notes from GitHub release JSON
    private static string GetReleaseNotes(JsonElement release)
    {
        if (release.TryGetProperty("body", out JsonElement bodyElem))
        {
            string notes = bodyElem.GetString() ?? "";
            return notes.Trim();
        }
        return "No patch notes available.";
    }

    /// <summary>
    /// Compare two version strings like "v1.2-beta 10" or "v1.2.10.0"
    /// </summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        string cleanLatest = latest.TrimStart('v', 'V').Trim();
        string cleanCurrent = current.TrimStart('v', 'V').Trim();

        var rx = new Regex(@"\d+");
        var latestNumbers = rx.Matches(cleanLatest);
        var currentNumbers = rx.Matches(cleanCurrent);

        int len = Math.Min(latestNumbers.Count, currentNumbers.Count);
        for (int i = 0; i < len; i++)
        {
            int lv = int.Parse(latestNumbers[i].Value);
            int cv = int.Parse(currentNumbers[i].Value);
            if (lv > cv) return true;
            if (lv < cv) return false;
        }

        // Fallback to lexicographical compare (handles beta labels)
        return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
