using CommandLine;
using TwitchDownloaderCLI.Models;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCLI.Modes.Arguments
{
    [Verb("chatmerge", HelpText = "Merge the chat from two or more VODs or clips.")]
    internal sealed class ChatMergeArgs : IFileCollisionArgs, ITwitchDownloaderArgs
    {
        [Option('i', "input", Required = true, HelpText = "List of paths to input files. Valid extensions are: .json, .json.gz.")]
        public IEnumerable<string> InputFiles { get; set; }

        [Option('o', "output", Required = true, HelpText = "Path to output file. File extension will be used to determine new chat type. Valid extensions are: .json, .html, and .txt.")]
        public string OutputFile { get; set; }

        [Option("compression", Default = ChatCompression.None, HelpText = "Compresses an output json chat file using a specified compression, usually resulting in 40-90% size reductions. Valid values are: None, Gzip.")]
        public ChatCompression Compression { get; set; }

        [Option("timestamp-format", Default = TimestampFormat.Relative, HelpText = "Sets the timestamp format for .txt chat logs. Valid values are: Utc, Relative, and None.")]
        public TimestampFormat TimeFormat { get; set; }

        [Option('d', "parts-delay", HelpText = "Delay between each part. Can be milliseconds (#ms), seconds (#s), minutes (#m), hours (#h), or time (##:##:##).")]
        public TimeDuration DelayBetweenParts { get; set; }

        [Option("temp-path", Default = "", HelpText = "Path to temporary folder to use for cache.")]
        public string TempFolder { get; set; }

        // Interface args
        public OverwriteBehavior OverwriteBehavior { get; set; }
        public bool? ShowBanner { get; set; }
        public LogLevel LogLevel { get; set; }
    }
}
