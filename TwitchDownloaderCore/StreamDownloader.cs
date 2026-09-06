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
            //var outputFileInfo = TwitchHelper.ClaimFile(downloadOptions.Filename, downloadOptions.FileCollisionCallback, progress);
            //downloadOptions.Filename = outputFileInfo.FullName;

            // Open the destination file so that it exists in the filesystem.
            //await using var outputFs = outputFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

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
                await DownloadAsyncImpl(null, null, stoppingToken, cancellationToken);
            }
            catch
            {
                await Task.Delay(100, CancellationToken.None);

                //TwitchHelper.CleanUpClaimedFile(outputFileInfo, outputFs, progress);

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

            _progress.SetStatus("Fetching Stream Info [1/4]");
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
            //TODO: check available space and warn user if it is less than 24h
            //TODO: display how long the stream has been live for


            _progress.SetStatus("Downloading Stream [2/4]");

            var downloadState = new StreamDownloadState();
            var downloadThreads = new StreamDownloadThread[_downloadOptions.DownloadThreads];
            for (var i = 0; i < _downloadOptions.DownloadThreads; i++)
            {
                downloadThreads[i] = new StreamDownloadThread(downloadState, _httpClient, _cacheDir, _downloadOptions.ThrottleKib, _progress, cancellationToken);
            }

            try
            {
                DateTimeOffset nextProgrameDateTimeNeeded = DateTimeOffset.MaxValue;
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                do
                {
                    //TODO: display better info (seconds downloaded, parts in queue still waiting to be downloaded by download threads...)
                    _progress.SetStatus(DateTime.Now.ToString());

                    var playlist = await GetPlaylistAsync(quality, linkedCts.Token);

                    var firstStream = playlist.Streams[0];
                    if (nextProgrameDateTimeNeeded - firstStream.ProgramDateTime < TimeSpan.Zero)
                    {
                        _progress.LogWarning($"Missing stream part from {nextProgrameDateTimeNeeded} to {firstStream.ProgramDateTime}");
                    }

                    downloadState.AppendSegment(playlist.Streams);

                    var lastStream = playlist.Streams[^1];
                    nextProgrameDateTimeNeeded = lastStream.ProgramDateTime + TimeSpan.FromSeconds((double)lastStream.PartInfo.Duration);
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

            // StoppingToken does nothing past this point
            cancellationToken.ThrowIfCancellationRequested();
            linkedCts.Dispose();

            downloadState.StopDownload();


            _progress.SetTemplateStatus("Verifying Parts {0}% [3/4]", 0);
        }

        private async Task<IVideoQuality<StreamQuality>> GetQuality(CancellationToken cancellationToken)
        {
            GqlStreamTokenResponse accessToken = await TwitchHelper.GetStreamToken(_downloadOptions.ChannelLogin, _downloadOptions.Oauth, cancellationToken);
            //TODO: get token expiration date

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
            //TODO: catch when stream goes offline
            string playlistString = await _httpClient.GetStringAsync(quality.Path, cancellationToken);
            var playlist = M3U8.Parse(playlistString);
            return playlist;
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

        private async Task<int> RunFfmpegDownload(IVideoQuality<StreamQuality> quality, CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = _downloadOptions.FfmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var args = new List<string>
            {
                "-stats",
                "-y",
                "-i", quality.Path,
                "-c", "copy",
                //"out.mp4"
                _downloadOptions.Filename
            };

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

                HandleFfmpegOutput(e.Data);
            };

            _progress.LogVerbose($"Running \"{_downloadOptions.FfmpegPath}\" in \"{process.StartInfo.WorkingDirectory}\" with args: {CombineArguments(process.StartInfo.ArgumentList)}");

            process.Start();
            process.BeginErrorReadLine();

            cancellationToken.Register(() =>
            {
                _progress.SetStatus("Stopping download...");
                process.StandardInput.Write('q');
            });

            await using var logWriter = File.CreateText(Path.Combine(_cacheDir, "ffmpegLog.txt"));
            logWriter.AutoFlush = true;
            do // We cannot handle logging inside the ErrorDataReceived lambda because more than 1 can come in at once and cause a race condition. lay295#598
            {
                try
                {
                    await Task.Delay(200, cancellationToken);
                }
                catch { }

                while (!logQueue.IsEmpty && logQueue.TryDequeue(out var logMessage))
                {
                    await logWriter.WriteLineAsync(logMessage);
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
        private void HandleFfmpegOutput(string output)
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

            _progress.SetStatus(encodingTime.ToString());
        }
    }
}
