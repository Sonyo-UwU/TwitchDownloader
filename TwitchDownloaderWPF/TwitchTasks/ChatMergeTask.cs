using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;
using TwitchDownloaderWPF.Utils;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ChatMergeTask : TwitchTask
    {
        public ChatMergeOptions MergeOptions { get; init; }
        public TwitchTask[] DependantTasks { get; init; } = [];
        public override string TaskType { get; } = "ChatMerge";//Translations.Strings.ChatMerge;
        public override string OutputFile => MergeOptions.OutputFile;

        public override void Reinitialize()
        {
            Progress = 0;
            TokenSource = new CancellationTokenSource();
            Exception = null;
            CanReinitialize = false;
            ChangeStatus(DependantTasks.Length == 0 ? TwitchTaskStatus.Ready : TwitchTaskStatus.Waiting);
        }

        public override bool CanRun()
        {
            if (DependantTasks.Length == 0)
            {
                return Status == TwitchTaskStatus.Ready;
            }

            if (Status == TwitchTaskStatus.Waiting)
            {
                if (DependantTasks.All(t => t.Status == TwitchTaskStatus.Finished))
                {
                    return true;
                }

                if (DependantTasks.Any(t => t.Status is TwitchTaskStatus.Failed or TwitchTaskStatus.Canceled))
                {
                    ChangeStatus(TwitchTaskStatus.Canceled);
                    CanReinitialize = true;
                    return false;
                }
            }

            return false;
        }

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
            ChatMerger merger = new(MergeOptions, progress);
            ChangeStatus(TwitchTaskStatus.Running);
            try
            {
                await merger.ValidateInputsAsync(TokenSource.Token);
                await merger.MergeAsync(TokenSource.Token);
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
            GC.Collect(-1, GCCollectionMode.Default, false);
        }
    }
}