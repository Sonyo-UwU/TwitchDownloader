using TwitchDownloaderCLI.Modes.Arguments;
using TwitchDownloaderCLI.Tools;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.Tools;

namespace TwitchDownloaderCLI.Modes
{
    internal static class DownloadStream
    {
        internal static void Download(StreamDownloadArgs inputOptions)
        {
            using var progress = new CliTaskProgress(inputOptions.LogLevel);

            FfmpegHandler.DetectFfmpeg(inputOptions.FfmpegPath, progress);

            var collisionHandler = new FileCollisionHandler(inputOptions, progress);
            var downloadOptions = GetDownloadOptions(inputOptions, collisionHandler, progress);

            var streamDownloader = new StreamDownloader(downloadOptions, progress);
            streamDownloader.DownloadAsync(new CancellationToken()).Wait();
        }

        private static StreamDownloadOptions GetDownloadOptions(StreamDownloadArgs inputOptions, FileCollisionHandler collisionHandler, ITaskLogger logger)
        {
            if (inputOptions.ChannelLogin is null)
            {
                logger.LogError("Channel login cannot be null!");
                Environment.Exit(1);
            }

            if (!Path.HasExtension(inputOptions.OutputFile) && inputOptions.Quality is { Length: > 0 })
            {
                inputOptions.OutputFile += FilenameService.GuessVodFileExtension(inputOptions.Quality);
            }

            StreamDownloadOptions downloadOptions = new()
            {
                DownloadThreads = inputOptions.DownloadThreads,
                ThrottleKib = inputOptions.ThrottleKib,
                ChannelLogin = inputOptions.ChannelLogin,
                Oauth = inputOptions.Oauth,
                Filename = inputOptions.OutputFile,
                Quality = Path.GetExtension(inputOptions.OutputFile)!.ToLower() switch
                {
                    ".mp4" => inputOptions.Quality,
                    ".m4a" => "Audio",
                    _ => throw new ArgumentException("Only MP4 and M4A audio files are supported.")
                },
                FfmpegPath = string.IsNullOrWhiteSpace(inputOptions.FfmpegPath) ? FfmpegHandler.FfmpegExecutableName : Path.GetFullPath(inputOptions.FfmpegPath),
                TempFolder = inputOptions.TempFolder,
                CacheCleanerCallback = directoryInfos =>
                {
                    logger.LogInfo(
                        $"{directoryInfos.Length} unmanaged video caches were found at '{directoryInfos.FirstOrDefault()?.Parent?.FullName ?? inputOptions.TempFolder}' and can be safely deleted. " +
                        "Run 'TwitchDownloaderCLI cache help' for more information.");

                    return Array.Empty<DirectoryInfo>();
                },
                FileCollisionCallback = collisionHandler.HandleCollisionCallback,
            };

            return downloadOptions;
        }
    }
}