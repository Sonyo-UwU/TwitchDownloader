using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Models.Interfaces;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.TwitchObjects.Gql;

namespace TwitchDownloaderCore
{
    public sealed partial class LiveDownloader
    {
        private readonly string _cacheDir;
        private readonly LiveDownloadOptions _downloadOptions;
        private readonly ITaskProgress _progress;

        public LiveDownloader(LiveDownloadOptions downloadOptions, ITaskProgress progress = default)
        {
            _downloadOptions = downloadOptions;
            _progress = progress;
            _cacheDir = Path.Combine(CacheDirectoryService.GetCacheDirectory(downloadOptions.TempFolder), $"{downloadOptions.ChannelLogin}_{DateTimeOffset.UtcNow.Ticks}");
            Directory.CreateDirectory(_cacheDir);
        }

        public async Task DownloadAsync(CancellationToken cancellationToken)
        {
            //var outputFileInfo = TwitchHelper.ClaimFile(downloadOptions.Filename, downloadOptions.FileCollisionCallback, progress);
            //downloadOptions.Filename = outputFileInfo.FullName;

            // Open the destination file so that it exists in the filesystem.
            //await using var outputFs = outputFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

            try
            {
                await DownloadAsyncImpl(null, null, cancellationToken);
            }
            catch
            {
                await Task.Delay(100, CancellationToken.None);

                //TwitchHelper.CleanUpClaimedFile(outputFileInfo, outputFs, progress);

                throw;
            }
        }

        public async Task DownloadAsyncImpl(FileInfo outputFileInfo, FileStream outputFs, CancellationToken cancellationToken)
        {
            _progress.SetStatus("Fetching Stream Info [1/2]");
            IVideoQuality<StreamQuality> quality = await GetQuality();


            _progress.SetStatus("Downloading Stream [1/2]");
            await RunFfmpegDownload(quality, cancellationToken);
        }

        private async Task<IVideoQuality<StreamQuality>> GetQuality()
        {
            GqlStreamTokenResponse accessToken = await TwitchHelper.GetStreamToken(_downloadOptions.ChannelLogin, _downloadOptions.Oauth);

            if (accessToken.data.streamPlaybackAccessToken is null)
            {
                throw new NullReferenceException("Invalid stream");
            }

            var playlistString = await TwitchHelper.GetStreamPlaylist(_downloadOptions.ChannelLogin, accessToken.data.streamPlaybackAccessToken.value, accessToken.data.streamPlaybackAccessToken.signature);
            if (playlistString.Contains("Can not find channel"))
            {
                throw new Exception("Channel does not exist or is not live");
            }

            var m3u8 = M3U8.Parse(playlistString);
            var qualities = VideoQualities.FromStreamM3U8(m3u8);
            return qualities.GetQuality(_downloadOptions.Quality);
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
