using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Chat;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.TwitchObjects;
using TwitchDownloaderCore.TwitchObjects.Gql;
using TwitchDownloaderWPF.Models;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Services;
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
        private readonly struct InputInfo()
        {
            public readonly required string FileName { get; init; }
            public readonly required ChatRoot ChatInfo { get; init; }

            public readonly string DisplayName => Path.GetFileNameWithoutExtension(FileName);
            public readonly string DisplayTime => ChatInfo.video.length <= 0 ? Translations.Strings.UnknownVideoLength : TimeSpan.FromSeconds(ChatInfo.video.length).ToString("c");
        }

        private readonly ObservableCollection<InputInfo> Inputs = [];
        private CancellationTokenSource _cancellationTokenSource;

        public PageVodMerge()
        {
            InitializeComponent();
            inputList.ItemsSource = Inputs;
        }

        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "JSON Files | *.json;*.json.gz",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            SetEnabled(false);
            SetActionButtonsEnabled(false);

            bool displayVideoInfo = Inputs.Count == 0;

            foreach (string file in openFileDialog.FileNames)
            {
                if (Path.GetExtension(file)!.ToLower() is not ".json" and not ".gz")
                {
                    continue;
                }

                try
                {
                    var chatRoot = await ChatJson.DeserializeAsync(file, true, true, false, CancellationToken.None);
                    if (chatRoot.video.length <= 0 && Inputs.Count > 0)
                    {
                        await UpdateChatInfoAsync(chatRoot);
                    }
                    Inputs.Add(new InputInfo { FileName = file, ChatInfo = chatRoot });
                    DisplayTotalLength();
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

            SetEnabled(Inputs.Count > 0);
            SetActionButtonsEnabled(Inputs.Count >= 2);

            if (displayVideoInfo && Inputs.Count > 0)
            {
                await DisplayVideoInfoAsync();
            }
        }

        private async Task UpdateChatInfoAsync(ChatRoot chatRoot)
        {
            if (chatRoot.video.length > 0 && !string.IsNullOrEmpty(chatRoot.video.chapters[0].thumbnailUrl))
                return;

            try
            {
                string videoId = chatRoot.video.id;
                if (videoId.All(char.IsDigit))
                {
                    GqlVideoResponse videoInfo = await TwitchHelper.GetVideoInfo(long.Parse(videoId));
                    if (videoInfo.data.video == null)
                    {
                        AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToGetVideoInfo + ": " + Translations.Strings.VodExpiredOrIdCorrupt);
                    }
                    else
                    {
                        if (chatRoot.video.length <= 0)
                            chatRoot.video.length = videoInfo.data.video.lengthSeconds;
                        if (string.IsNullOrEmpty(chatRoot.video.title))
                            chatRoot.video.title = videoInfo.data.video.title;
                        if (chatRoot.video.created_at == default)
                            chatRoot.video.created_at = videoInfo.data.video.createdAt;
                        if (string.IsNullOrEmpty(chatRoot.streamer.name))
                            chatRoot.streamer.name = videoInfo.data.video.owner.displayName;
                        if (string.IsNullOrEmpty(chatRoot.video.chapters[0].thumbnailUrl))
                            chatRoot.video.chapters[0].thumbnailUrl = videoInfo.data.video.thumbnailURLs.FirstOrDefault();
                    }
                }
                else
                {
                    GqlClipResponse clipInfo = await TwitchHelper.GetClipInfo(videoId);
                    if (clipInfo.data.clip.video == null)
                    {
                        AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToGetVideoInfo + ": " + Translations.Strings.VodExpiredOrIdCorrupt);
                    }
                    else
                    {
                        if (chatRoot.video.length <= 0)
                            chatRoot.video.length = clipInfo.data.clip.durationSeconds;
                        if (string.IsNullOrEmpty(chatRoot.video.title))
                            chatRoot.video.title = clipInfo.data.clip.title;
                        if (chatRoot.video.created_at == default)
                            chatRoot.video.created_at = clipInfo.data.clip.createdAt;
                        if (string.IsNullOrEmpty(chatRoot.streamer.name))
                            chatRoot.streamer.name = clipInfo.data.clip.broadcaster.displayName;
                        if (string.IsNullOrEmpty(chatRoot.video.chapters[0].thumbnailUrl))
                            chatRoot.video.chapters[0].thumbnailUrl = clipInfo.data.clip.thumbnailURL;
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
        }

        private async Task DisplayVideoInfoAsync(ChatRoot chatRoot = null)
        {
            if (chatRoot is null && Inputs.Count == 0)
            {
                textCreatedAt.Text = string.Empty;
                textStreamer.Text = string.Empty;
                textTitle.Text = string.Empty;
                labelLength.Text = string.Empty;
                imgThumbnail.Source = null;
                return;
            }

            chatRoot ??= Inputs[0].ChatInfo;

            SetEnabled(false);
            imgThumbnail.Source = null;

            await UpdateChatInfoAsync(chatRoot);
            if (!ThumbnailService.TryGetThumb(chatRoot.video.chapters[0].thumbnailUrl, out var image))
            {
                ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out image);
            }

            // Finally, display info
            var videoCreatedAt = chatRoot.video.created_at == default
                ? chatRoot.comments[0].created_at - TimeSpan.FromSeconds(chatRoot.comments[0].content_offset_seconds)
                : chatRoot.video.created_at;

            textCreatedAt.Text = Settings.Default.UTCVideoTime ? videoCreatedAt.ToString(CultureInfo.CurrentCulture) : videoCreatedAt.ToLocalTime().ToString(CultureInfo.CurrentCulture);
            textStreamer.Text = chatRoot.streamer.name;
            textTitle.Text = chatRoot.video.title ?? Translations.Strings.Unknown;
            imgThumbnail.Source = image;

            SetEnabled(Inputs.Count > 0);
        }

        private void DisplayTotalLength()
        {
            TimeSpan length = new();
            foreach (InputInfo input in Inputs)
            {
                length += TimeSpan.FromSeconds(input.ChatInfo.video.length);
            }
            labelLength.Text = length.ToString("c");
        }

        private void UpdateActionButtons(bool isUpdating)
        {
            if (isUpdating)
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
            SetEnabled(false);
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

        private void SetEnabled(bool isEnabled)
        {
            radioTimestampRelative.IsEnabled = isEnabled;
            radioTimestampUTC.IsEnabled = isEnabled;
            radioTimestampNone.IsEnabled = isEnabled;
            radioCompressionNone.IsEnabled = isEnabled;
            radioCompressionGzip.IsEnabled = isEnabled;
            radioJson.IsEnabled = isEnabled;
            radioText.IsEnabled = isEnabled;
            radioHTML.IsEnabled = isEnabled;
            numDelay.IsEnabled = isEnabled;
            inputList.IsEnabled = isEnabled;
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

        public ChatMergeOptions GetOptions()
        {
            return GetOptions(GetDefaultOutputFilename());
        }

        public ChatMergeOptions GetOptions(string outputFile)
        {
            ChatMergeOptions options = new()
            {
                InputFiles = [.. Inputs.Select(x => x.FileName)],
                OutputFile = outputFile,
                DelayBetweenParts = numDelay.Value,
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

        public string GetDefaultOutputFilename()
        {
            var firstChatInfo = Inputs[0].ChatInfo;
            return FilenameService.GetFilename(
                Settings.Default.TemplateChat,
                textTitle.Text,
                firstChatInfo.video.id ?? firstChatInfo.comments.FirstOrDefault()?.content_id ?? "-1",
                firstChatInfo.video.created_at,
                textStreamer.Text,
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
                ChatMergeOptions mergeOptions = GetOptions(saveFileDialog.FileName);

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
                SetEnabled(false);
                SetActionButtonsEnabled(false);

                SetImage("Images/ppOverheat.gif", true);
                statusMessage.Text = Translations.Strings.StatusUpdating;
                _cancellationTokenSource = new CancellationTokenSource();
                UpdateActionButtons(true);

                try
                {
                    await currentMerge.MergeAsync(_cancellationTokenSource.Token);
                    Inputs.Clear();
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

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            statusMessage.Text = Translations.Strings.StatusCanceling;
            SetImage("Images/ppStretch.gif", true);
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException) { }
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

        private async void BtnRemoveInput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: InputInfo file })
            {
                return;
            }

            var index = Inputs.IndexOf(file);
            if (index < 0)
                return;

            Inputs.RemoveAt(index);
            DisplayTotalLength();

            SetEnabled(Inputs.Count > 0);
            SetActionButtonsEnabled(Inputs.Count >= 2);

            if (index == 0)
            {
                await DisplayVideoInfoAsync();
            }
        }

        private async void BtnMoveInputUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: InputInfo file })
            {
                return;
            }

            var index = Inputs.IndexOf(file);
            if (index < 1)
                return;

            Inputs.Move(index, index - 1);

            if (index == 1)
            {
                await DisplayVideoInfoAsync();
            }
        }

        private async void BtnMoveInputDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: InputInfo file })
            {
                return;
            }

            var index = Inputs.IndexOf(file);
            if (index == -1 || index == Inputs.Count - 1)
                return;

            Inputs.Move(index, index + 1);

            if (index == 0)
            {
                await DisplayVideoInfoAsync();
            }
        }
    }
}