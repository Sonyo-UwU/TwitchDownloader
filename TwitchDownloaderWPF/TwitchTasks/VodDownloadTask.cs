using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class VodDownloadTask(VideoDownloadOptions downloadOptions, TaskData info, TwitchTask dependantTask = null) : TwitchTask(info, dependantTask)
    {
        public VideoDownloadOptions DownloadOptions { get; } = downloadOptions;
        public override string TaskType { get; } = Translations.Strings.VodDownload;
        public override string OutputFile => DownloadOptions.Filename;

        protected override async Task RunAsync()
        {
            if (DownloadOptions.DelayDownload)
            {
                var success = await DelayUntilVideoOffline(DownloadOptions.Id, TaskProgress);
                if (!success)
                {
                    ChangeStatus(TwitchTaskStatus.Failed);
                    CanReinitialize = true;
                    return;
                }
            }

            if (TokenSource.IsCancellationRequested)
            {
                TokenSource.Dispose();
                ChangeStatus(TwitchTaskStatus.Canceled);
                CanReinitialize = true;
                return;
            }

            var downloader = new VideoDownloader(DownloadOptions, TaskProgress);
            ChangeStatus(TwitchTaskStatus.Running);

            try
            {
                await downloader.DownloadAsync(TokenSource.Token);
                if (TokenSource.IsCancellationRequested)
                {
                    ChangeStatus(TwitchTaskStatus.Canceled);
                    CanReinitialize = true;
                }
                else
                {
                    TaskProgress.ReportProgress(100);
                    ChangeStatus(TwitchTaskStatus.Finished);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && TokenSource.IsCancellationRequested)
            {
                ChangeStatus(TwitchTaskStatus.Canceled);
                CanReinitialize = true;
            }
            catch (Exception ex)
            {
                ChangeStatus(TwitchTaskStatus.Failed);
                Exception = ex;
                CanReinitialize = true;
            }
            TokenSource.Dispose();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, false);
        }
    }
}