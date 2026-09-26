namespace TwitchDownloaderCore.Options
{
    public class VideoMergeOptions
    {
        public string[] InputFiles {  get; set; }
        public string OutputFile { get; set; }
        public double DelayBetweenParts { get; set; }
        public string FfmpegPath { get; set; }
        public string FfprobePath { get; set; }
        public Func<FileInfo, FileInfo> FileCollisionCallback { get; set; } = info => info;
    }
}
