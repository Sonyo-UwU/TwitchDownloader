using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Chat;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.Tools;
using TwitchDownloaderCore.TwitchObjects;
using TwitchDownloaderWPF.Models;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Utils;
using WpfAnimatedGif;

namespace TwitchDownloaderWPF
{
    /// <summary>
    /// Interaction logic for PageVodMerge.xaml
    /// </summary>
    public partial class PageVodMerge : Page
    {
        [DebuggerDisplay("{DisplayName}")]
        private readonly struct ChatInputInfo()
        {
            public readonly required string FileName { get; init; }
            public readonly required ChatRoot ChatInfo { get; init; }

            public readonly string DisplayName => Path.GetFileNameWithoutExtension(FileName);
            public readonly string DisplayTime => ChatInfo.video.length <= 0 ? Translations.Strings.UnknownVideoLength : TimeSpan.FromSeconds(ChatInfo.video.length).ToString("c");
        }

        [DebuggerDisplay("{DisplayName}")]
        private readonly struct VideoInputInfo()
        {
            public readonly required string FileName { get; init; }
            public readonly required double VideoLength { get; init; }

            public readonly string DisplayName => Path.GetFileNameWithoutExtension(FileName);
            public readonly string DisplayTime => VideoLength <= 0 ? Translations.Strings.UnknownVideoLength : TimeSpan.FromSeconds(VideoLength).ToString("c");
        }

        public ChatRoot ChatInfo => InputsChat[0].ChatInfo;
        public string VideoFileName => InputsVideo[0].FileName;
        public bool QueueChatMode = true;

        private readonly ObservableCollection<ChatInputInfo> InputsChat = [];
        private readonly ObservableCollection<VideoInputInfo> InputsVideo = [];
        private CancellationTokenSource _cancellationTokenSource;

        public PageVodMerge()
        {
            InitializeComponent();
            inputListChat.ItemsSource = InputsChat;
            inputListVideo.ItemsSource = InputsVideo;
        }

        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "JSON & Video files|*.json;*.json.gz;*.mp4;*.ts;*.mov;*.webm;*.mkv|All Files (*.*)|*.*",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            SetChatEnabled(false);
            SetVideoEnabled(false);
            SetActionButtonsEnabled(false);

            if (!openFileDialog.FileNames.Any(x => Path.GetExtension(x)!.ToLower() is ".json" or ".gz"))
                TabVideo.IsSelected = true;
            else if (!openFileDialog.FileNames.Any(x => Path.GetExtension(x)!.ToLower() is not ".json" and not ".gz"))
                TabChat.IsSelected = true;

            foreach (string file in openFileDialog.FileNames)
            {
                if (Path.GetExtension(file)!.ToLower() is ".json" or ".gz")
                {
                    try
                    {
                        var chatRoot = await ChatJson.DeserializeAsync(file, true, true, false, CancellationToken.None);
                        InputsChat.Add(new ChatInputInfo { FileName = file, ChatInfo = chatRoot });
                    }
                    catch (Exception ex)
                    {
                        AppendLog(Translations.Strings.ErrorLog + ex.Message);
                        if (Settings.Default.VerboseErrors)
                        {
                            MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    TimeSpan duration = TimeSpan.Zero;
                    await FfprobeRunner.Run("ffprobe", file, "format=duration", (sender, e) =>
                    {
                        if (e.Data is null)
                            return;

                        if (e.Data.StartsWith("duration="))
                        {
                            _ = TimeSpan.TryParse(e.Data.AsSpan("duration=".Length), out duration);
                        }
                    }, CancellationToken.None);

                    InputsVideo.Add(new VideoInputInfo() { FileName = file, VideoLength = duration.TotalSeconds });
                }
            }

            DisplayTotalLength();
            SetChatEnabled(InputsChat.Count > 0);
            SetVideoEnabled(InputsVideo.Count > 0);
            SetActionButtonsEnabled(TabChat.IsSelected ? InputsChat.Count >= 2 : InputsVideo.Count >= 2);
        }

        private void DisplayTotalLength()
        {
            TimeSpan length = TimeSpan.Zero;
            foreach (ChatInputInfo input in InputsChat)
            {
                length += TimeSpan.FromSeconds(input.ChatInfo.video.length);
            }
            labelLengthChat.Text = length.ToString("c");

            length = TimeSpan.Zero;
            foreach (VideoInputInfo input in InputsVideo)
            {
                length += TimeSpan.FromSeconds(input.VideoLength);
            }
            labelLengthVideo.Text = length.ToString("c");
        }

        private void UpdateActionButtons(bool isMerging)
        {
            if (isMerging)
            {
                SplitBtnMerge.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Visible;
                return;
            }
            SplitBtnMerge.Visibility = Visibility.Visible;
            BtnCancel.Visibility = Visibility.Collapsed;
        }

        private void Page_Initialized(object sender, EventArgs e)
        {
            SetChatEnabled(false);
            SetVideoEnabled(false);
            SetActionButtonsEnabled(false);
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

        private void SetActionButtonsEnabled(bool isEnabled)
        {
            SplitBtnMerge.IsEnabled = isEnabled;
            MenuItemEnqueue.IsEnabled = isEnabled;
        }

        private void SetChatEnabled(bool isEnabled)
        {
            radioTimestampRelative.IsEnabled = isEnabled;
            radioTimestampUTC.IsEnabled = isEnabled;
            radioTimestampNone.IsEnabled = isEnabled;
            radioCompressionNone.IsEnabled = isEnabled;
            radioCompressionGzip.IsEnabled = isEnabled;
            radioJson.IsEnabled = isEnabled;
            radioText.IsEnabled = isEnabled;
            radioHTML.IsEnabled = isEnabled;
            numDelayChat.IsEnabled = isEnabled;
            inputListChat.IsEnabled = isEnabled;
        }

        private void SetVideoEnabled(bool isEnabled)
        {
            numDelayVideo.IsEnabled = isEnabled;
            inputListVideo.IsEnabled = isEnabled;
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

        public ChatMergeOptions GetChatOptions(string outputFile)
        {
            ChatMergeOptions options = new()
            {
                InputFiles = [.. InputsChat.Select(x => x.FileName)],
                OutputFile = outputFile,
                DelayBetweenParts = numDelayChat.Value,
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


            if (radioTimestampUTC.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.Utc;
            else if (radioTimestampRelative.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.Relative;
            else if (radioTimestampNone.IsChecked.GetValueOrDefault())
                options.TextTimestampFormat = TimestampFormat.None;

            return options;
        }

        public VideoMergeOptions GetVideoOptions(string outputFile)
        {
            VideoMergeOptions options = new()
            {
                InputFiles = [.. InputsVideo.Select(x => x.FileName)],
                OutputFile = outputFile,
                DelayBetweenParts = numDelayVideo.Value,
                FfmpegPath = "ffmpeg",
                FfprobePath = "ffprobe"
            };

            return options;
        }

        private string GetDefaultOutputFilename()
        {
            var firstChatInfo = InputsChat[0].ChatInfo;
            return FilenameService.GetFilename(
                Settings.Default.TemplateChat,
                firstChatInfo.video.title,
                firstChatInfo.video.id ?? firstChatInfo.comments.FirstOrDefault()?.content_id ?? "-1",
                firstChatInfo.video.created_at,
                firstChatInfo.streamer.name,
                firstChatInfo.streamer.id.ToString(),
                TimeSpan.FromSeconds(double.Max(0, firstChatInfo.video.start)),
                TimeSpan.FromSeconds(firstChatInfo.video.length),
                TimeSpan.FromSeconds(firstChatInfo.video.length),
                firstChatInfo.video.viewCount,
                firstChatInfo.video.game,
                firstChatInfo.clipper?.name,
                firstChatInfo.clipper?.id.ToString());
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

        private async Task MergeChats()
        {
            SaveFileDialog saveFileDialog = new()
            {
                FileName = GetDefaultOutputFilename()
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

            try
            {
                ChatMergeOptions mergeOptions = GetChatOptions(saveFileDialog.FileName);

                var mergeProgress = new WpfTaskProgress((LogLevel)Settings.Default.LogLevels, SetPercent, SetStatus, AppendLog);
                var currentMerge = new ChatMerger(mergeOptions, mergeProgress);
                try
                {
                    await currentMerge.ValidateInputsAsync(CancellationToken.None);
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

                btnBrowse.IsEnabled = false;
                SetChatEnabled(false);
                SetVideoEnabled(false);
                SetActionButtonsEnabled(false);

                SetImage("Images/ppOverheat.gif", true);
                statusMessage.Text = Translations.Strings.StatusUpdating;
                _cancellationTokenSource = new CancellationTokenSource();
                UpdateActionButtons(true);

                try
                {
                    await currentMerge.MergeAsync(_cancellationTokenSource.Token);
                    InputsChat.Clear();
                    mergeProgress.SetStatus(Translations.Strings.StatusDone);
                    SetImage("Images/ppHop.gif", true);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && _cancellationTokenSource.IsCancellationRequested)
                {
                    mergeProgress.SetStatus(Translations.Strings.StatusCanceled);
                    SetImage("Images/ppHop.gif", true);
                }
                catch (Exception ex)
                {
                    mergeProgress.SetStatus(Translations.Strings.StatusError);
                    SetImage("Images/peepoSad.png", false);
                    AppendLog(Translations.Strings.ErrorLog + ex.Message);
                    if (Settings.Default.VerboseErrors)
                    {
                        MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                btnBrowse.IsEnabled = true;
                mergeProgress.ReportProgress(0);
                _cancellationTokenSource.Dispose();
                UpdateActionButtons(false);
                SetChatEnabled(InputsChat.Count > 0);
                SetVideoEnabled(InputsVideo.Count > 0);
                SetActionButtonsEnabled(TabChat.IsSelected ? InputsChat.Count >= 2 : InputsVideo.Count >= 2);

                GC.Collect();
            }
            catch (Exception ex)
            {
                AppendLog(Translations.Strings.ErrorLog + ex.Message);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task MergeVideos()
        {
            SaveFileDialog saveFileDialog = new()
            {
                FileName = InputsVideo[0].FileName
            };

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                VideoMergeOptions mergeOptions = GetVideoOptions(saveFileDialog.FileName);

                var mergeProgress = new WpfTaskProgress((LogLevel)Settings.Default.LogLevels, SetPercent, SetStatus, AppendLog);
                var currentMerge = new VideoMerger(mergeOptions, mergeProgress);
                
                btnBrowse.IsEnabled = false;
                SetChatEnabled(false);
                SetVideoEnabled(false);
                SetActionButtonsEnabled(false);

                SetImage("Images/ppOverheat.gif", true);
                statusMessage.Text = Translations.Strings.StatusUpdating;
                _cancellationTokenSource = new CancellationTokenSource();
                UpdateActionButtons(true);

                try
                {
                    await currentMerge.MergeAsync(_cancellationTokenSource.Token);
                    InputsVideo.Clear();
                    mergeProgress.SetStatus(Translations.Strings.StatusDone);
                    SetImage("Images/ppHop.gif", true);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && _cancellationTokenSource.IsCancellationRequested)
                {
                    mergeProgress.SetStatus(Translations.Strings.StatusCanceled);
                    SetImage("Images/ppHop.gif", true);
                }
                catch (Exception ex)
                {
                    mergeProgress.SetStatus(Translations.Strings.StatusError);
                    SetImage("Images/peepoSad.png", false);
                    AppendLog(Translations.Strings.ErrorLog + ex.Message);
                    if (Settings.Default.VerboseErrors)
                    {
                        MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                btnBrowse.IsEnabled = true;
                mergeProgress.ReportProgress(0);
                _cancellationTokenSource.Dispose();
                UpdateActionButtons(false);
                SetChatEnabled(InputsChat.Count > 0);
                SetVideoEnabled(InputsVideo.Count > 0);
                SetActionButtonsEnabled(TabChat.IsSelected ? InputsChat.Count >= 2 : InputsVideo.Count >= 2);

                GC.Collect();
            }
            catch (Exception ex)
            {
                AppendLog(Translations.Strings.ErrorLog + ex.Message);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

        private async void SplitBtnMerge_Click(object sender, RoutedEventArgs e)
        {
            if (((HandyControl.Controls.SplitButton)sender).IsDropDownOpen)
            {
                return;
            }

            if (TabChat.IsSelected)
            {
                await MergeChats();
            }
            else
            {
                await MergeVideos();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            statusMessage.Text = Translations.Strings.StatusCanceling;
            SetImage("Images/ppStretch.gif", true);
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch { }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabChat.IsSelected)
            {
                SplitBtnMerge.Content = Translations.Strings.MergeChats;
                SetActionButtonsEnabled(InputsChat.Count >= 2);
            }
            else
            {
                SplitBtnMerge.Content = Translations.Strings.MergeVideos;
                SetActionButtonsEnabled(InputsVideo.Count >= 2);
            }
        }

        private void radioJson_Checked(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                timeText.Visibility = Visibility.Collapsed;
                timeOptions.Visibility = Visibility.Collapsed;
                compressionText.Visibility = Visibility.Visible;
                compressionOptions.Visibility = Visibility.Visible;

                Settings.Default.ChatDownloadType = (int)ChatFormat.Json;
                Settings.Default.Save();
            }
        }

        private void radioHTML_Checked(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                timeText.Visibility = Visibility.Collapsed;
                timeOptions.Visibility = Visibility.Collapsed;
                compressionText.Visibility = Visibility.Collapsed;
                compressionOptions.Visibility = Visibility.Collapsed;

                Settings.Default.ChatDownloadType = (int)ChatFormat.Html;
                Settings.Default.Save();
            }
        }

        private void radioText_Checked(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                timeText.Visibility = Visibility.Visible;
                timeOptions.Visibility = Visibility.Visible;
                compressionText.Visibility = Visibility.Collapsed;
                compressionOptions.Visibility = Visibility.Collapsed;

                Settings.Default.ChatDownloadType = (int)ChatFormat.Text;
                Settings.Default.Save();
            }
        }

        private void MenuItemEnqueue_Click(object sender, RoutedEventArgs e)
        {
            QueueChatMode = TabChat.IsSelected;
            var queueOptions = new WindowQueueOptions(this)
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

        private async void BtnRemoveChatInput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ChatInputInfo file })
            {
                return;
            }

            var index = InputsChat.IndexOf(file);
            if (index < 0)
                return;

            InputsChat.RemoveAt(index);
            DisplayTotalLength();

            SetChatEnabled(InputsChat.Count > 0);
            if (TabChat.IsSelected)
                SetActionButtonsEnabled(InputsChat.Count >= 2);
        }

        private async void BtnRemoveVideoInput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: VideoInputInfo file })
            {
                return;
            }

            var index = InputsVideo.IndexOf(file);
            if (index < 0)
                return;

            InputsVideo.RemoveAt(index);
            DisplayTotalLength();

            SetVideoEnabled(InputsVideo.Count > 0);
            if (TabVideo.IsSelected)
                SetActionButtonsEnabled(InputsVideo.Count >= 2);
        }

        private async void BtnMoveChatInputUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ChatInputInfo file })
            {
                return;
            }

            var index = InputsChat.IndexOf(file);
            if (index < 1)
                return;

            InputsChat.Move(index, index - 1);
        }

        private async void BtnMoveVideoInputUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: VideoInputInfo file })
            {
                return;
            }

            var index = InputsVideo.IndexOf(file);
            if (index < 1)
                return;

            InputsVideo.Move(index, index - 1);
        }

        private async void BtnMoveChatInputDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ChatInputInfo file })
            {
                return;
            }

            var index = InputsChat.IndexOf(file);
            if (index == -1 || index == InputsChat.Count - 1)
                return;

            InputsChat.Move(index, index + 1);
        }

        private async void BtnMoveVideoInputDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: VideoInputInfo file })
            {
                return;
            }

            var index = InputsVideo.IndexOf(file);
            if (index == -1 || index == InputsVideo.Count - 1)
                return;

            InputsVideo.Move(index, index + 1);
        }

        private void numDelayChat_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (IsInitialized)
            {
                numDelayVideo.Value = numDelayChat.Value;
            }
        }

        private void numDelayVideo_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (IsInitialized)
            {
                numDelayChat.Value = numDelayVideo.Value;
            }
        }
    }
}