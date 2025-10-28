using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using CoverArt = TagLib;

namespace phone_utils
{
    // MediaController encapsulates all SMTC / MediaPlayer logic
    internal class MediaController
    {
        private MediaPlayer mediaPlayer;
        private SystemMediaTransportControls smtcControls;
        private SystemMediaTransportControlsDisplayUpdater smtcDisplayUpdater;
        private readonly Dispatcher dispatcher;
        private readonly Func<string> getCurrentDevice; // callback to get device id
        private readonly Func<Task> updateCurrentSongCallback; // optional callback to refresh song
        private string lastSMTCTitle;

        public MediaController(Dispatcher dispatcher, Func<string> getCurrentDevice, Func<Task> updateCurrentSongCallback)
        {
            this.dispatcher = dispatcher;
            this.getCurrentDevice = getCurrentDevice;
            this.updateCurrentSongCallback = updateCurrentSongCallback;
        }

        public void Initialize()
        {
            try
            {
                mediaPlayer = new MediaPlayer();
                smtcControls = mediaPlayer.SystemMediaTransportControls;
                smtcDisplayUpdater = smtcControls.DisplayUpdater;

                smtcControls.IsEnabled = true;
                smtcControls.IsPlayEnabled = true;
                smtcControls.IsPauseEnabled = true;
                smtcControls.IsNextEnabled = true;
                smtcControls.IsPreviousEnabled = true;

                smtcControls.ButtonPressed += SmTc_ButtonPressed;
                smtcDisplayUpdater.Type = MediaPlaybackType.Music;
            }
            catch (Exception ex)
            {
                Debugger.show($"MediaPlayer initialization failed: {ex.Message}");
            }
        }

        private async void SmTc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            Debugger.show($"SMTC Button Pressed: {args.Button}");
            await dispatcher.InvokeAsync(async () =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        await PlayTrack();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        await PauseTrack();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        await NextTrack();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        await PreviousTrack();
                        break;
                }
            });
        }

        private async Task PlayTrack()
        {
            var device = getCurrentDevice();
            if (string.IsNullOrEmpty(device)) return;
            await AdbHelper.RunAdbAsync($"-s {device} shell input keyevent 85");
            if (smtcControls != null)
                smtcControls.PlaybackStatus = MediaPlaybackStatus.Playing;
            Debugger.show("Play requested");
        }

        private async Task PauseTrack()
        {
            var device = getCurrentDevice();
            if (string.IsNullOrEmpty(device)) return;
            await AdbHelper.RunAdbAsync($"-s {device} shell input keyevent 85");
            if (smtcControls != null)
                smtcControls.PlaybackStatus = MediaPlaybackStatus.Paused;
            Debugger.show("Pause requested.");
        }

        private async Task NextTrack()
        {
            var device = getCurrentDevice();
            if (string.IsNullOrEmpty(device)) return;
            await AdbHelper.RunAdbAsync($"-s {device} shell input keyevent 87");
            await Task.Delay(500);
            if (updateCurrentSongCallback != null) await updateCurrentSongCallback();
            Debugger.show("Next track requested.");
        }

        private async Task PreviousTrack()
        {
            var device = getCurrentDevice();
            if (string.IsNullOrEmpty(device)) return;
            await AdbHelper.RunAdbAsync($"-s {device} shell input keyevent 88");
            await Task.Delay(500);
            if (updateCurrentSongCallback != null) await updateCurrentSongCallback();
            Debugger.show("Previous track requested.");
        }

        public async Task UpdateMediaControlsAsync(string title, string artist, string album, bool isPlaying)
        {
            if (string.Equals(lastSMTCTitle, title, StringComparison.OrdinalIgnoreCase))
            {
                Debugger.show($"SMTC title '{title}' is same as last. Skipping update.");
                return;
            }

            lastSMTCTitle = title;

            if (smtcControls != null)
                smtcControls.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

            TimeSpan? duration = await SetSMTCImageAsync(title, artist);
            if (mediaPlayer == null || smtcDisplayUpdater == null)
                return;

            try
            {
                var musicProperties = smtcDisplayUpdater.MusicProperties;
                musicProperties.Title = title;
                musicProperties.Artist = artist;
                musicProperties.AlbumTitle = album;

                smtcDisplayUpdater.Update();

                if (duration.HasValue && smtcControls != null)
                {
                    var timelineProps = new SystemMediaTransportControlsTimelineProperties
                    {
                        StartTime = TimeSpan.Zero,
                        EndTime = duration.Value,
                    };
                    smtcControls.UpdateTimelineProperties(timelineProps);
                }
            }
            catch (Exception ex)
            {
                Debugger.show($"Failed updating SMTC metadata: {ex.Message}");
            }
        }

        public void Clear()
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

        private async Task<TimeSpan?> SetSMTCImageAsync(string fileNameWithoutExtension, string artist)
        {
            if (mediaPlayer == null || smtcDisplayUpdater == null)
            {
                Initialize();
                if (mediaPlayer == null || smtcDisplayUpdater == null)
                {
                    Debugger.show("Failed to initialize media player");
                    return null;
                }
            }

            string folderPath = @"C:\Users\wille\Desktop\Audio";
            string[] audioExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus" };
            string[] imageExtensions = { ".webp", ".png", ".jpg", ".jpeg" };

            try
            {
                Debugger.show($"Starting cover art search for: '{fileNameWithoutExtension}' by '{artist}'");

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
                    Debugger.show("No audio files found (partial match failed)");
                    await SetDefaultImage();
                    return null;
                }

                List<string> artistMatches = new List<string>();

                if (!string.IsNullOrEmpty(artist) && matchingFiles.Count > 1)
                {
                    artistMatches = matchingFiles
                        .Where(f => f.IndexOf(artist, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }

                List<string> filesToProcess = artistMatches.Count > 0 ? artistMatches : matchingFiles;

                string filePathForDuration = filesToProcess.FirstOrDefault(f => audioExtensions.Contains(Path.GetExtension(f).ToLower()));

                TimeSpan? duration = null;
                if (filePathForDuration != null)
                {
                    try
                    {
                        var tagFile = CoverArt.File.Create(filePathForDuration);
                        duration = tagFile.Properties.Duration;
                    }
                    catch (Exception ex)
                    {
                        Debugger.show($"Failed to read audio file duration: {ex.Message}");
                    }
                }

                var sortedFiles = filesToProcess.OrderByDescending(f =>
                {
                    string fileDir = Path.GetDirectoryName(f);
                    return !fileDir.Equals(folderPath, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                foreach (var filePath in sortedFiles)
                {
                    StorageFile imageFile = await TryGetCoverArtForFile(filePath, folderPath, imageExtensions);

                    if (imageFile != null)
                    {
                        smtcDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(imageFile);
                        smtcDisplayUpdater.Update();
                        Debugger.show("SMTC image updated successfully");
                        return duration;
                    }
                }

                await SetDefaultImage();
                return duration;
            }
            catch (Exception ex)
            {
                Debugger.show($"Critical error in SetSMTCImageAsync: {ex.Message}");
                await SetDefaultImage();
                return null;
            }
        }

        private async Task<StorageFile> TryGetCoverArtForFile(string filePath, string folderPath, string[] imageExtensions)
        {
            string fileDirectory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            bool isInSubfolder = !fileDirectory.Equals(folderPath, StringComparison.OrdinalIgnoreCase);

            if (isInSubfolder)
            {
                string coverJpg = Path.Combine(fileDirectory, "cover.jpg");
                string coverPng = Path.Combine(fileDirectory, "cover.png");

                if (File.Exists(coverJpg))
                {
                    return await StorageFile.GetFileFromPathAsync(coverJpg);
                }
                else if (File.Exists(coverPng))
                {
                    return await StorageFile.GetFileFromPathAsync(coverPng);
                }

                var tagLibImage = await TryExtractCoverFromTagLib(filePath);
                if (tagLibImage != null)
                {
                    return tagLibImage;
                }
            }
            else
            {
                foreach (var imgExt in imageExtensions)
                {
                    string imagePath = Path.Combine(fileDirectory + "\\images", fileName + imgExt);
                    if (File.Exists(imagePath))
                    {
                        return await StorageFile.GetFileFromPathAsync(imagePath);
                    }
                }

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
            try
            {
                var tagFile = CoverArt.File.Create(filePath);
                if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                {
                    var pictureData = tagFile.Tag.Pictures[0].Data.Data;
                    string tempPath = Path.Combine(Path.GetTempPath(), "smtc_cover.jpg");
                    await File.WriteAllBytesAsync(tempPath, pictureData);
                    return await StorageFile.GetFileFromPathAsync(tempPath);
                }
            }
            catch (Exception ex)
            {
                Debugger.show($"TagLib extraction failed: {ex.Message}");
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

                var imageFile = await StorageFile.GetFileFromPathAsync(defaultImagePath);
                smtcDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(imageFile);
                smtcDisplayUpdater.Update();
            }
            catch (Exception ex)
            {
                Debugger.show($"Failed to set default image: {ex.Message}");
            }
        }
    }
}
