using CommandLine;
using TwitchDownloaderCLI.Models;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCLI.Modes.Arguments
{
    [Verb("streamdownload", HelpText = "Downloads a stream live from Twitch")]
    internal sealed class StreamDownloadArgs : IFileCollisionArgs, ITwitchDownloaderArgs
    {
        [Option('u', "login", Required = true, HelpText = "The login of the target channel.")]
        public string ChannelLogin { get; set; }

        [Option('o', "output", Required = true, HelpText = "Path to output file. File extension will be used to determine download type. Valid extensions are: .mp4 and .m4a.")]
        public string OutputFile { get; set; }

        [Option('q', "quality", HelpText = "The quality the program will attempt to download.")]
        public string Quality { get; set; }

        [Option('t', "threads", Default = 4, HelpText = "Number of parallel download threads. Large values may result in IP rate limiting.")]
        public int DownloadThreads { get; set; }

        [Option("bandwidth", Default = -1, HelpText = "The maximum bandwidth a thread will be allowed to use in kibibytes per second (KiB/s), or -1 for no maximum.")]
        public int ThrottleKib { get; set; }

        [Option("oauth", HelpText = "OAuth access token to download subscriber only streams or access logged-in only qualities. DO NOT SHARE THIS WITH ANYONE.")]
        public string Oauth { get; set; }

        [Option("ffmpeg-path", HelpText = "Path to FFmpeg executable.")]
        public string FfmpegPath { get; set; }

        [Option("temp-path", Default = "", HelpText = "Path to temporary caching folder.")]
        public string TempFolder { get; set; }

        // Interface args
        public OverwriteBehavior OverwriteBehavior { get; set; }
        public bool? ShowBanner { get; set; }
        public LogLevel LogLevel { get; set; }
    }
}
