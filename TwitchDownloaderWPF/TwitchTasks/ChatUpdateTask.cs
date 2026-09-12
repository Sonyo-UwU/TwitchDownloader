using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;
using TwitchDownloaderWPF.Utils;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ChatUpdateTask(ChatUpdateOptions updateOptions, TaskData info, TwitchTask dependantTask = null) : TwitchTask(info, dependantTask)
    {
        public ChatUpdateOptions UpdateOptions { get; } = updateOptions;
        public override string TaskType { get; } = Translations.Strings.ChatUpdate;
        public override string OutputFile => UpdateOptions.OutputFile;

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
            var updater = new ChatUpdater(UpdateOptions, progress);
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