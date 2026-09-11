using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Models.Interfaces;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.Tools;
using TwitchDownloaderCore.TwitchObjects.Gql;

namespace TwitchDownloaderCore
{
    public sealed partial class StreamDownloader
    {
        private readonly StreamDownloadOptions _downloadOptions;
        private readonly HttpClient _httpClient;
        private readonly ITaskProgress _progress;
        private readonly string _cacheDir;
        private bool _shouldClearCache = true;

        public StreamDownloader(StreamDownloadOptions downloadOptions, ITaskProgress progress = default)
        {
            _downloadOptions = downloadOptions;
            _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
            _progress = progress;
            _cacheDir = Path.Combine(CacheDirectoryService.GetCacheDirectory(downloadOptions.TempFolder), $"{downloadOptions.ChannelLogin}_{DateTimeOffset.UtcNow.Ticks}");
        }

        /// <param name="stoppingToken">A <see cref="CancellationToken"/> used to stop the stream download, but still finalize downloaded parts to the output file.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the download.</param>
        public async Task DownloadAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
        {
            var outputFileInfo = TwitchHelper.ClaimFile(_downloadOptions.Filename, _downloadOptions.FileCollisionCallback, _progress);
            _downloadOptions.Filename = outputFileInfo.FullName;

            // Open the destination file so that it exists in the filesystem.
            await using var outputFs = outputFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

            // Create and delete cache folder here to avoid surrounding DownloadAsyncImpl with a try/finally
            await TwitchHelper.CleanupAbandonedVideoCaches(_cacheDir, _downloadOptions.CacheCleanerCallback, _progress);
            if (Directory.Exists(_cacheDir))
            {
                _progress.LogWarning("Download cache already exists!");
            }
            else
            {
                TwitchHelper.CreateDirectory(_cacheDir);
            }

            try
            {
                await DownloadAsyncImpl(outputFileInfo, outputFs, stoppingToken, cancellationToken);
            }
            catch
            {
                await Task.Delay(100, CancellationToken.None);

                TwitchHelper.CleanUpClaimedFile(outputFileInfo, outputFs, _progress);

                throw;
            }
            finally
            {
                await Task.Delay(100, CancellationToken.None);

                if (_shouldClearCache)
                {
                    Cleanup(_cacheDir);
                }
            }
        }

        public async Task DownloadAsyncImpl(FileInfo outputFileInfo, FileStream outputFs, CancellationToken stoppingToken, CancellationToken cancellationToken)
        {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, cancellationToken);

            _progress.SetStatus("Fetching Stream Info [1/3]");
            IVideoQuality<StreamQuality> quality;
            try
            {
                quality = await GetQuality(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _progress.LogWarning("No stream parts downloaded");
                    return;
                }
                else
                {
                    throw;
                }
            }
            // TODO: check available space and warn user if it is less than 24h
            // TODO: display how long the stream has been live for
            // TODO: option to download earlier parts from the VOD, either now or at end of stream download


            // Hacky workaroud to display more than 23h
            _progress.SetTemplateStatus("Downloading Stream ({0}h{1:m\\ms\\s} downloaded) [2/3]", 0, TimeSpan.Zero, TimeSpan.Zero);
            var progressTemplateIncludesMissingTime = false;

            var downloadState = new StreamDownloadState();
            var downloadThreads = new StreamDownloadThread[_downloadOptions.DownloadThreads];
            for (var i = 0; i < _downloadOptions.DownloadThreads; i++)
            {
                downloadThreads[i] = new StreamDownloadThread(downloadState, _httpClient, _cacheDir, _downloadOptions.ThrottleKib, _progress, cancellationToken);
            }

            var concatListPath = Path.Combine(_cacheDir, "concat.txt");
            FfmpegConcatList.StreamIds streamIds = null;

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                do
                {
                    var playlist = await GetPlaylistAsync(quality, linkedCts.Token);
                    if (playlist is null)
                        break;

                    if (downloadState.HeaderFile is null && playlist.FileMetadata.Map?.Uri is not null)
                    {
                        downloadState.HeaderFile = await GetHeaderFile(playlist, cancellationToken);
                    }
                    streamIds ??= GetStreamIds(playlist);

                    if (!progressTemplateIncludesMissingTime && downloadState.TotalMissingTime > TimeSpan.Zero)
                    {
                        _progress.SetTemplateStatus(
                            "Downloading Stream ({0}h{1:m\\ms\\s} downloaded, {2:h\\hm\\ms\\s} missing) [2/3]",
                            (int)downloadState.TotalDownloadedTime.TotalHours,
                            downloadState.TotalDownloadedTime,
                            downloadState.TotalMissingTime);
                        progressTemplateIncludesMissingTime = true;
                    }

                    var completedParts = downloadState.AppendSegment(playlist);

                    await using var fs = new FileStream(concatListPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    await FfmpegConcatList.SerializeAsync(fs, completedParts.Select(x => (x.FileName, (decimal)x.Duration.TotalSeconds)), streamIds, cancellationToken);
                } while (await timer.WaitForNextTickAsync(linkedCts.Token));
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _progress.LogInfo("Stopping stream download");
                }
                else
                {
                    throw;
                }
            }

            if (!linkedCts.IsCancellationRequested)
            {
                // TODO: wait a minute or two before finalizing in case the stream crashed. If channel goes back live, resume the download
                _progress.LogInfo("End of live stream");
            }

            cancellationToken.ThrowIfCancellationRequested();
            downloadState.StopDownload();

            // StoppingToken does nothing past this point
            linkedCts.Dispose();

            // Download threads only throw when cancelled, we can just wait they all exit
            await Task.WhenAll(downloadThreads.Select(x => x.ThreadTask));
            cancellationToken.ThrowIfCancellationRequested();

            var lastParts = downloadState.GetLastParts();
            await using var concatFs = new FileStream(concatListPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await FfmpegConcatList.SerializeAsync(concatFs, lastParts.Select(x => (x.FileName, (decimal)x.Duration.TotalSeconds)), streamIds, cancellationToken);

            // TODO: option to download missing parts from VOD


            _progress.SetTemplateStatus("Finalizing Video {0}% [3/3]", 0);
            // TODO: try to get vod info if it exists (or fallback to channel info) to serialize metadata

            outputFs.Close();

            int ffmpegExitCode;
            var ffmpegRetries = 0;
            do
            {
                // For some reason using the full concatListPath makes ffmpeg not use _cacheDir as working directory
                ffmpegExitCode = await RunFfmpegVideoCopy(outputFileInfo, "concat.txt", downloadState.TotalDownloadedTime + downloadState.TotalMissingTime, ffmpegRetries > 0, cancellationToken);
                if (ffmpegExitCode != 0)
                {
                    _progress.LogError($"Failed to finalize video (code {ffmpegExitCode}), retrying in 5 seconds...");
                    await Task.Delay(5_000, cancellationToken);
                }
            } while (ffmpegExitCode != 0 && ffmpegRetries++ < 1);

            outputFileInfo.Refresh();
            if (ffmpegExitCode != 0 || !outputFileInfo.Exists || outputFileInfo.Length == 0)
            {
                _shouldClearCache = false;
                throw new Exception($"Failed to finalize video. The download cache has not been cleared and can be found at {_cacheDir} along with a log file.");
            }

            _progress.ReportProgress(100);
        }

        private async Task<IVideoQuality<StreamQuality>> GetQuality(CancellationToken cancellationToken)
        {
            GqlStreamTokenResponse accessToken = await TwitchHelper.GetStreamToken(_downloadOptions.ChannelLogin, _downloadOptions.Oauth, cancellationToken);
            // TODO: get token expiration date

            if (accessToken.data.streamPlaybackAccessToken is null)
            {
                throw new NullReferenceException("Invalid stream");
            }

            var playlistString = await TwitchHelper.GetStreamPlaylist(
                _downloadOptions.ChannelLogin,
                accessToken.data.streamPlaybackAccessToken.value,
                accessToken.data.streamPlaybackAccessToken.signature,
                cancellationToken);
            if (playlistString.Contains("Can not find channel"))
            {
                throw new Exception("Channel does not exist or is not live");
            }

            var m3u8 = M3U8.Parse(playlistString);
            var (availableQualities, unavailableQualities) = VideoQualities.FromStreamM3U8(m3u8);
            var allQualities = new StreamVideoQualities([.. availableQualities.Qualities, .. unavailableQualities.Qualities]);

            var quality = allQualities.GetQuality(_downloadOptions.Quality);
            if (quality.Path is null)
            {
                var fallback = availableQualities.GetQuality(_downloadOptions.Quality);
                _progress.LogWarning($"Quality {quality.Name} is unavailable for reasons: {quality.Item.Video}. Switching to {fallback.Name}");
                return fallback;
            }

            return quality;
        }

        private async Task<M3U8> GetPlaylistAsync(IVideoQuality<StreamQuality> quality, CancellationToken cancellationToken)
        {
            string playlistString;
            try
            {
                playlistString = await _httpClient.GetStringAsync(quality.Path, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Stream went offline
                    return null;
                }
                else
                {
                    throw;
                }
            }
            var playlist = M3U8.Parse(playlistString);
            return playlist;
        }

        private async Task<string> GetHeaderFile(M3U8 playlist, CancellationToken cancellationToken)
        {
            var map = playlist.FileMetadata.Map;
            if (string.IsNullOrWhiteSpace(map?.Uri))
            {
                return null;
            }

            if (map.ByteRange != default)
            {
                _progress.LogWarning($"Byte range was {map.ByteRange}, but is not yet implemented!");
            }

            var destinationFile = Path.Combine(_cacheDir, "header" + DownloadTools.GetStreamPartFileExtension(map.Uri));

            var uri = new Uri(map.Uri);
            _progress.LogVerbose($"Downloading header file from '{uri}' to '{destinationFile}'");

            await DownloadTools.DownloadFileAsync(_httpClient, uri, destinationFile, null, _downloadOptions.ThrottleKib, _progress, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

            return destinationFile;
        }

        private FfmpegConcatList.StreamIds GetStreamIds(M3U8 playlist)
        {
            var path = playlist.Streams.FirstOrDefault()?.Path ?? "";
            var extension = DownloadTools.GetStreamPartFileExtension(path);
            switch (extension)
            {
                case ".mp4":
                    return FfmpegConcatList.StreamIds.Mp4;
                case ".ts":
                    return FfmpegConcatList.StreamIds.TransportStream;
                default:
                    _progress.LogWarning("No file extension was found! Assuming TS.");
                    return FfmpegConcatList.StreamIds.TransportStream;
            }
        }

        private async Task<int> RunFfmpegVideoCopy(FileInfo outputFile, string concatListPath, TimeSpan videoLength, bool disableAudioCopy, CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = _downloadOptions.FfmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = _cacheDir
                }
            };

            var args = new List<string>
            {
                "-stats",
                "-y",
                "-avoid_negative_ts", "make_zero",
                "-analyzeduration", $"{int.MaxValue}",
                "-probesize", $"{int.MaxValue}",
                "-f", "concat",
                "-max_streams", $"{int.MaxValue}",
                "-i", concatListPath,
                disableAudioCopy ? "-c:v" : "-c", "copy",
                outputFile.FullName
            };

            if (disableAudioCopy)
            {
                // Some VODs have bad audio data which FFmpeg doesn't like in copy mode. See lay295#1121 for more info
                // No idea if this is necessary with live streams
                _progress.LogVerbose("Running with audio copy disabled.");
            }

            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            var logQueue = new ConcurrentQueue<string>();

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data is null)
                    return;

                logQueue.Enqueue(e.Data); // We cannot use -report ffmpeg arg because it redirects stderr

                HandleFfmpegOutput(e.Data, videoLength);
            };
            cancellationToken.Register(process.Kill);
            cancellationToken.ThrowIfCancellationRequested();

            _progress.LogVerbose($"Running \"{_downloadOptions.FfmpegPath}\" in \"{process.StartInfo.WorkingDirectory}\" with args: {CombineArguments(process.StartInfo.ArgumentList)}");

            process.Start();
            process.BeginErrorReadLine();

            await using var logWriter = File.AppendText(Path.Combine(_cacheDir, "ffmpegLog.txt"));
            logWriter.AutoFlush = true;
            do // We cannot handle logging inside the ErrorDataReceived lambda because more than 1 can come in at once and cause a race condition. lay295#598
            {
                await Task.Delay(200, cancellationToken);
                while (!logQueue.IsEmpty && logQueue.TryDequeue(out var logMessage))
                {
                    await logWriter.WriteLineAsync(logMessage);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            } while (!process.HasExited || !logQueue.IsEmpty);

            return process.ExitCode;

            static string CombineArguments(IEnumerable<string> args)
            {
                return string.Join(' ', args.Select(x =>
                {
                    if (!x.StartsWith('"') && !x.StartsWith('\'') && x.Contains(' '))
                        return $"\"{x}\"";

                    return x;
                }));
            }
        }

        [GeneratedRegex(@"(?<=time=)(\d\d):(\d\d):(\d\d)\.(\d\d)")]
        private static partial Regex EncodingTimeRegex { get; }

        private void HandleFfmpegOutput(string output, TimeSpan videoLength)
        {
            var encodingTimeMatch = EncodingTimeRegex.Match(output);
            if (!encodingTimeMatch.Success)
                return;

            // TimeSpan.Parse insists that hours cannot be greater than 24, thus we must use the TimeSpan ctor.
            if (!int.TryParse(encodingTimeMatch.Groups[1].ValueSpan, out var hours))
                return;
            if (!int.TryParse(encodingTimeMatch.Groups[2].ValueSpan, out var minutes))
                return;
            if (!int.TryParse(encodingTimeMatch.Groups[3].ValueSpan, out var seconds))
                return;
            if (!int.TryParse(encodingTimeMatch.Groups[4].ValueSpan, out var milliseconds))
                return;
            var encodingTime = new TimeSpan(0, hours, minutes, seconds, milliseconds);

            var percent = (int)Math.Round(encodingTime / videoLength * 100);

            _progress.ReportProgress(Math.Clamp(percent, 0, 100));
        }

        private void Cleanup(string downloadFolder)
        {
            try
            {
                if (Directory.Exists(downloadFolder))
                {
                    Directory.Delete(downloadFolder, true);
                }
            }
            catch (IOException e)
            {
                _progress.LogWarning($"Failed to delete download cache: {e.Message}");
            }
        }
    }
}
