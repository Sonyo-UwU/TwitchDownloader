using TwitchDownloaderCore.Interfaces;

namespace TwitchDownloaderCore.Tools
{
    internal sealed record StreamDownloadThread
    {
        private readonly StreamDownloadState _downloadState;
        private readonly HttpClient _client;
        private readonly string _cacheFolder;
        private readonly int _throttleKib;
        private readonly ITaskLogger _logger;
        private readonly CancellationToken _cancellationToken;
        public Task ThreadTask { get; private set; }

        public StreamDownloadThread(StreamDownloadState downloadState, HttpClient httpClient, string cacheFolder, int throttleKib, ITaskLogger logger, CancellationToken cancellationToken)
        {
            _downloadState = downloadState;
            _client = httpClient;
            _cacheFolder = cacheFolder;
            _throttleKib = throttleKib;
            _logger = logger;
            _cancellationToken = cancellationToken;
            StartDownload();
        }

        public void StartDownload()
        {
            if (ThreadTask is { Status: TaskStatus.Created or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun or TaskStatus.Running })
            {
                throw new InvalidOperationException($"Tried to start a thread that was already running or waiting to run ({ThreadTask.Status}).");
            }

            ThreadTask = Task.Factory.StartNew(
                Execute,
                _cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        private void Execute()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);

            while (_downloadState.DownloadInProgress || !_downloadState.PartQueue.IsEmpty)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (_downloadState.PartQueue.TryDequeue(out var videoPart))
                {
                    try
                    {
                        var result = DownloadStreamPartAsync(videoPart, cts).GetAwaiter().GetResult();
                        if (!result)
                        {
                            Thread.Sleep(Random.Shared.Next(100, 1_000));
                            _downloadState.PartQueue.Enqueue(videoPart);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Deliberately do not re-enqueue the part on exceptions
                        _logger.LogWarning($"Error while downloading {videoPart}: {ex.Message}");
                    }
                }

				// TODO: use mutexes to avoid busy-waiting
                Thread.Sleep(Random.Shared.Next(100, 200));
            }
        }

        /// <remarks>The <paramref name="cancellationTokenSource"/> may be canceled by this method.</remarks>
        private async Task<bool> DownloadStreamPartAsync(string videoPartName, CancellationTokenSource cancellationTokenSource)
        {
            var partState = _downloadState.PartStates[videoPartName];
            var partUri = new Uri(videoPartName);
            var partFile = Path.Combine(_cacheFolder, partState.FileName);
            var partFi = new FileInfo(partFile);

            if (partFi.Exists)
            {
                _logger.LogWarning($"Tried to redownload already downloaded part: {videoPartName}.");
                return true;
            }

            try
            {
                // Check download attempts
                const int MAX_DOWNLOAD_ATTEMPTS = 5;
                if (partState.DownloadAttempts++ >= MAX_DOWNLOAD_ATTEMPTS)
                {
                    throw new Exception($"{videoPartName} failed to download after {partState.DownloadAttempts - 1} attempts.");
                }

                // Download file
                // Stream parts don't have a Content-Length header, so this always returns -1
                await DownloadTools.DownloadFileAsync(_client, partUri, partFile, _downloadState.HeaderFile, _throttleKib, _logger, cancellationTokenSource);

                // Check file size
                partFi.Refresh();
                CheckTsLength(partFile, partFi.Length);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogVerbose(ex.StatusCode.HasValue
                    ? $"Received {(int)ex.StatusCode}: {ex.StatusCode} for {videoPartName}."
                    : $"{videoPartName}: {ex.Message}");

                await Delay(1_000, cancellationTokenSource.Token);
                return false;
            }
            catch (TaskCanceledException ex) when (ex.Message.Contains("HttpClient.Timeout"))
            {
                _logger.LogVerbose($"{videoPartName} timed out.");

                await Delay(5_000, cancellationTokenSource.Token);
                return false;
            }

            return true;
        }

        private void CheckTsLength(string partFile, long length)
        {
            if (!partFile.EndsWith(".ts"))
            {
                return;
            }

            const int TS_PACKET_LENGTH = 188; // MPEG TS packets are made of a header and a body: [ 4B ][   184B   ] - https://tsduck.io/download/docs/mpegts-introduction.pdf
            if (length % TS_PACKET_LENGTH != 0)
            {
                _logger.LogWarning($"{Path.GetFileName(partFile)} contains malformed packets and may cause encoding issues.");
            }
        }

        private static Task Delay(int millis, CancellationToken cancellationToken)
        {
            var jitteredMillis = millis + Random.Shared.Next(-200, 200);
            return Task.Delay(Math.Clamp(jitteredMillis, millis / 2, millis * 2), cancellationToken);
        }
    }
}