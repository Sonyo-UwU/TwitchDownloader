using CommandLine;
using TwitchDownloaderCLI.Models;

namespace TwitchDownloaderCLI.Modes.Arguments
{
    [Verb("chatmerge", HelpText = "Merge the chat from two or more VODs or clips.")]
    internal sealed class VideoMergeArgs : IFileCollisionArgs, ITwitchDownloaderArgs
    {
        [Option('i', "input", Required = true, HelpText = "List of paths to input files. Valid extensions are: .json, .json.gz.")]
        public IEnumerable<string> InputFiles { get; set; }

        [Option('o', "output", Required = true, HelpText = "Path to output file. File extension will be used to determine new chat type. Valid extensions are: .json, .html, and .txt.")]
        public string OutputFile { get; set; }

        [Option('d', "parts-delay", HelpText = "Delay between each part. Can be milliseconds (#ms), seconds (#s), minutes (#m), hours (#h), or time (##:##:##).")]
        public TimeDuration DelayBetweenParts { get; set; }

        // Interface args
        public OverwriteBehavior OverwriteBehavior { get; set; }
        public bool? ShowBanner { get; set; }
        public LogLevel LogLevel { get; set; }
    }
}
