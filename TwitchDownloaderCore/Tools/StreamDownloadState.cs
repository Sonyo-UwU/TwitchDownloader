using System.Collections.Concurrent;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCore.Tools
{
    public class StreamDownloadState
    {
        public class PartState
        {
            public byte DownloadAttempts;
            public DateTimeOffset ProgramDateTime;
        }

        public bool DownloadInProgress { get; private set; }

        public ConcurrentQueue<string> PartQueue { get; }

        public Dictionary<string, PartState> PartStates { get; }

        public int PartCount => PartStates.Count;

        public StreamDownloadState()
        {
            PartQueue = new();
            PartStates = [];
            DownloadInProgress = true;
        }

        public void AppendSegment(IReadOnlyCollection<M3U8.Stream> playlist)
        {
            foreach (var stream in playlist)
            {
                PartQueue.Enqueue(stream.Path);
                PartStates[stream.Path] = new PartState() { ProgramDateTime = stream.ProgramDateTime };
            }
        }

        public void StopDownload()
        {
            DownloadInProgress = false;
        }
    }
}