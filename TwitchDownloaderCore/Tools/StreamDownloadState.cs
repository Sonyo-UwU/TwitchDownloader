using System.Collections.Concurrent;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCore.Tools
{
    internal sealed class StreamDownloadState
    {
        public class PartState
        {
            public byte DownloadAttempts { get; set; } = 0;
            public bool IsDownloaded { get; set; } = false;
            public required string Path { get; init; }
            public required DateTimeOffset ProgramDateTime { get; init; }
            public required TimeSpan Duration { get; set; }
            public required string FileName { get; init; }

            public override string ToString() => FileName;
        }

        public bool DownloadInProgress { get; private set; } = true;

        public ConcurrentQueue<PartState> PartQueue { get; } = new();

        public ConcurrentDictionary<DateTimeOffset, PartState> ProcessedParts { get; } = [];

        public string HeaderFile { get; set; } = null;

        public TimeSpan TotalDownloadedTime { get; set; } = TimeSpan.Zero;

        public TimeSpan TotalMissingTime { get; set; } = TimeSpan.Zero;

        private PartState _lastPartProcessed;

        public IEnumerable<PartState> AppendSegment(M3U8 playlist)
        {
            uint? startId = playlist.FileMetadata.MediaSequence ?? playlist.FileMetadata.TwitchLiveSequence;
            for (int i = 0; i < playlist.Streams.Length; i++)
            {
                M3U8.Stream stream = playlist.Streams[i];
                var filename = startId is not null ? (startId + i).ToString() : stream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff");
                PartQueue.Enqueue(new()
                {
                    Path = stream.Path,
                    ProgramDateTime = stream.ProgramDateTime,
                    Duration = TimeSpan.FromSeconds((double)stream.PartInfo.Duration),
                    FileName = filename + DownloadTools.GetStreamPartFileExtension(stream.Path)
                });
            }

            return GetCorrectedPartStates();
        }

        public void StopDownload()
        {
            DownloadInProgress = false;
        }

        public IEnumerable<PartState> GetLastParts() => GetCorrectedPartStates().Concat([_lastPartProcessed]);

        private List<PartState> GetCorrectedPartStates()
        {
            while (_lastPartProcessed is null || !_lastPartProcessed.IsDownloaded)
            {
                if (ProcessedParts.IsEmpty)
                    return [];

                var oldest = ProcessedParts.Keys.Min();
                ProcessedParts.TryRemove(oldest, out _lastPartProcessed);
            }

            List<PartState> correctedParts = [];
            while (ProcessedParts.TryRemove(_lastPartProcessed.ProgramDateTime + _lastPartProcessed.Duration, out var nextPart))
            {
                if (!nextPart.IsDownloaded)
                {
                    _lastPartProcessed.Duration += nextPart.Duration;
                }
                else
                {
                    correctedParts.Add(_lastPartProcessed);
                    _lastPartProcessed = nextPart;
                }
            }

            return correctedParts;
        }
    }
}