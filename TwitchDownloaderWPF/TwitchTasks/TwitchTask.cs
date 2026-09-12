using JetBrains.Annotations;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using TwitchDownloaderCore.Extensions;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderWPF.Models;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Services;
using TwitchDownloaderWPF.Utils;

namespace TwitchDownloaderWPF.TwitchTasks
{
    public abstract class TwitchTask : INotifyPropertyChanged
    {
        public TwitchTask(TwitchTask dependantTask = null)
        {
            DependantTask = dependantTask;
            Status = dependantTask is null ? TwitchTaskStatus.Ready : TwitchTaskStatus.Waiting;
            TokenSource = new();
            TaskProgress = new WpfTaskProgress(i => Progress = i, s => DisplayStatus = s);

            dependantTask?.PropertyChanged += DependantTask_PropertyChanged;
        }

        [UsedImplicitly(Reason = "Used by PageQueue bindings")]
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<TaskTerminatedEventArgs> TaskTerminated;

        public TaskData Info { get; set; }
        public TwitchTask DependantTask { get; }

        public abstract string TaskType { get; }
        public abstract string OutputFile { get; }

        protected CancellationTokenSource TokenSource { get; set; }
        protected ITaskProgress TaskProgress { get; private set; }


        public int Progress
        {
            get;
            protected set => SetField(ref field, value);
        }

        public TwitchTaskStatus Status
        {
            get;
            private set => SetField(ref field, value);
        } = TwitchTaskStatus.Ready;

        public string DisplayStatus
        {
            get;
            protected set => SetField(ref field, value);
        }

        public string StatusImage
        {
            get;
            private set => SetField(ref field, value);
        }

        public Exception Exception
        {
            get;
            protected set => SetField(ref field, value);
        }

        public bool CanCancel
        {
            get;
            protected set => SetField(ref field, value);
        }

        public bool CanReinitialize
        {
            get;
            protected set => SetField(ref field, value);
        }


        protected abstract Task RunAsync();

        public async void Begin()
        {
            try
            {
                await RunAsync();
            }
            finally
            {
                OnTaskTerminated();
            }
        }

        internal void Begin(LogLevel logLevel, Action<string> handleLog, Action<string> handleFfmpegLog = null)
        {
            TaskProgress = new WpfTaskProgress(logLevel, i => Progress = i, s => DisplayStatus = s, handleLog, handleFfmpegLog);
            Begin();
        }


        public void Cancel()
        {
            if (!CanCancel)
                return;

            TokenSource.Cancel();

            ChangeStatus(Status is TwitchTaskStatus.Running ? TwitchTaskStatus.Stopping : TwitchTaskStatus.Canceled);
        }

        public void Reinitialize()
        {
            Progress = 0;
            TokenSource = new CancellationTokenSource();
            Exception = null;
            CanReinitialize = false;
            ChangeStatus(TwitchTaskStatus.Ready);
        }

        public bool CanRun()
        {
            return Status == TwitchTaskStatus.Ready;
        }

        public void ChangeStatus(TwitchTaskStatus newStatus)
        {
            Status = newStatus;
            DisplayStatus = newStatus.ToString();

            CanCancel = newStatus is not TwitchTaskStatus.Canceled and not TwitchTaskStatus.Failed and not TwitchTaskStatus.Finished and not TwitchTaskStatus.Stopping;

            if (Settings.Default.ReduceMotion)
            {
                StatusImage = null;
            }
            else
            {
                StatusImage = newStatus switch
                {
                    TwitchTaskStatus.Running => "Images/ppOverheat.gif",
                    TwitchTaskStatus.Ready or TwitchTaskStatus.Waiting => "Images/ppHop.gif",
                    TwitchTaskStatus.Stopping => "Images/ppStretch.gif",
                    TwitchTaskStatus.Failed => "Images/peepoSad.png",
                    _ => null
                };
            }
        }

        protected async Task<bool> DelayUntilVideoOffline(long videoId, ITaskLogger logger)
        {
            try
            {
                ChangeStatus(TwitchTaskStatus.Waiting);

                var videoMonitor = new LiveVideoMonitor(videoId, logger);
                while (await videoMonitor.IsVideoRecording(TokenSource.Token))
                {
                    var waitTime = Random.Shared.NextDouble(8, 14);
                    await Task.Delay(TimeSpan.FromSeconds(waitTime), TokenSource.Token);
                }

                var thumbUrl = videoMonitor.LatestVideoResponse.data.video.thumbnailURLs.FirstOrDefault();
                await MainWindow.pageQueue.Dispatcher.InvokeAsync(() =>
                {
                    if (ThumbnailService.TryGetThumb(thumbUrl, out var newThumb))
                    {
                        Info.Thumbnail = newThumb;
                        OnPropertyChanged(nameof(Info));
                    }
                }, DispatcherPriority.Normal, TokenSource.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && TokenSource.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception ex)
            {
                Exception = ex;
                return false;
            }

            return true;
        }

        private void OnTaskTerminated()
        {
            TaskTerminated?.Invoke(this, new TaskTerminatedEventArgs(Status == TwitchTaskStatus.Finished));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void DependantTask_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Status))
            {
                if (DependantTask.Status == TwitchTaskStatus.Finished)
                {
                    ChangeStatus(TwitchTaskStatus.Ready);
                }
                else if (DependantTask.Status is TwitchTaskStatus.Failed or TwitchTaskStatus.Canceled)
                {
                    ChangeStatus(TwitchTaskStatus.Canceled);
                    CanReinitialize = true;
                }
            }
        }
    }
}