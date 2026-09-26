using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Tools;

namespace TwitchDownloaderCore
{
    public sealed partial class VideoMerger(VideoMergeOptions mergeOptions, ITaskProgress progress)
    {
        [GeneratedRegex(@"(?<=time=)(\d\d):(\d\d):(\d\d)\.(\d\d)")]
        private static partial Regex EncodingTimeRegex { get; }
        private readonly VideoMergeOptions mergeOptions = mergeOptions;
        private readonly ITaskProgress _progress = progress;

        public async Task MergeAsync(CancellationToken cancellationToken)
        {
            var outputFileInfo = TwitchHelper.ClaimFile(mergeOptions.OutputFile, mergeOptions.FileCollisionCallback, _progress);
            mergeOptions.OutputFile = outputFileInfo.FullName;

            // Open the destination file so that it exists in the filesystem.
            await using var outputFs = outputFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

            try
            {
                await MergeAsyncImpl(outputFileInfo, outputFs, cancellationToken);
            }
            catch
            {
                await Task.Delay(100, CancellationToken.None);

                TwitchHelper.CleanUpClaimedFile(outputFileInfo, outputFs, _progress);

                throw;
            }
        }

        private async Task MergeAsyncImpl(FileInfo outputFileInfo, FileStream outputFs, CancellationToken cancellationToken)
        {
            _progress.SetTemplateStatus("Combining... {0}%", 0);

            TimeSpan totalVideoLength = TimeSpan.Zero;
            int maxWidth = 0;
            int maxHeight = 0;
            float maxFrameRate = 0;

            foreach (var input in mergeOptions.InputFiles)
            {
                var (duration, width, height, frameRate) = await GetVideoInfo(input, cancellationToken);
                if (duration <= TimeSpan.Zero)
                {
                    throw new Exception($"Couldn't read input \"{input}\".");
                }
                totalVideoLength += duration;
                maxWidth = Math.Max(maxWidth, width);
                maxHeight = Math.Max(maxHeight, height);
                maxFrameRate = Math.Max(maxFrameRate, frameRate);
            }

            var ffmpegLogFile = Path.Combine(Path.GetDirectoryName(mergeOptions.OutputFile), "ffmpegLog.txt");
            for (int i = 0; File.Exists(ffmpegLogFile); i++)
            {
                ffmpegLogFile = Path.Combine(Path.GetDirectoryName(mergeOptions.OutputFile), $"ffmpegLog{i}.txt");
            }

            outputFs.Close();
            var ffmpegExitCode = await RunFfmpeg(outputFileInfo, ffmpegLogFile, totalVideoLength, maxWidth, maxHeight, maxFrameRate, cancellationToken);

            outputFileInfo.Refresh();
            if ((ffmpegExitCode != 0 || !outputFileInfo.Exists || outputFileInfo.Length == 0) && !cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"Failed to merge videos. A log file can be found at {ffmpegLogFile}.");
            }

            File.Delete(ffmpegLogFile);
            cancellationToken.ThrowIfCancellationRequested();

            _progress.ReportProgress(100);
        }

        private async Task<(TimeSpan duration, int width, int height, float frameRate)> GetVideoInfo(string input, CancellationToken cancellationToken)
        {
            TimeSpan duration = TimeSpan.Zero;
            int maxWidth = 0;
            int maxHeight = 0;
            float maxFps = 0;

            await FfprobeRunner.Run(mergeOptions.FfprobePath, input, "format=duration:stream=width,height,r_frame_rate", (sender, e) =>
            {
                if (e.Data is null)
                    return;

                if (e.Data.StartsWith("duration="))
                {
                    _ = TimeSpan.TryParse(e.Data.AsSpan("duration=".Length), out duration);
                }
                else if (e.Data.StartsWith("width="))
                {
                    if (int.TryParse(e.Data.AsSpan("width=".Length), out int width))
                    {
                        maxWidth = Math.Max(maxWidth, width);
                    }
                }
                else if (e.Data.StartsWith("height="))
                {
                    if (int.TryParse(e.Data.AsSpan("height=".Length), out int height))
                    {
                        maxHeight = Math.Max(maxHeight, height);
                    }
                }
                else if (e.Data.StartsWith("r_frame_rate="))
                {
                    var parts = e.Data["r_frame_rate=".Length..].Split('/');

                    if (int.TryParse(parts[0], out int num) &&
                        int.TryParse(parts[1], out int denom))
                    {
                        if (denom != 0)
                        {
                            maxFps = Math.Max(maxFps, (float)num / denom);
                        }
                    }
                }
            }, cancellationToken);

            return (duration, maxWidth, maxHeight, maxFps);
        }

        private async Task<int> RunFfmpeg(FileInfo outputFileInfo, string logFile, TimeSpan totalVideoLength, int width, int height, float fps, CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = mergeOptions.FfmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var n = mergeOptions.InputFiles.Length;
            var d = mergeOptions.DelayBetweenParts.ToString(CultureInfo.InvariantCulture);
            var args = new List<string>();
            args.AddRange(
                mergeOptions.InputFiles.SelectMany<string, string>(f => ["-i", f])
            );
            args.AddRange(
                "-filter_complex",
                string.Join(';', mergeOptions.InputFiles.Select((f, i) => $"[{i}:v]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,fps={fps},setsar=1[v{i}];[{i}:a]aresample=48000[a{i}]")) +
                $";color=c=black:s={width}x{height}:r={fps}:d='{d}'{(n > 2 ? $",split={n - 1}" : "")}{string.Join("", Enumerable.Range(0, n - 1).Select(i => $"[vb{i}]"))};" +
                $"anullsrc=r=48000:cl=stereo:d='{d}'{(n > 2 ? $",asplit={n - 1}" : "")}{string.Join("", Enumerable.Range(0, n - 1).Select(i => $"[ab{i}]"))};" +
                string.Join("", mergeOptions.InputFiles.Select((f, i) => $"[v{i}][a{i}]" + (i < n - 1 ? $"[vb{i}][ab{i}]" : ""))) +
                $"concat=n={n + n - 1}:v=1:a=1[v][a]",
                "-map", "[v]",
                "-map", "[a]",
                "-c:v", "libx264",
                "-c:a", "aac"
            );
            args.AddRange(
                "-stats",
                "-y",
                outputFileInfo.FullName
            );

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

                HandleFfmpegOutput(e.Data, totalVideoLength);
            };

            _progress.LogVerbose($"Running \"{mergeOptions.FfmpegPath}\" with args: {CombineArguments(process.StartInfo.ArgumentList)}");

            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.Register(process.Kill);

            process.Start();
            process.BeginErrorReadLine();


            await using var logWriter = File.CreateText(logFile);
            logWriter.AutoFlush = true;
            do // We cannot handle logging inside the ErrorDataReceived lambda because more than 1 can come in at once and cause a race condition. lay295#598
            {
                // Intentionally don't throw until the log file gets deleted
                await Task.Delay(200, CancellationToken.None);
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
    }
}
