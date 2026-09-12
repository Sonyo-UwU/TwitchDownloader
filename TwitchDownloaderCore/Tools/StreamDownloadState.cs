using System.Collections.Concurrent;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;

namespace TwitchDownloaderCore.Tools
{
    internal sealed class StreamDownloadState(ITaskLogger logger)
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

        public Lock TimeWriteLock { get; } = new();

        private DateTimeOffset _expectedNextPart = default;

        private PartState _lastPartProcessed;

        public IEnumerable<PartState> AppendSegment(M3U8 playlist)
        {
            var firstStream = playlist.Streams[0];
            if (_expectedNextPart == default)
            {
                _expectedNextPart = firstStream.ProgramDateTime;
            }
            else
            {
                if (firstStream.ProgramDateTime - _expectedNextPart > TimeSpan.Zero)
                {
                    logger.LogWarning($"Parts from {_expectedNextPart.ToString("yyyy-MM-ddTHH-mm-ss.fffffff")} to {firstStream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff")} are missing and will be missing from the finalized video");

                    lock (TimeWriteLock)
                    {
                        TotalMissingTime += firstStream.ProgramDateTime - _expectedNextPart;
                    }
                }
            }

            uint? startId = playlist.FileMetadata.TwitchLiveSequence ?? playlist.FileMetadata.MediaSequence;
            for (int i = 0; i < playlist.Streams.Length; i++)
            {
                M3U8.Stream stream = playlist.Streams[i];
                if (stream.ProgramDateTime < _expectedNextPart)
                    continue;

                var filename = startId is not null ? (startId + i).ToString() : stream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff");
                PartQueue.Enqueue(new()
                {
                    Path = stream.Path,
                    ProgramDateTime = stream.ProgramDateTime,
                    Duration = TimeSpan.FromSeconds((double)stream.PartInfo.Duration),
                    FileName = filename + DownloadTools.GetStreamPartFileExtension(stream.Path)
                });
                _expectedNextPart = stream.ProgramDateTime + TimeSpan.FromSeconds((double)stream.PartInfo.Duration);
            }

            return GetCorrectedPartStates();
        }

        public void StopDownload()
        {
            DownloadInProgress = false;
        }

        public IEnumerable<PartState> GetLastParts()
        {
            _expectedNextPart = DateTimeOffset.MaxValue;
            return GetCorrectedPartStates().Concat([_lastPartProcessed]);
        }

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

            if (!ProcessedParts.IsEmpty && _expectedNextPart - _lastPartProcessed.ProgramDateTime > TimeSpan.FromSeconds(60))
            {
                // There was some parts missing
                var oldest = ProcessedParts.Values.MinBy(x => x.ProgramDateTime);
                _lastPartProcessed.Duration = oldest.ProgramDateTime - _lastPartProcessed.ProgramDateTime;
                correctedParts.Add(_lastPartProcessed);
                ProcessedParts.TryRemove(oldest.ProgramDateTime, out _lastPartProcessed);

                bool stop = false;
                do
                {
                    var nextParts = GetCorrectedPartStates();
                    stop = nextParts.Count == 0 || ProcessedParts.IsEmpty;
                    correctedParts.AddRange(nextParts);
                } while (!stop);
            }

            return correctedParts;
        }
    }
}