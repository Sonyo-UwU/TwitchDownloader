using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Services;
using TwitchDownloaderWPF.TwitchTasks;

namespace TwitchDownloaderWPF
{
    /// <summary>
    /// Interaction logic for QueueOptions.xaml
    /// </summary>
    public partial class WindowQueueOptions : Window
    {
        // This file is absolutely atrocious, but fixing it would mean rewriting the entire GUI in a more abstract form

        private readonly IList<TaskData> _dataList;
        private readonly Page _parentPage;

        private bool CheckRenderWasChecked = false;

        public WindowQueueOptions(Page page)
        {
            _parentPage = page;
            InitializeComponent();

            textFolder.Text = Settings.Default.QueueFolder;

            TextPreferredQuality.Visibility = Visibility.Collapsed;
            ComboPreferredQuality.Visibility = Visibility.Collapsed;

            if (page is PageVodDownload)
            {
                throw new UnreachableException();
            }
            else if (page is PageClipDownload)
            {
                throw new UnreachableException();
            }
            else if (page is PageChatDownload)
            {
                throw new UnreachableException();
            }
            else if (page is PageChatUpdate)
            {
                checkVideo.Visibility = Visibility.Collapsed;
                checkDelayVideo.Visibility = Visibility.Collapsed;
                checkChatDownload.Visibility = Visibility.Collapsed;
                TextDownloadFormat.Visibility = Visibility.Collapsed;
                radioJson.Visibility = Visibility.Collapsed;
                radioText.Visibility = Visibility.Collapsed;
                radioHTML.Visibility = Visibility.Collapsed;
                TextCompression.Visibility = Visibility.Collapsed;
                RadioCompressionNone.Visibility = Visibility.Collapsed;
                RadioCompressionGzip.Visibility = Visibility.Collapsed;
                checkEmbed.Visibility = Visibility.Collapsed;
                StackThirdPartyEmbed.Visibility = Visibility.Collapsed;
                checkDelayChat.Visibility = Visibility.Collapsed;
                checkRender.Visibility = Visibility.Collapsed;
            }
            else if (page is PageChatRender)
            {
                checkVideo.Visibility = Visibility.Collapsed;
                checkDelayVideo.Visibility = Visibility.Collapsed;
                checkChatDownload.Visibility = Visibility.Collapsed;
                TextDownloadFormat.Visibility = Visibility.Collapsed;
                radioJson.Visibility = Visibility.Collapsed;
                radioText.Visibility = Visibility.Collapsed;
                radioHTML.Visibility = Visibility.Collapsed;
                TextCompression.Visibility = Visibility.Collapsed;
                RadioCompressionNone.Visibility = Visibility.Collapsed;
                RadioCompressionGzip.Visibility = Visibility.Collapsed;
                checkEmbed.Visibility = Visibility.Collapsed;
                StackThirdPartyEmbed.Visibility = Visibility.Collapsed;
                checkDelayChat.Visibility = Visibility.Collapsed;
                checkRender.IsChecked = true;
                checkRender.IsEnabled = false;
            }
        }

        public WindowQueueOptions(IList<TaskData> dataList,
            bool forceVideoDownload = false,
            bool forceChatDownload = false,
            bool forceChatUpdate = false,
            string[] videoQualities = null,
            int selectedQuality = 0)
        {
            _dataList = dataList;
            InitializeComponent();

            if (_dataList.Count == 0)
                throw new Exception("Data list must contain at least one task");

            textFolder.Text = Settings.Default.QueueFolder;

            if (_dataList.All(x => !x.IsDownload))
            {
                // No download
                VideoDownloadSettings.Visibility = Visibility.Collapsed;
                checkChatDownload.Visibility = Visibility.Collapsed;
                checkDelayChat.Visibility = Visibility.Collapsed;
                checkEmbed.Content = Translations.Strings.EmbedMissing;
            }
            else
            {
                if (_dataList.All(x => x.IsDownload))
                {
                    // All download
                    checkChatUpdate.Visibility = Visibility.Collapsed;
                    CheckReplaceEmbeds.Visibility = Visibility.Collapsed;

                    if (_dataList.All(x => !x.Id.All(char.IsDigit)))
                    {
                        // All clip download
                        CheckTrimStart.Visibility = Visibility.Collapsed;
                        TrimStartSettings.Visibility = Visibility.Collapsed;
                        CheckTrimEnd.Visibility = Visibility.Collapsed;
                        TrimEndSettings.Visibility = Visibility.Collapsed;
                    }
                }

                if (_dataList.Any(x => x.IsDownload && !x.Id.All(char.IsDigit)))
                {
                    // At least one clip download
                    ComboPreferredQuality.Items.Insert(1, new ComboBoxItem { Content = "Source Portrait" });
                    ComboPreferredQuality.Items.Add(new ComboBoxItem { Content = "Worst Portrait" });
                }

                if (_dataList.Any(x => x.IsDownload && x.Id.All(char.IsDigit)))
                {
                    // At least one vod download
                    ComboPreferredQuality.Items.Add(new ComboBoxItem { Content = "Audio Only" });
                }
                else
                {
                    // No vod download
                    checkDelayVideo.Visibility = Visibility.Collapsed;
                }
            }

            if (videoQualities is { Length: > 0 })
            {
                ComboPreferredQuality.Items.Clear();
                for (var i = 0; i < videoQualities.Length; i++)
                {
                    ComboPreferredQuality.Items.Add(new ComboBoxItem() { Content = videoQualities[i] });
                    if (i == selectedQuality)
                    {
                        ComboPreferredQuality.SelectedIndex = i;
                    }
                }
            }
            else
            {
                var preferredQuality = Settings.Default.PreferredQuality;
                for (var i = 0; i < ComboPreferredQuality.Items.Count; i++)
                {
                    if (ComboPreferredQuality.Items[i] is ComboBoxItem { Content: string quality } && quality == preferredQuality)
                    {
                        ComboPreferredQuality.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (forceVideoDownload)
            {
                checkVideo.IsChecked = true;
                checkVideo.IsEnabled = false;
            }
            if (forceChatDownload)
            {
                checkChatDownload.IsChecked = true;
                checkChatDownload.IsEnabled = false;
            }
            if (forceChatUpdate)
            {
                checkChatUpdate.IsChecked = true;
                checkChatUpdate.IsEnabled = false;
            }

            switch ((ChatFormat)Settings.Default.ChatDownloadType)
            {
                case ChatFormat.Json:
                    radioJson.IsChecked = true;
                    break;
                case ChatFormat.Text:
                    radioText.IsChecked = true;
                    break;
                case ChatFormat.Html:
                    radioHTML.IsChecked = true;
                    break;
            }
            switch ((ChatCompression)Settings.Default.ChatJsonCompression)
            {
                case ChatCompression.None:
                    RadioCompressionNone.IsChecked = true;
                    break;
                case ChatCompression.Gzip:
                    RadioCompressionGzip.IsChecked = true;
                    break;
            }

            checkEmbed.IsChecked = _dataList.All(x => !x.IsDownload) ? Settings.Default.ChatEmbedMissing : Settings.Default.ChatEmbedEmotes;
            CheckReplaceEmbeds.IsChecked = Settings.Default.ChatReplaceEmbeds;
            CheckBttvEmbed.IsChecked = Settings.Default.BTTVEmotes;
            CheckFfzEmbed.IsChecked = Settings.Default.FFZEmotes;
            CheckStvEmbed.IsChecked = Settings.Default.STVEmotes;


            // Set trims from first task
            var firstTask = _dataList[0];
            NumTrimStartHour.Value = (int)firstTask.TrimStartTime.TotalHours;
            NumTrimStartMinute.Value = firstTask.TrimStartTime.Minutes;
            NumTrimStartSecond.Value = firstTask.TrimStartTime.Seconds;
            CheckTrimStart.IsChecked = firstTask.TrimStart;
            NumTrimEndHour.Value = (int)firstTask.RelativeTrimEndTime.TotalHours;
            NumTrimEndMinute.Value = firstTask.RelativeTrimEndTime.Minutes;
            NumTrimEndSecond.Value = firstTask.RelativeTrimEndTime.Seconds;
            CheckTrimEnd.IsChecked = firstTask.TrimEnd;

            // Check if any other task has other trim options
            for (int i = 1; i < _dataList.Count; i++)
            {
                TaskData task = _dataList[i];

                if (NumTrimStartHour.Value   != (int)task.TrimStartTime.TotalHours ||
                    NumTrimStartMinute.Value !=      task.TrimStartTime.Minutes    ||
                    NumTrimStartSecond.Value !=      task.TrimStartTime.Seconds    ||
                    CheckTrimStart.IsChecked.GetValueOrDefault() != task.TrimStart ||
                    
                    NumTrimEndHour.Value   != (int)task.RelativeTrimEndTime.TotalHours ||
                    NumTrimEndMinute.Value !=      task.RelativeTrimEndTime.Minutes    ||
                    NumTrimEndSecond.Value !=      task.RelativeTrimEndTime.Seconds    ||
                    CheckTrimEnd.IsChecked.GetValueOrDefault() != task.TrimEnd)
                {
                    // Incompatible, disable trim options
                    CheckTrimStart.Visibility = Visibility.Collapsed;
                    TrimStartSettings.Visibility = Visibility.Collapsed;
                    CheckTrimEnd.Visibility = Visibility.Collapsed;
                    TrimEndSettings.Visibility = Visibility.Collapsed;
                    break;
                }
            }

            UpdateEnabled();
        }

        private FileInfo HandleFileCollisionCallback(FileInfo fileInfo)
        {
            return Dispatcher.Invoke(() => FileCollisionService.HandleCollisionCallback(fileInfo, Application.Current.MainWindow));
        }

        private void btnQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_parentPage != null)
            {
                if (_parentPage is PageVodDownload vodDownloadPage)
                {
                    throw new UnreachableException();
                }

                if (_parentPage is PageClipDownload clipDownloadPage)
                {
                    throw new UnreachableException();
                }

                if (_parentPage is PageChatDownload chatDownloadPage)
                {
                    throw new UnreachableException();
                }

                if (_parentPage is PageChatUpdate chatUpdatePage)
                {
                    throw new UnreachableException();
                }

                if (_parentPage is PageChatRender chatRenderPage)
                {
                    string folderPath = textFolder.Text;
                    foreach (string fileName in chatRenderPage.FileNames)
                    {
                        if (!Directory.Exists(folderPath))
                        {
                            try
                            {
                                TwitchHelper.CreateDirectory(folderPath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(this, Translations.Strings.InvalidFolderPathMessage, Translations.Strings.InvalidFolderPath, MessageBoxButton.OK, MessageBoxImage.Error);

                                if (Settings.Default.VerboseErrors)
                                {
                                    MessageBox.Show(this, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                                }

                                return;
                            }
                        }

                        string fileFormat = chatRenderPage.comboFormat.SelectedItem.ToString()!;
                        string filePath = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(fileName) + "." + fileFormat.ToLower());
                        ChatRenderOptions renderOptions = MainWindow.pageChatRender.GetOptions(filePath);
                        renderOptions.InputFile = fileName;
                        renderOptions.FileCollisionCallback = HandleFileCollisionCallback;

                        ChatRenderTask renderTask = new ChatRenderTask
                        {
                            DownloadOptions = renderOptions,
                            Info =
                            {
                                Title = Path.GetFileNameWithoutExtension(filePath)
                            }
                        };

                        if (ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out var image))
                        {
                            renderTask.Info.Thumbnail = image;
                        }

                        lock (PageQueue.TaskLock)
                        {
                            PageQueue.taskList.Add(renderTask);
                        }

                        this.Close();
                    }
                }
            }
            else if (_dataList.Count > 0)
            {
                EnqueueDataList();
            }
        }

        private void EnqueueDataList()
        {
            string folderPath = textFolder.Text;
            if (!Directory.Exists(folderPath))
            {
                try
                {
                    TwitchHelper.CreateDirectory(folderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Translations.Strings.InvalidFolderPathMessage, Translations.Strings.InvalidFolderPath, MessageBoxButton.OK, MessageBoxImage.Error);

                    if (Settings.Default.VerboseErrors)
                    {
                        MessageBox.Show(this, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    return;
                }
            }

            if (!ValidateTrims())
            {
                return;
            }

            foreach (var taskData in _dataList)
            {
                if (taskData.IsDownload)
                {
                    EnqueueDownloadTask(taskData, folderPath);
                }
                else
                {
                    EnqueueLocalTask(taskData, folderPath);
                }
            }

            this.DialogResult = true;
            this.Close();
        }

        private void EnqueueDownloadTask(TaskData taskData, string folderPath)
        {
            if (checkVideo.IsChecked.GetValueOrDefault())
            {
                if (taskData.Id.All(char.IsDigit))
                {
                    EnqueueVodDownload(taskData, folderPath);
                }
                else
                {
                    EnqueueClipDownload(taskData, folderPath);
                }
            }

            if (checkChatDownload.IsChecked.GetValueOrDefault())
            {
                EnqueueChatDownload(taskData, folderPath);
            }
        }

        private void EnqueueLocalTask(TaskData taskData, string folderPath)
        {
            if (checkChatUpdate.IsChecked.GetValueOrDefault())
            {
                // Also handles rendering of output
                EnqueueChatUpdate(taskData, folderPath);
            }
            else if (checkRender.IsChecked.GetValueOrDefault())
            {
                EnqueueChatRender(taskData, folderPath);
            }
        }

        private void EnqueueVodDownload(TaskData taskData, string folderPath)
        {
            VideoDownloadOptions downloadOptions = new VideoDownloadOptions
            {
                Oauth = Settings.Default.OAuth,
                TempFolder = Settings.Default.TempPath,
                Id = long.Parse(taskData.Id),
                Quality = (ComboPreferredQuality.SelectedItem as ComboBoxItem)?.Content as string,
                FfmpegPath = "ffmpeg",
                TrimBeginning = CheckTrimStart.IsChecked.GetValueOrDefault(),
                TrimBeginningTime = new TimeSpan((int)NumTrimStartHour.Value, (int)NumTrimStartMinute.Value, (int)NumTrimStartSecond.Value),
                TrimEnding = CheckTrimEnd.IsChecked.GetValueOrDefault(),
                TrimEndingTime = taskData.Length - GetTrimEnd(),
                DownloadThreads = Settings.Default.VodDownloadThreads,
                ThrottleKib = Settings.Default.DownloadThrottleEnabled
                                ? Settings.Default.MaximumBandwidthKib
                                : -1,
                FileCollisionCallback = HandleFileCollisionCallback,
                DelayDownload = checkDelayVideo.IsChecked.GetValueOrDefault()
            };
            downloadOptions.Filename = Path.Combine(folderPath,
                FilenameService.GetFilename(
                    Settings.Default.TemplateVod,
                    taskData.Title,
                    taskData.Id,
                    taskData.Time,
                    taskData.StreamerName,
                    taskData.StreamerId,
                    downloadOptions.TrimBeginning ? downloadOptions.TrimBeginningTime : TimeSpan.Zero,
                    downloadOptions.TrimEnding ? downloadOptions.TrimEndingTime : taskData.Length,
                    taskData.Length,
                    taskData.Views,
                    taskData.Game) +
                FilenameService.GuessVodFileExtension(downloadOptions.Quality));

            VodDownloadTask downloadTask = new VodDownloadTask
            {
                DownloadOptions = downloadOptions,
                Info =
                {
                    Title = taskData.Title,
                    Thumbnail = taskData.Thumbnail
                }
            };

            lock (PageQueue.TaskLock)
            {
                PageQueue.taskList.Add(downloadTask);
            }
        }

        private void EnqueueClipDownload(TaskData taskData, string folderPath)
        {
            ClipDownloadOptions downloadOptions = new ClipDownloadOptions
            {
                Id = taskData.Id,
                Quality = (ComboPreferredQuality.SelectedItem as ComboBoxItem)?.Content as string,
                Filename = Path.Combine(folderPath,
                    FilenameService.GetFilename(
                        Settings.Default.TemplateClip,
                        taskData.Title,
                        taskData.Id,
                        taskData.Time,
                        taskData.StreamerName,
                        taskData.StreamerId,
                        TimeSpan.Zero,
                        taskData.Length,
                        taskData.Length,
                        taskData.Views,
                        taskData.Game,
                        taskData.ClipperName,
                        taskData.ClipperId) +
                    ".mp4"),
                ThrottleKib = Settings.Default.DownloadThrottleEnabled
                                ? Settings.Default.MaximumBandwidthKib
                                : -1,
                TempFolder = Settings.Default.TempPath,
                EncodeMetadata = Settings.Default.EncodeClipMetadata,
                FfmpegPath = "ffmpeg",
                FileCollisionCallback = HandleFileCollisionCallback,
            };

            ClipDownloadTask downloadTask = new ClipDownloadTask
            {
                DownloadOptions = downloadOptions,
                Info =
                {
                    Title = taskData.Title,
                    Thumbnail = taskData.Thumbnail
                }
            };

            lock (PageQueue.TaskLock)
            {
                PageQueue.taskList.Add(downloadTask);
            }
        }

        private void EnqueueChatDownload(TaskData taskData, string folderPath)
        {
            ChatDownloadOptions downloadOptions = new ChatDownloadOptions
            {
                EmbedData = checkEmbed.IsChecked.GetValueOrDefault(),
                BttvEmotes = CheckBttvEmbed.IsChecked.GetValueOrDefault(),
                FfzEmotes = CheckFfzEmbed.IsChecked.GetValueOrDefault(),
                StvEmotes = CheckStvEmbed.IsChecked.GetValueOrDefault(),
                TimeFormat = TimestampFormat.Relative,
                Id = taskData.Id,
                TrimBeginning = CheckTrimStart.IsChecked.GetValueOrDefault() && taskData.Id.All(char.IsDigit), // Clips can't be trimmed
                TrimBeginningTime = GetTrimStart().TotalSeconds,
                TrimEnding = CheckTrimEnd.IsChecked.GetValueOrDefault() && taskData.Id.All(char.IsDigit),
                TrimEndingTime = (taskData.Length - GetTrimEnd()).TotalSeconds,
                FileCollisionCallback = HandleFileCollisionCallback,
                DelayDownload = checkDelayChat.IsChecked.GetValueOrDefault(),
                DownloadThreads = Settings.Default.ChatDownloadThreads
            };
            if (radioJson.IsChecked == true)
                downloadOptions.DownloadFormat = ChatFormat.Json;
            else if (radioHTML.IsChecked == true)
                downloadOptions.DownloadFormat = ChatFormat.Html;
            else
                downloadOptions.DownloadFormat = ChatFormat.Text;
            // TODO: Support non-json chat compression
            if (RadioCompressionGzip.IsChecked.GetValueOrDefault() && downloadOptions.DownloadFormat == ChatFormat.Json)
                downloadOptions.Compression = ChatCompression.Gzip;
            downloadOptions.Filename = Path.Combine(folderPath,
                FilenameService.GetFilename(
                    Settings.Default.TemplateChat,
                    taskData.Title,
                    taskData.Id,
                    taskData.Time,
                    taskData.StreamerName,
                    taskData.StreamerId,
                    downloadOptions.TrimBeginning ? TimeSpan.FromSeconds(downloadOptions.TrimBeginningTime) : TimeSpan.Zero,
                    downloadOptions.TrimEnding ? TimeSpan.FromSeconds(downloadOptions.TrimEndingTime) : taskData.Length,
                    taskData.Length,
                    taskData.Views,
                    taskData.Game,
                    taskData.ClipperName,
                    taskData.ClipperId) +
                downloadOptions.FileExtension);

            ChatDownloadTask downloadTask = new ChatDownloadTask
            {
                DownloadOptions = downloadOptions,
                Info =
                {
                    Title = taskData.Title,
                    Thumbnail = taskData.Thumbnail
                }
            };

            lock (PageQueue.TaskLock)
            {
                PageQueue.taskList.Add(downloadTask);
            }

            if (checkRender.IsChecked.GetValueOrDefault())
            {
                EnqueueChatRender(taskData, folderPath, downloadTask);
            }
        }

        private void EnqueueChatUpdate(TaskData taskData, string folderPath)
        {
            ChatUpdateOptions updateOptions = new ChatUpdateOptions
            {
                EmbedMissing = checkEmbed.IsChecked.GetValueOrDefault(),
                ReplaceEmbeds = CheckReplaceEmbeds.IsChecked.GetValueOrDefault(),
                BttvEmotes = CheckBttvEmbed.IsChecked.GetValueOrDefault(),
                FfzEmotes = CheckFfzEmbed.IsChecked.GetValueOrDefault(),
                StvEmotes = CheckStvEmbed.IsChecked.GetValueOrDefault(),
                TextTimestampFormat = TimestampFormat.Relative,
                InputFile = taskData.FilePath,
                TrimBeginning = CheckTrimStart.IsChecked.GetValueOrDefault() && taskData.Id.All(char.IsDigit), // Clips can't be trimmed
                TrimBeginningTime = GetTrimStart().TotalSeconds,
                TrimEnding = CheckTrimEnd.IsChecked.GetValueOrDefault() && taskData.Id.All(char.IsDigit),
                TrimEndingTime = (taskData.Length - GetTrimEnd()).TotalSeconds,
                FileCollisionCallback = HandleFileCollisionCallback
            };
            if (radioJson.IsChecked == true)
                updateOptions.OutputFormat = ChatFormat.Json;
            else if (radioHTML.IsChecked == true)
                updateOptions.OutputFormat = ChatFormat.Html;
            else
                updateOptions.OutputFormat = ChatFormat.Text;
            // TODO: Support non-json chat compression
            if (RadioCompressionGzip.IsChecked.GetValueOrDefault() && updateOptions.OutputFormat == ChatFormat.Json)
                updateOptions.Compression = ChatCompression.Gzip;
            updateOptions.OutputFile = Path.Combine(folderPath,
                FilenameService.GetFilename(
                    Settings.Default.TemplateChat,
                    taskData.Title,
                    taskData.Id,
                    taskData.Time,
                    taskData.StreamerName,
                    taskData.StreamerId,
                    updateOptions.TrimBeginning ? TimeSpan.FromSeconds(updateOptions.TrimBeginningTime) : TimeSpan.Zero,
                    updateOptions.TrimEnding ? TimeSpan.FromSeconds(updateOptions.TrimEndingTime) : taskData.Length,
                    taskData.Length,
                    taskData.Views,
                    taskData.Game,
                    taskData.ClipperName,
                    taskData.ClipperId) +
                updateOptions.FileExtension);

            ChatUpdateTask updateTask = new ChatUpdateTask
            {
                UpdateOptions = updateOptions,
                Info =
                {
                    Title = taskData.Title,
                    Thumbnail = taskData.Thumbnail
                }
            };

            lock (PageQueue.TaskLock)
            {
                PageQueue.taskList.Add(updateTask);
            }

            if (checkRender.IsChecked.GetValueOrDefault())
            {
                EnqueueChatRender(taskData, folderPath, updateTask);
            }
        }

        private void EnqueueChatRender(TaskData taskData, string folderPath, TwitchTask dependantTask = null)
        {
            string filePath = Path.Combine(folderPath,
                FilenameService.GetFilename(
                    Settings.Default.TemplateChat,
                    taskData.Title,
                    taskData.Id,
                    taskData.Time,
                    taskData.StreamerName,
                    taskData.StreamerId,
                    CheckTrimStart.IsChecked.GetValueOrDefault() ? GetTrimStart() : TimeSpan.Zero,
                    CheckTrimEnd.IsChecked.GetValueOrDefault() ? taskData.Length - GetTrimEnd() : taskData.Length,
                    taskData.Length,
                    taskData.Views,
                    taskData.Game,
                    taskData.ClipperName,
                    taskData.ClipperId) +
                "." + MainWindow.pageChatRender.comboFormat.SelectedItem.ToString()!.ToLower());
            ChatRenderOptions renderOptions = MainWindow.pageChatRender.GetOptions(filePath);
            renderOptions.InputFile = dependantTask is null ? taskData.FilePath : dependantTask.OutputFile;
            renderOptions.FileCollisionCallback = HandleFileCollisionCallback;

            // No need to override if dependant task is already trimmed
            if (dependantTask is null && CheckTrimStart.IsChecked.GetValueOrDefault())
            {
                renderOptions.StartOverride = (int)GetTrimStart().TotalSeconds;
            }
            if (dependantTask is null && CheckTrimEnd.IsChecked.GetValueOrDefault())
            {
                renderOptions.EndOverride = (int)(taskData.Length - GetTrimEnd()).TotalSeconds;
            }

            ChatRenderTask renderTask = new ChatRenderTask
            {
                DownloadOptions = renderOptions,
                Info =
                {
                    Title = taskData.Title,
                    Thumbnail = taskData.Thumbnail
                },
                DependantTask = dependantTask
            };
            if (dependantTask is not null)
            {
                renderTask.ChangeStatus(TwitchTaskStatus.Waiting);
            }

            lock (PageQueue.TaskLock)
            {
                PageQueue.taskList.Add(renderTask);
            }
        }

        private bool ValidateTrims()
        {
            var startSeconds = CheckTrimStart.IsChecked.GetValueOrDefault() ? GetTrimStart() : TimeSpan.Zero;
            var endSeconds = CheckTrimEnd.IsChecked.GetValueOrDefault() ? GetTrimEnd() : TimeSpan.Zero;

            int incompatibleCount = 0;
            foreach (var taskData in _dataList)
            {
                if (startSeconds + endSeconds >= taskData.Length)
                {
                    incompatibleCount++;
                }
            }

            var clipCount = _dataList.Count(x => !x.Id.All(char.IsDigit));

            string message = null;
            bool isError = false;

            var showClipWarning = clipCount > 0 && (CheckTrimStart.IsChecked.GetValueOrDefault() || CheckTrimEnd.IsChecked.GetValueOrDefault());
            var showIncompatibleWarning = incompatibleCount > 0;
            if (showClipWarning && showIncompatibleWarning)
            {
                if (clipCount + incompatibleCount >= _dataList.Count)
                {
                    message = string.Format(Translations.Strings.TrimErrorIncompatibleAndClips, _dataList.Count);
                    isError = true;
                }
                else
                {
                    message = string.Format(Translations.Strings.TrimWarningIncompatibleAndClips, clipCount + incompatibleCount);
                }
            }
            else if (showClipWarning)
            {
                if (clipCount >= _dataList.Count)
                {
                    throw new UnreachableException("Trim inputs should be disabled when all inputs are clips");
                }
                else
                {
                    message = string.Format(Translations.Strings.TrimWarningClips, clipCount + incompatibleCount);
                }
            }
            else if (showIncompatibleWarning)
            {
                if (incompatibleCount >= _dataList.Count)
                {
                    message = string.Format(Translations.Strings.TrimErrorIncompatible, _dataList.Count);
                    isError = true;
                }
                else
                {
                    message = string.Format(Translations.Strings.TrimWarningIncompatible, clipCount + incompatibleCount);
                }
            }

            if (!string.IsNullOrEmpty(message))
            {
                if (isError)
                {
                    MessageBox.Show(this, message, Translations.Strings.IncompatibleTrims, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var result = MessageBox.Show(this, message, Translations.Strings.IncompatibleTrims, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel)
                {
                    return false;
                }
            }

            return true;
        }

        private TimeSpan GetTrimStart()
        {
            return new TimeSpan((int)NumTrimStartHour.Value, (int)NumTrimStartMinute.Value, (int)NumTrimStartSecond.Value);
        }

        private TimeSpan GetTrimEnd()
        {
            return new TimeSpan((int)NumTrimEndHour.Value, (int)NumTrimEndMinute.Value, (int)NumTrimEndSecond.Value);
        }

        private void btnFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (Directory.Exists(textFolder.Text))
            {
                dialog.InitialDirectory = textFolder.Text;
            }

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                textFolder.Text = dialog.FolderName;
            }
        }

        private void TextFolder_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.QueueFolder = textFolder.Text;
            Settings.Default.Save();
        }
        private void UpdateEnabled()
        {
            if (!IsInitialized)
                return;

            var enabledBrush  = (Brush)Application.Current.Resources["AppText"];
            var disabledBrush = (Brush)Application.Current.Resources["AppTextDisabled"];

            var videoSettingsEnabled = checkVideo.IsChecked.GetValueOrDefault();
            TextPreferredQuality.Foreground = videoSettingsEnabled ? enabledBrush : disabledBrush;
            ComboPreferredQuality.IsEnabled = videoSettingsEnabled;
            checkDelayVideo.IsEnabled = videoSettingsEnabled;

            var chatSettingsEnabled = checkChatDownload.IsChecked.GetValueOrDefault() || checkChatUpdate.IsChecked.GetValueOrDefault();
            TextDownloadFormat.Foreground = chatSettingsEnabled ? enabledBrush : disabledBrush;
            TextCompression.Foreground = chatSettingsEnabled ? enabledBrush : disabledBrush;
            radioJson.IsEnabled = chatSettingsEnabled;
            radioText.IsEnabled = chatSettingsEnabled;
            radioHTML.IsEnabled = chatSettingsEnabled;
            RadioCompressionNone.IsEnabled = chatSettingsEnabled;
            RadioCompressionGzip.IsEnabled = chatSettingsEnabled;
            checkDelayChat.IsEnabled = chatSettingsEnabled;

            StackChatCompression.Visibility = radioJson.IsChecked.GetValueOrDefault() ? Visibility.Visible : Visibility.Collapsed;

            var embedEnabled = chatSettingsEnabled && !radioText.IsChecked.GetValueOrDefault();
            checkEmbed.IsEnabled = embedEnabled;
            var embedSettingsEnabled = embedEnabled && checkEmbed.IsChecked.GetValueOrDefault();
            CheckReplaceEmbeds.IsEnabled = embedSettingsEnabled;
            CheckBttvEmbed.IsEnabled = embedSettingsEnabled;
            CheckFfzEmbed.IsEnabled = embedSettingsEnabled;
            CheckStvEmbed.IsEnabled = embedSettingsEnabled;

            checkRender.IsEnabled = _parentPage is not PageChatRender && (
                (checkChatDownload.IsChecked.GetValueOrDefault() && radioJson.IsChecked.GetValueOrDefault()) || // Download then render
                (checkChatUpdate.IsChecked.GetValueOrDefault() && radioJson.IsChecked.GetValueOrDefault()) || // Update then render
                _dataList is null ||
                (_dataList.Any(x => !x.IsDownload) && !checkChatUpdate.IsChecked.GetValueOrDefault() && !checkChatDownload.IsChecked.GetValueOrDefault())); // Direct render

            TrimStartSettings.IsEnabled = CheckTrimStart.IsChecked.GetValueOrDefault();
            TrimEndSettings.IsEnabled = CheckTrimEnd.IsChecked.GetValueOrDefault();
        }

        private void UpdateEnabledEvent(object sender, RoutedEventArgs e)
        {
            UpdateEnabled();
        }
        private void Window_OnSourceInitialized(object sender, EventArgs e)
        {
            App.RequestTitleBarChange();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Prevent height changes after initialization
            SizeToContent = SizeToContent.Width;
            UpdateEnabled();
        }

        private void ComboPreferredQuality_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized)
                return;

            if (ComboPreferredQuality.SelectedItem is ComboBoxItem { Content: string preferredQuality })
            {
                Settings.Default.PreferredQuality = preferredQuality;
            }
        }

        private void checkRender_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsInitialized)
                return;

            // Remember if option was checked before disable
            if (!checkRender.IsEnabled)
            {
                // On disable
                CheckRenderWasChecked = checkRender.IsChecked.GetValueOrDefault();
                checkRender.IsChecked = false;
            }
            else if (CheckRenderWasChecked)
            {
                // On enable
                checkRender.IsChecked = true;
            }
        }
    }
}