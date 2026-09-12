using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ClipDownloadTask(TwitchTask dependantTask = null) : TwitchTask(dependantTask)
    {
        public ClipDownloadOptions DownloadOptions { get; set; }
        public override string TaskType { get; } = Translations.Strings.ClipDownload;
        public override string OutputFile => DownloadOptions.Filename;

        protected override async Task RunAsync()
        {
            if (TokenSource.IsCancellationRequested)
            {
                TokenSource.Dispose();
                ChangeStatus(TwitchTaskStatus.Canceled);
                CanReinitialize = true;
                return;
            }

            var downloader = new ClipDownloader(DownloadOptions, TaskProgress);
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