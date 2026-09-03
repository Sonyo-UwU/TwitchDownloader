namespace TwitchDownloaderCore.Options
{
    public class LiveDownloadOptions
    {
        public string ChannelLogin { get; set; }
        public string Quality { get; set; }
        public string Filename { get; set; }
        public string Oauth { get; set; }
        public string FfmpegPath { get; set; }
        public string TempFolder { get; set; }
        public Func<FileInfo, FileInfo> FileCollisionCallback { get; set; } = info => info;
    }
}
