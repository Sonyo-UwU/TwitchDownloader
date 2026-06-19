using TwitchDownloaderCLI.Modes.Arguments;
using TwitchDownloaderCLI.Tools;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderCLI.Modes
{
    internal static class MergeChats
    {
        internal static void Merge(ChatMergeArgs inputOptions)
        {
            using var progress = new CliTaskProgress(inputOptions.LogLevel);

            var collisionHandler = new FileCollisionHandler(inputOptions, progress);
            var mergeOptions = GetMergeOptions(inputOptions, collisionHandler, progress);

            var chatMerger = new ChatMerger(mergeOptions, progress);
            chatMerger.ParseJsonAsync().Wait();
            chatMerger.MergeAsync(new CancellationToken()).Wait();
        }

        private static ChatMergeOptions GetMergeOptions(ChatMergeArgs inputOptions, FileCollisionHandler collisionHandler, ITaskLogger logger)
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

                var fileExtension = Path.GetExtension(input)!.ToLower();
                switch (fileExtension)
                {
                    case ".html" or ".htm":
                        logger.LogError("Input file must be .json or .json.gz!");
                        Environment.Exit(1);
                        break;
                    case ".json" or ".gz":
                        break;
                    case ".txt" or ".text":
                        logger.LogError("Input file must be .json or .json.gz!");
                        Environment.Exit(1);
                        break;
                    default:
                        logger.LogError($"{fileExtension} is not a valid chat file extension.");
                        Environment.Exit(1);
                        break;
                }

                if (Path.GetFullPath(input) == Path.GetFullPath(inputOptions.OutputFile))
                {
                    logger.LogWarning("Output file path is identical to an input file. This is not recommended in case something goes wrong. All data will be permanently overwritten!");
                }
            }

            var outFileExtension = Path.GetExtension(inputOptions.OutputFile)!.ToLower();
            var outFormat = outFileExtension switch
            {
                ".html" or ".htm" => ChatFormat.Html,
                ".json" => ChatFormat.Json,
                ".txt" or ".text" or "" => ChatFormat.Text,
                _ => throw new NotSupportedException($"{outFileExtension} is not a valid chat file extension.")
            };


            ChatMergeOptions mergeOptions = new()
            {
                InputFiles = [.. inputOptions.InputFiles],
                OutputFile = inputOptions.Compression is ChatCompression.Gzip
                    ? inputOptions.OutputFile + ".gz"
                    : inputOptions.OutputFile,
                Compression = inputOptions.Compression,
                OutputFormat = outFormat,
                TextTimestampFormat = inputOptions.TimeFormat,
                DelayBetweenParts = ((TimeSpan)inputOptions.DelayBetweenParts).TotalSeconds,
                TempFolder = inputOptions.TempFolder,
                FileCollisionCallback = collisionHandler.HandleCollisionCallback,
            };

            return mergeOptions;
        }
    }
}
