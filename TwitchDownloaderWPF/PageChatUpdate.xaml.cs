using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.TwitchObjects.Gql;
using TwitchDownloaderWPF.Models;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Services;
using TwitchDownloaderWPF.TwitchTasks;
using TwitchDownloaderWPF.Utils;
using WpfAnimatedGif;

namespace TwitchDownloaderWPF
{
    /// <summary>
    /// Interaction logic for PageChatUpdate.xaml
    /// </summary>
    public partial class PageChatUpdate : Page
    {
        private ChatUpdateTask updateTask;
        private TaskData taskData => updateTask.Info;

        public PageChatUpdate()
        {
            InitializeUpdateTask();
            InitializeComponent();
        }

        private void InitializeUpdateTask()
        {
            updateTask?.PropertyChanged -= UpdateTask_PropertyChanged;
            updateTask?.TaskTerminated -= UpdateTask_TaskTerminated;
            updateTask = new() { Info = new() };
            updateTask.PropertyChanged += UpdateTask_PropertyChanged;
            updateTask.TaskTerminated += UpdateTask_TaskTerminated;
        }

        private void UpdateTask_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(updateTask.StatusImage))
            {
                var image = updateTask.StatusImage ?? "Images/ppHop.gif";
                SetImage(image, image.EndsWith(".gif"));
            }

            else if (e.PropertyName == nameof(updateTask.Progress))
            {
                SetPercent(updateTask.Progress);
            }

            else if (e.PropertyName == nameof(updateTask.DisplayStatus))
            {
                SetStatus(updateTask.DisplayStatus);
            }

            else if (e.PropertyName == nameof(updateTask.Status))
            {
                switch (updateTask.Status)
                {
                    case TwitchTaskStatus.Running:
                        statusMessage.Text = Translations.Strings.StatusUpdating;
                        break;
                    case TwitchTaskStatus.Finished:
                        statusMessage.Text = Translations.Strings.StatusDone;
                        break;
                    case TwitchTaskStatus.Stopping:
                        statusMessage.Text = Translations.Strings.StatusCanceling;
                        break;
                    case TwitchTaskStatus.Failed:
                        statusMessage.Text = Translations.Strings.StatusError;
                        break;
                }
            }

            else if (e.PropertyName == nameof(updateTask.Exception))
            {
                if (updateTask.Exception is not null)
                {
                    AppendLog(Translations.Strings.ErrorLog + updateTask.Exception.Message);
                    if (Settings.Default.VerboseErrors)
                    {
                        MessageBox.Show(Application.Current.MainWindow!, updateTask.Exception.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void UpdateTask_TaskTerminated(object sender, TaskTerminatedEventArgs e)
        {
            if (e.IsSuccessful)
            {
                InitializeUpdateTask();
            }
            else
            {
                updateTask.Reinitialize();
                SetEnabled(true);
            }

            textJson.Text = "";
            btnBrowse.IsEnabled = true;
            SetPercent(0);
            UpdateActionButtons(false);
        }

        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files | *.json;*.json.gz"
            };
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            textJson.Text = openFileDialog.FileName;
            var inputFile = openFileDialog.FileName;
            imgThumbnail.Source = null;
            SetEnabled(false);

            if (Path.GetExtension(inputFile)!.ToLower() is not ".json" and not ".gz")
            {
                textJson.Text = "";
                inputFile = "";
                return;
            }

            try
            {
                updateTask.Info = await TaskData.FromJsonFileAsync(inputFile);
                GC.Collect();
            }
            catch (Exception ex)
            {
                AppendLog(Translations.Strings.ErrorLog + ex.Message);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }

                return;
            }

            SetEnabled(true);


            var chatStart = taskData.TrimStartTime;
            numStartHour.Value = (int)chatStart.TotalHours;
            numStartMinute.Value = chatStart.Minutes;
            numStartSecond.Value = chatStart.Seconds;

            var chatEnd = taskData.TrimEndTime;
            numEndHour.Value = (int)chatEnd.TotalHours;
            numEndMinute.Value = chatEnd.Minutes;
            numEndSecond.Value = chatEnd.Seconds;


            try
            {
                if (taskData.Id.All(char.IsDigit))
                {
                    GqlVideoResponse videoInfo = await TwitchHelper.GetVideoInfo(long.Parse(taskData.Id));
                    if (videoInfo.data.video == null)
                    {
                        AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToFindThumbnail + ": " + Translations.Strings.VodExpiredOrIdCorrupt);
                        _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out var image);
                        imgThumbnail.Source = image;
                    }
                    else
                    {
                        taskData.Length = TimeSpan.FromSeconds(videoInfo.data.video.lengthSeconds);
                        taskData.Views = videoInfo.data.video.viewCount;
                        taskData.Game = videoInfo.data.video.game?.displayName;

                        var thumbUrl = videoInfo.data.video.thumbnailURLs.FirstOrDefault();
                        if (!ThumbnailService.TryGetThumb(thumbUrl, out var image))
                        {
                            AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToFindThumbnail);
                            _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out image);
                        }

                        imgThumbnail.Source = image;
                    }
                }
                else
                {
                    GqlClipResponse clipInfo = await TwitchHelper.GetClipInfo(taskData.Id);
                    if (clipInfo.data.clip.video == null)
                    {
                        AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToFindThumbnail + ": " + Translations.Strings.VodExpiredOrIdCorrupt);
                        _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out var image);
                        imgThumbnail.Source = image;
                    }
                    else
                    {
                        taskData.Length = TimeSpan.FromSeconds(clipInfo.data.clip.durationSeconds);
                        taskData.Views = clipInfo.data.clip.viewCount;
                        taskData.Game = clipInfo.data.clip.game?.displayName;
                        taskData.ClipperName ??= clipInfo.data.clip.curator?.displayName ?? Translations.Strings.UnknownUser;
                        taskData.ClipperId ??= clipInfo.data.clip.curator?.id;

                        var thumbUrl = clipInfo.data.clip.thumbnailURL;
                        if (!ThumbnailService.TryGetThumb(thumbUrl, out var image))
                        {
                            AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToFindThumbnail);
                            _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out image);
                        }

                        imgThumbnail.Source = image;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Application.Current.MainWindow!, Translations.Strings.UnableToGetInfoMessage, Translations.Strings.UnableToGetInfo, MessageBoxButton.OK, MessageBoxImage.Error);
                AppendLog(Translations.Strings.ErrorLog + ex.Message);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            numStartHour.Value = 0;
            numStartMinute.Value = 0;
            numStartSecond.Value = 0;

            numStartHour.Maximum = 48;
            numEndHour.Maximum = 48;
            numStartMinute.Maximum = 59;
            numEndMinute.Maximum = 59;
            numStartSecond.Maximum = 59;
            numEndSecond.Maximum = 59;

            if (taskData.Length > TimeSpan.Zero)
            {
                numStartHour.Maximum = taskData.Length.Hours;
                numEndHour.Maximum = taskData.Length.Hours;
                if (taskData.Length.Hours == 0)
                {
                    numStartMinute.Maximum = taskData.Length.Minutes;
                    numEndMinute.Maximum = taskData.Length.Minutes;
                    if (taskData.Length.Minutes == 0)
                    {
                        numStartSecond.Maximum = taskData.Length.Seconds;
                        numEndSecond.Maximum = taskData.Length.Seconds;
                    }
                }
            }

            textCreatedAt.Text = taskData.Time.ToString(CultureInfo.CurrentCulture);
            textStreamer.Text = taskData.StreamerName;
            textTitle.Text = taskData.Title;
            labelLength.Text = taskData.Length.Seconds > 0
                ? taskData.Length.ToString("c")
                : Translations.Strings.UnknownVideoLength;
        }

        private void UpdateActionButtons(bool isUpdating)
        {
            if (isUpdating)
            {
                SplitBtnUpdate.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Visible;
                return;
            }
            SplitBtnUpdate.Visibility = Visibility.Visible;
            BtnCancel.Visibility = Visibility.Collapsed;
        }

        private void Page_Initialized(object sender, EventArgs e)
        {
            SetEnabled(false);
            SetEnabledTrimStart(false);
            SetEnabledTrimEnd(false);
            checkEmbedMissing.IsChecked = Settings.Default.ChatEmbedMissing;
            checkReplaceEmbeds.IsChecked = Settings.Default.ChatReplaceEmbeds;
            checkBttvEmbed.IsChecked = Settings.Default.BTTVEmotes;
            checkFfzEmbed.IsChecked = Settings.Default.FFZEmotes;
            checkStvEmbed.IsChecked = Settings.Default.STVEmotes;
            _ = (ChatFormat)Settings.Default.ChatDownloadType switch
            {
                ChatFormat.Text => radioText.IsChecked = true,
                ChatFormat.Html => radioHTML.IsChecked = true,
                ChatFormat.Json => radioJson.IsChecked = true,
                _ => null,
            };
            _ = (ChatCompression)Settings.Default.ChatJsonCompression switch
            {
                ChatCompression.None => radioCompressionNone.IsChecked = true,
                ChatCompression.Gzip => radioCompressionGzip.IsChecked = true,
                _ => null,
            };
            _ = (TimestampFormat)Settings.Default.ChatTextTimestampStyle switch
            {
                TimestampFormat.Utc => radioTimestampUTC.IsChecked = true,
                TimestampFormat.Relative => radioTimestampRelative.IsChecked = true,
                TimestampFormat.None => radioTimestampNone.IsChecked = true,
                _ => null,
            };
        }

        private void SetEnabled(bool isEnabled)
        {
            checkStart.IsEnabled = isEnabled;
            checkEnd.IsEnabled = isEnabled;
            checkEmbedMissing.IsEnabled = isEnabled;
            checkReplaceEmbeds.IsEnabled = isEnabled;
            SplitBtnUpdate.IsEnabled = isEnabled;
            MenuItemEnqueue.IsEnabled = isEnabled;
            radioTimestampRelative.IsEnabled = isEnabled;
            radioTimestampUTC.IsEnabled = isEnabled;
            radioTimestampNone.IsEnabled = isEnabled;
            radioCompressionNone.IsEnabled = isEnabled;
            radioCompressionGzip.IsEnabled = isEnabled;
            radioJson.IsEnabled = isEnabled;
            radioText.IsEnabled = isEnabled;
            radioHTML.IsEnabled = isEnabled;

            if (isEnabled)
                return;

            checkBttvEmbed.IsEnabled = isEnabled;
            checkFfzEmbed.IsEnabled = isEnabled;
            checkStvEmbed.IsEnabled = isEnabled;
        }

        private void SetEnabledTrimStart(bool isEnabled)
        {
            numStartHour.IsEnabled = isEnabled;
            numStartMinute.IsEnabled = isEnabled;
            numStartSecond.IsEnabled = isEnabled;
            taskData.TrimStart = isEnabled;
        }

        private void SetEnabledTrimEnd(bool isEnabled)
        {
            numEndHour.IsEnabled = isEnabled;
            numEndMinute.IsEnabled = isEnabled;
            numEndSecond.IsEnabled = isEnabled;
            taskData.TrimEnd = isEnabled;
        }

        private void SetPercent(int percent)
        {
            Dispatcher.BeginInvoke(() =>
                statusProgressBar.Value = percent
            );
        }

        private void SetStatus(string message)
        {
            Dispatcher.BeginInvoke(() =>
                statusMessage.Text = message
            );
        }

        private void AppendLog(string message)
        {
            BtnClearLog.Dispatcher.BeginInvoke(() =>
                BtnClearLog.IsEnabled = true
            );
            textLog.Dispatcher.BeginInvoke(() =>
                textLog.AppendText(message + Environment.NewLine)
            );
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            BtnClearLog.IsEnabled = false;
            textLog.Dispatcher.BeginInvoke(() =>
                textLog.Document.Blocks.Clear()
            );
        }

        public ChatUpdateOptions GetOptions(string outputFile)
        {
            var options = new ChatUpdateOptions()
            {
                EmbedMissing = checkEmbedMissing.IsChecked.GetValueOrDefault(),
                ReplaceEmbeds = checkReplaceEmbeds.IsChecked.GetValueOrDefault(),
                BttvEmotes = checkBttvEmbed.IsChecked.GetValueOrDefault(),
                FfzEmotes = checkFfzEmbed.IsChecked.GetValueOrDefault(),
                StvEmotes = checkStvEmbed.IsChecked.GetValueOrDefault(),
                InputFile = textJson.Text,
                OutputFile = outputFile,
                TrimBeginningTime = -1,
                TrimEndingTime = -1
            };

            if (radioJson.IsChecked.GetValueOrDefault())
                options.OutputFormat = ChatFormat.Json;
            else if (radioHTML.IsChecked.GetValueOrDefault())
                options.OutputFormat = ChatFormat.Html;
            else if (radioText.IsChecked.GetValueOrDefault())
                options.OutputFormat = ChatFormat.Text;

            // TODO: Support non-json chat compression
            if (radioCompressionNone.IsChecked == true || options.OutputFormat != ChatFormat.Json)
                options.Compression = ChatCompression.None;
            else if (radioCompressionGzip.IsChecked == true)
                options.Compression = ChatCompression.Gzip;

            if (taskData.TrimStart == true)
            {
                options.TrimBeginning = true;
                TimeSpan start = taskData.TrimStartTime;
                options.TrimBeginningTime = (int)Math.Round(start.TotalSeconds);
            }
            if (taskData.TrimEnd == true)
            {
                options.TrimEnding = true;
                TimeSpan end = taskData.TrimEndTime;
                options.TrimEndingTime = (int)Math.Round(end.TotalSeconds);
            }

            if (radioTimestampUTC.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.Utc;
            else if (radioTimestampRelative.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.Relative;
            else if (radioTimestampNone.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.None;

            return options;
        }

        public void SetImage(string imageUri, bool isGif)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(imageUri, UriKind.Relative);
            image.EndInit();
            if (isGif)
            {
                ImageBehavior.SetAnimatedSource(statusImage, image);
            }
            else
            {
                ImageBehavior.SetAnimatedSource(statusImage, null);
                statusImage.Source = image;
            }
        }

        private void btnDonate_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.buymeacoffee.com/lay295") { UseShellExecute = true });
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new WindowSettings
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            settings.ShowDialog();
            btnDonate.Visibility = Settings.Default.HideDonation ? Visibility.Collapsed : Visibility.Visible;
            statusImage.Visibility = Settings.Default.ReduceMotion ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            btnDonate.Visibility = Settings.Default.HideDonation ? Visibility.Collapsed : Visibility.Visible;
            statusImage.Visibility = Settings.Default.ReduceMotion ? Visibility.Collapsed : Visibility.Visible;
        }

        private void checkEmbedMissing_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.ChatEmbedMissing = true;
                Settings.Default.ChatReplaceEmbeds = false;
                Settings.Default.Save();
                checkReplaceEmbeds.IsChecked = false;
                checkBttvEmbed.IsEnabled = true;
                checkFfzEmbed.IsEnabled = true;
                checkStvEmbed.IsEnabled = true;
            }
        }

        private void checkEmbedMissing_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.ChatEmbedMissing = false;
                Settings.Default.Save();
                checkBttvEmbed.IsEnabled = false;
                checkFfzEmbed.IsEnabled = false;
                checkStvEmbed.IsEnabled = false;
            }
        }

        private void checkReplaceEmbeds_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.ChatEmbedMissing = false;
                Settings.Default.ChatReplaceEmbeds = true;
                Settings.Default.Save();
                checkEmbedMissing.IsChecked = false;
                checkBttvEmbed.IsEnabled = true;
                checkFfzEmbed.IsEnabled = true;
                checkStvEmbed.IsEnabled = true;
            }
        }

        private void checkReplaceEmbeds_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.ChatReplaceEmbeds = false;
                Settings.Default.Save();
                checkBttvEmbed.IsEnabled = false;
                checkFfzEmbed.IsEnabled = false;
                checkStvEmbed.IsEnabled = false;
            }
        }

        private void checkBttvEmbed_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.BTTVEmotes = true;
                Settings.Default.Save();
            }
        }

        private void checkBttvEmbed_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.BTTVEmotes = false;
                Settings.Default.Save();
            }
        }

        private void checkFfzEmbed_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.FFZEmotes = true;
                Settings.Default.Save();
            }
        }

        private void checkFfzEmbed_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.FFZEmotes = false;
                Settings.Default.Save();
            }
        }

        private void checkStvEmbed_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.STVEmotes = true;
                Settings.Default.Save();
            }
        }

        private void checkStvEmbed_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                Settings.Default.STVEmotes = false;
                Settings.Default.Save();
            }
        }

        private async void SplitBtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (((HandyControl.Controls.SplitButton)sender).IsDropDownOpen)
            {
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                FileName = FilenameService.GetFilename(
                    Settings.Default.TemplateChat,
                    taskData.Title,
                    taskData.Id,
                    taskData.Time,
                    taskData.StreamerName,
                    taskData.StreamerId,
                    taskData.OutputTrimStartTime,
                    taskData.OutputTrimEndTime,
                    taskData.Length,
                    taskData.Views,
                    taskData.Game,
                    taskData.ClipperName,
                    taskData.ClipperId)
            };

            if (radioJson.IsChecked == true)
            {
                if (radioCompressionNone.IsChecked == true)
                {
                    saveFileDialog.Filter = "JSON Files | *.json";
                    saveFileDialog.FileName += ".json";
                }
                else if (radioCompressionGzip.IsChecked == true)
                {
                    saveFileDialog.Filter = "GZip JSON Files | *.json.gz";
                    saveFileDialog.FileName += ".json.gz";
                }
            }
            else if (radioHTML.IsChecked == true)
            {
                saveFileDialog.Filter = "HTML Files | *.html";
                saveFileDialog.FileName += ".html";
            }
            else if (radioText.IsChecked == true)
            {
                saveFileDialog.Filter = "TXT Files | *.txt";
                saveFileDialog.FileName += ".txt";
            }

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            updateTask.UpdateOptions = GetOptions(saveFileDialog.FileName);

            btnBrowse.IsEnabled = false;
            SetEnabled(false);
            UpdateActionButtons(true);

            updateTask.Begin((LogLevel)Settings.Default.LogLevels, AppendLog);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                updateTask.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        private void radioJson_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                timeText.Visibility = Visibility.Collapsed;
                timeOptions.Visibility = Visibility.Collapsed;
                stackEmbedText.Visibility = Visibility.Visible;
                stackEmbedChecks.Visibility = Visibility.Visible;
                compressionText.Visibility = Visibility.Visible;
                compressionOptions.Visibility = Visibility.Visible;
                textTrim.Margin = new Thickness(0, 10, 0, 33);

                Settings.Default.ChatDownloadType = (int)ChatFormat.Json;
                Settings.Default.Save();
            }
        }

        private void radioHTML_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                timeText.Visibility = Visibility.Collapsed;
                timeOptions.Visibility = Visibility.Collapsed;
                stackEmbedText.Visibility = Visibility.Visible;
                stackEmbedChecks.Visibility = Visibility.Visible;
                compressionText.Visibility = Visibility.Collapsed;
                compressionOptions.Visibility = Visibility.Collapsed;
                textTrim.Margin = new Thickness(0, 17, 0, 33);

                Settings.Default.ChatDownloadType = (int)ChatFormat.Html;
                Settings.Default.Save();
            }
        }

        private void radioText_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                timeText.Visibility = Visibility.Visible;
                timeOptions.Visibility = Visibility.Visible;
                stackEmbedText.Visibility = Visibility.Collapsed;
                stackEmbedChecks.Visibility = Visibility.Collapsed;
                compressionText.Visibility = Visibility.Collapsed;
                compressionOptions.Visibility = Visibility.Collapsed;
                textTrim.Margin = new Thickness(0, 10, 0, 0);

                Settings.Default.ChatDownloadType = (int)ChatFormat.Text;
                Settings.Default.Save();
            }
        }

        private void checkStart_OnCheckStateChanged(object sender, RoutedEventArgs e)
        {
            SetEnabledTrimStart(checkStart.IsChecked.GetValueOrDefault());
        }

        private void checkEnd_OnCheckStateChanged(object sender, RoutedEventArgs e)
        {
            SetEnabledTrimEnd(checkEnd.IsChecked.GetValueOrDefault());
        }

        private void MenuItemEnqueue_Click(object sender, RoutedEventArgs e)
        {
            var queueOptions = new WindowQueueOptions([taskData], forceChatUpdate: true)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            queueOptions.ShowDialog();
        }

        private void RadioCompressionNone_OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.ChatJsonCompression = (int)ChatCompression.None;
            Settings.Default.Save();
        }

        private void RadioCompressionGzip_OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.ChatJsonCompression = (int)ChatCompression.Gzip;
            Settings.Default.Save();
        }

        private void RadioTimestampUTC_OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.ChatTextTimestampStyle = (int)TimestampFormat.Utc;
            Settings.Default.Save();
        }

        private void RadioTimestampRelative_OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.ChatTextTimestampStyle = (int)TimestampFormat.Relative;
            Settings.Default.Save();
        }

        private void RadioTimestampNone_OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            Settings.Default.ChatTextTimestampStyle = (int)TimestampFormat.None;
            Settings.Default.Save();
        }

        private void NumTrim_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            taskData.TrimStartTime = new((int)numStartHour.Value, (int)numStartMinute.Value, (int)numStartSecond.Value);
            taskData.TrimEndTime = new((int)numEndHour.Value, (int)numEndMinute.Value, (int)numEndSecond.Value);
        }
    }
}