using TwitchDownloaderCore;
using TwitchDownloaderCore.Options;
using TwitchDownloaderWPF.Utils;

namespace TwitchDownloaderWPF.TwitchTasks
{
    internal class ChatRenderTask(ChatRenderOptions renderOptions, TaskData info, TwitchTask dependantTask = null) : TwitchTask(info, dependantTask)
    {
        public ChatRenderOptions RenderOptions { get; } = renderOptions;
        public override string TaskType { get; } = Translations.Strings.ChatRender;
        public override string OutputFile => RenderOptions.OutputFile;

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
            var renderer = new ChatRenderer(RenderOptions, progress);
            ChangeStatus(TwitchTaskStatus.Running);
            try
            {
                await renderer.ParseJsonAsync(TokenSource.Token);
                await renderer.RenderVideoAsync(TokenSource.Token);
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
            renderer.Dispose();
            TokenSource.Dispose();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, false);
        }
    }
}