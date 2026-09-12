using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ChatUpdateTask(TwitchTask dependantTask = null) : TwitchTask(dependantTask)
    {
        public ChatUpdateOptions UpdateOptions { get; set; }
        public override string TaskType { get; } = Translations.Strings.ChatUpdate;
        public override string OutputFile => UpdateOptions.OutputFile;

        protected override async Task RunAsync()
        {
            if (TokenSource.IsCancellationRequested)
            {
                TokenSource.Dispose();
                ChangeStatus(TwitchTaskStatus.Canceled);
                CanReinitialize = true;
                return;
            }

            var updater = new ChatUpdater(UpdateOptions, TaskProgress);
            ChangeStatus(TwitchTaskStatus.Running);
            try
            {
                await updater.ParseJsonAsync(TokenSource.Token);
                await updater.UpdateAsync(TokenSource.Token);
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