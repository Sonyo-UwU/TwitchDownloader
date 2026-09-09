using System.Collections.Concurrent;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCore.Tools
{
    internal sealed class StreamDownloadState
    {
        public class PartState
        {
            public byte DownloadAttempts;
            public DateTimeOffset ProgramDateTime;
            public string FileName { get; set; }
        }

        public bool DownloadInProgress { get; private set; }

        public ConcurrentQueue<string> PartQueue { get; }

        public Dictionary<string, PartState> PartStates { get; }

        public string HeaderFile { get; set; }

        public int PartCount => PartStates.Count;

        public StreamDownloadState()
        {
            PartQueue = new();
            PartStates = [];
            DownloadInProgress = true;
            HeaderFile = null;
        }

        public void AppendSegment(IEnumerable<M3U8.Stream> playlist)
        {
            foreach (var stream in playlist)
            {
                PartQueue.Enqueue(stream.Path);
                PartStates[stream.Path] = new PartState()
                {
                    ProgramDateTime = stream.ProgramDateTime,
                    FileName = stream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff") + DownloadTools.GetStreamPartFileExtension(stream.Path)
                };
            }
        }

        public void StopDownload()
        {
            DownloadInProgress = false;
        }
    }
}