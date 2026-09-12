using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;
using TwitchDownloaderWPF.Utils;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ClipDownloadTask(ClipDownloadOptions downloadOptions, TaskData info, TwitchTask dependantTask = null) : TwitchTask(info, dependantTask)
    {
        public ClipDownloadOptions DownloadOptions { get; } = downloadOptions;
        public override string TaskType { get; } = Translations.Strings.ClipDownload;
        public override string OutputFile => DownloadOptions.Filename;

        public override async Task RunAsync()
        {
            if (TokenSource.IsCancellationRequested)
            {
                TokenSource.Dispose();
                ChangeStatus(TwitchTaskStatus.Canceled);
                CanReinitialize = true;
                return;
            }

            var progress = new WpfTaskProgress(i => Progress = i, s => DisplayStatus = s);
            var downloader = new ClipDownloader(DownloadOptions, progress);
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
                    progress.ReportProgress(100);
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