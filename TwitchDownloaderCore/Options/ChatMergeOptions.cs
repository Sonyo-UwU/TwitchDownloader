using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCore.Options
{
    public class ChatMergeOptions
    {
        public string[] InputFiles {  get; set; }
        public string OutputFile { get; set; }
        public ChatCompression Compression { get; set; } = ChatCompression.None;
        public ChatFormat OutputFormat { get; set; } = ChatFormat.Json;
        public TimestampFormat TextTimestampFormat { get; set; }
        public double DelayBetweenParts { get; set; }
        public string FileExtension
        {
            get
            {
                return string.Concat(
                    OutputFormat switch
                    {
                        ChatFormat.Json => ".json",
                        ChatFormat.Html => ".html",
                        ChatFormat.Text => ".txt",
                        _ => ""
                    },
                    Compression switch
                    {
                        ChatCompression.None => "",
                        ChatCompression.Gzip => ".gz",
                        _ => ""
                    }
                );
            }
        }
        public string TempFolder { get; set; }
        public Func<FileInfo, FileInfo> FileCollisionCallback { get; set; } = info => info;
    }
}
