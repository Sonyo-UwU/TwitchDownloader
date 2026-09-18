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
            if (_expectedNextPart == default)
            {
                _expectedNextPart = playlist.Streams[0].ProgramDateTime;
            }

            var correctedParts = GetCorrectedPartStates();

            for (int i = 0; i < playlist.Streams.Length; i++)
            {
                M3U8.Stream stream = playlist.Streams[i];
                if (stream.ProgramDateTime < _expectedNextPart)
                    continue;

                // Streams might rarely have a gap of a few microseconds
                if (stream.ProgramDateTime - _expectedNextPart > TimeSpan.FromMicroseconds(10))
                {
                    logger.LogWarning($"Parts from {_expectedNextPart.ToString("yyyy-MM-ddTHH-mm-ss.fffffff")} to {stream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff")} are missing from the live feed.");

                    ProcessedParts[_expectedNextPart] = new PartState()
                    {
                        ProgramDateTime = _expectedNextPart,
                        Duration = stream.ProgramDateTime - _expectedNextPart,
                        FileName = "",
                        Path = "",
                        IsDownloaded = false
                    };
                    lock (TimeWriteLock)
                    {
                        TotalMissingTime += stream.ProgramDateTime - _expectedNextPart;
                    }
                }

                PartQueue.Enqueue(new()
                {
                    Path = stream.Path,
                    ProgramDateTime = stream.ProgramDateTime,
                    Duration = TimeSpan.FromSeconds((double)stream.PartInfo.Duration),
                    FileName = stream.ProgramDateTime.ToString("yyyy-MM-ddTHH-mm-ss.fffffff") + DownloadTools.GetStreamPartFileExtension(stream.Path)
                });
                _expectedNextPart = stream.ProgramDateTime + TimeSpan.FromSeconds((double)stream.PartInfo.Duration);
            }

            return correctedParts;
        }

        public void StopDownload()
        {
            DownloadInProgress = false;
        }

        public IEnumerable<PartState> GetLastParts()
        {
            if (_lastPartProcessed is null && ProcessedParts.IsEmpty)
            {
                return [];
            }

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

            return correctedParts;
        }
    }
}