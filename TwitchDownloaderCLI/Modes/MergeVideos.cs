using TwitchDownloaderCLI.Modes.Arguments;
using TwitchDownloaderCLI.Tools;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderCLI.Modes
{
    internal static class MergeVideos
    {
        internal static void Merge(VideoMergeArgs inputOptions)
        {
            using var progress = new CliTaskProgress(inputOptions.LogLevel);

            var collisionHandler = new FileCollisionHandler(inputOptions, progress);
            var mergeOptions = GetMergeOptions(inputOptions, collisionHandler, progress);

            var videoMerger = new VideoMerger(mergeOptions, progress);
            videoMerger.MergeAsync(new CancellationToken()).Wait();
        }

        private static VideoMergeOptions GetMergeOptions(VideoMergeArgs inputOptions, FileCollisionHandler collisionHandler, ITaskLogger logger)
        {
            if (inputOptions.InputFiles.Count() <= 1)
            {
                logger.LogError("Must specify at least two input files!");
                Environment.Exit(1);
            }

            foreach (var input in inputOptions.InputFiles)
            {
                if (!File.Exists(input))
                {
                    logger.LogError($"Input file {input} does not exist!");
                    Environment.Exit(1);
                }

                if (Path.GetFullPath(input) == Path.GetFullPath(inputOptions.OutputFile))
                {
                    logger.LogWarning("Output file path is identical to an input file. This is not recommended in case something goes wrong. All data will be permanently overwritten!");
                }
            }


            VideoMergeOptions mergeOptions = new()
            {
                InputFiles = [.. inputOptions.InputFiles],
                OutputFile = inputOptions.OutputFile,
                DelayBetweenParts = ((TimeSpan)inputOptions.DelayBetweenParts).TotalSeconds,
                FileCollisionCallback = collisionHandler.HandleCollisionCallback,
            };

            return mergeOptions;
        }
    }
}
