using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Models.Interfaces;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Services;
using TwitchDownloaderCore.Tools;
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
    /// Interaction logic for PageClipDownload.xaml
    /// </summary>
    public partial class PageClipDownload : Page
    {
        private TaskData taskData;
        private CancellationTokenSource _cancellationTokenSource;

        public PageClipDownload()
        {
            InitializeComponent();
        }

        private async void btnGetInfo_Click(object sender, RoutedEventArgs e)
        {
            await GetClipInfo();
        }

        private async Task GetClipInfo()
        {
            var clipId = ValidateUrl(textUrl.Text.Trim());
            if (string.IsNullOrWhiteSpace(clipId))
            {
                MessageBox.Show(Application.Current.MainWindow!, Translations.Strings.InvalidClipLinkIdMessage.Replace(@"\n", Environment.NewLine), Translations.Strings.InvalidClipLinkId, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                btnGetInfo.IsEnabled = false;
                var clipRenderStatus = await TwitchHelper.GetShareClipRenderStatus(clipId);
                var clip = clipRenderStatus.data.clip;

                var thumbUrl = clip.thumbnailURL;
                if (!ThumbnailService.TryGetThumb(thumbUrl, out var image))
                {
                    AppendLog(Translations.Strings.ErrorLog + Translations.Strings.UnableToFindThumbnail);
                    _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out image);
                }
                imgThumbnail.Source = image;

                taskData = new()
                {
                    Id = clipId,
                    Thumbnail = image,
                    Title = clip.title,
                    StreamerName = clip.broadcaster?.displayName ?? Translations.Strings.UnknownUser,
                    StreamerId = clip.broadcaster?.id,
                    ClipperName = clip.curator?.displayName ?? Translations.Strings.UnknownUser,
                    ClipperId = clip.curator?.id,
                    Time = Settings.Default.UTCVideoTime ? clip.createdAt : clip.createdAt.ToLocalTime(),
                    Views = clip.viewCount,
                    Game = clip.game?.displayName ?? Translations.Strings.UnknownGame,
                    Length = TimeSpan.FromSeconds(clip.durationSeconds)
                };

                textStreamer.Text = taskData.StreamerName;
                textCreatedAt.Text = taskData.Time.ToString(CultureInfo.CurrentCulture);
                textTitle.Text = taskData.Title;
                labelLength.Text = taskData.Length.ToString("c");

                comboQuality.Items.Clear();
                var clipQualities = VideoQualities.FromClip(clip);
                foreach (var quality in clipQualities.Qualities)
                {
                    comboQuality.Items.Add(quality);
                }

                comboQuality.SelectedIndex = 0;
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Application.Current.MainWindow!, Translations.Strings.UnableToGetClipInfo, Translations.Strings.UnableToGetInfo, MessageBoxButton.OK, MessageBoxImage.Error);
                AppendLog(Translations.Strings.ErrorLog + ex);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            btnGetInfo.IsEnabled = true;
        }

        private void UpdateActionButtons(bool isDownloading)
        {
            if (isDownloading)
            {
                SplitBtnDownload.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Visible;
                return;
            }
            SplitBtnDownload.Visibility = Visibility.Visible;
            BtnCancel.Visibility = Visibility.Collapsed;
        }

        private static string ValidateUrl(string text)
        {
            var clipIdMatch = IdParse.MatchClipId(text);
            return clipIdMatch is { Success: true }
                ? clipIdMatch.Value
                : null;
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

        private void Page_Initialized(object sender, EventArgs e)
        {
            SetEnabled(false);
            CheckMetadata.IsChecked = Settings.Default.EncodeClipMetadata;
        }

        private void SetEnabled(bool enabled)
        {
            comboQuality.IsEnabled = enabled;
            SplitBtnDownload.IsEnabled = enabled;
            CheckMetadata.IsEnabled = enabled;
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

        private async void SplitBtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (((HandyControl.Controls.SplitButton)sender).IsDropDownOpen)
            {
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "MP4 Files | *.mp4",
                FileName = FilenameService.GetFilename(
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
                    taskData.ClipperId) + ".mp4"
            };
            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            SetEnabled(false);

            ClipDownloadOptions downloadOptions = GetOptions(saveFileDialog.FileName);
            _cancellationTokenSource = new CancellationTokenSource();

            var downloadProgress = new WpfTaskProgress((LogLevel)Settings.Default.LogLevels, SetPercent, SetStatus, AppendLog);
            var currentDownload = new ClipDownloader(downloadOptions, downloadProgress);

            SetImage("Images/ppOverheat.gif", true);
            statusMessage.Text = Translations.Strings.StatusDownloading;
            UpdateActionButtons(true);
            try
            {
                await currentDownload.DownloadAsync(_cancellationTokenSource.Token);
                downloadProgress.SetStatus(Translations.Strings.StatusDone);
                SetImage("Images/ppHop.gif", true);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && _cancellationTokenSource.IsCancellationRequested)
            {
                downloadProgress.SetStatus(Translations.Strings.StatusCanceled);
                SetImage("Images/ppHop.gif", true);
            }
            catch (Exception ex)
            {
                downloadProgress.SetStatus(Translations.Strings.StatusError);
                SetImage("Images/peepoSad.png", false);
                AppendLog(Translations.Strings.ErrorLog + ex.Message);
                if (Settings.Default.VerboseErrors)
                {
                    MessageBox.Show(Application.Current.MainWindow!, ex.ToString(), Translations.Strings.VerboseErrorOutput, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            btnGetInfo.IsEnabled = true;
            downloadProgress.ReportProgress(0);
            _cancellationTokenSource.Dispose();
            UpdateActionButtons(false);
        }

        private ClipDownloadOptions GetOptions(string fileName)
        {
            return new ClipDownloadOptions
            {
                Filename = fileName,
                Id = taskData.Id,
                Quality = comboQuality.Text,
                ThrottleKib = Settings.Default.DownloadThrottleEnabled
                    ? Settings.Default.MaximumBandwidthKib
                    : -1,
                TempFolder = Settings.Default.TempPath,
                EncodeMetadata = CheckMetadata.IsChecked!.Value,
                FfmpegPath = "ffmpeg",
            };
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

        private void MenuItemEnqueue_Click(object sender, RoutedEventArgs e)
        {
            var queueOptions = new WindowQueueOptions([taskData],
                forceVideoDownload: true,
                videoQualities: comboQuality.Items.Cast<IVideoQuality<ShareClipRenderStatusVideoQuality>>().Select(q => q.Name).ToArray(),
                selectedQuality: comboQuality.SelectedIndex)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            queueOptions.ShowDialog();
        }

        private async void TextUrl_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await GetClipInfo();
                e.Handled = true;
            }
        }

        private void CheckMetadata_OnCheckStateChanged(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                Settings.Default.EncodeClipMetadata = CheckMetadata.IsChecked!.Value;
                Settings.Default.Save();
            }
        }
    }
}