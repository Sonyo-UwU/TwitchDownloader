using JetBrains.Annotations;
using System.Windows.Media;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Chat;
using TwitchDownloaderWPF.Properties;
using TwitchDownloaderWPF.Services;

namespace TwitchDownloaderWPF.TwitchTasks
{
    public class TaskData
    {
        public static async Task<TaskData> FromVideoIdAsync(string id)
        {
            if (id.All(char.IsDigit))
            {
                return await FromVodIdAsync(long.Parse(id));
            }
            else
            {
                return await FromClipIdAsync(id);
            }
        }

        public static async Task<TaskData> FromVodIdAsync(long id)
        {
            var res = await TwitchHelper.GetVideoInfo(id);
            var videoInfo = res.data.video;

            var thumbUrl = videoInfo.thumbnailURLs.FirstOrDefault();
            if (!ThumbnailService.TryGetThumb(thumbUrl, out var thumbnail))
            {
                _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out thumbnail);
            }

            return new TaskData
            {
                Id = id.ToString(),
                Thumbnail = thumbnail,
                Title = videoInfo.title,
                StreamerName = videoInfo.owner?.displayName ?? Translations.Strings.UnknownUser,
                StreamerId = videoInfo.owner?.id,
                Time = Settings.Default.UTCVideoTime ? videoInfo.createdAt : videoInfo.createdAt.ToLocalTime(),
                Views = videoInfo.viewCount,
                Game = videoInfo.game?.displayName ?? Translations.Strings.UnknownGame,
                Length = TimeSpan.FromSeconds(videoInfo.lengthSeconds)
            };
        }

        public static async Task<TaskData> FromClipIdAsync(string id)
        {
            var res = await TwitchHelper.GetClipInfo(id);
            var clipInfo = res.data.clip;

            var thumbUrl = clipInfo.thumbnailURL;
            if (!ThumbnailService.TryGetThumb(thumbUrl, out var thumbnail))
            {
                _ = ThumbnailService.TryGetThumb(ThumbnailService.THUMBNAIL_MISSING_URL, out thumbnail);
            }

            return new TaskData
            {
                Id = id,
                Thumbnail = thumbnail,
                Title = clipInfo.title,
                StreamerName = clipInfo.broadcaster?.displayName ?? Translations.Strings.UnknownUser,
                StreamerId = clipInfo.broadcaster?.id,
                ClipperName = clipInfo.curator?.displayName ?? Translations.Strings.UnknownUser,
                ClipperId = clipInfo.curator?.id,
                Time = Settings.Default.UTCVideoTime ? clipInfo.createdAt : clipInfo.createdAt.ToLocalTime(),
                Views = clipInfo.viewCount,
                Game = clipInfo.game?.displayName ?? Translations.Strings.UnknownGame,
                Length = TimeSpan.FromSeconds(clipInfo.durationSeconds)
            };
        }
        public static async Task<TaskData> FromJsonFileAsync(string filePath)
        {
            // Get first comment to update missing values
            var chatRoot = await ChatJson.DeserializeAsync(filePath, true, true, false);

            return new TaskData
            {
                FilePath = filePath,
                Id = chatRoot.video.id,
                Thumbnail = ThumbnailService.GetThumb(ThumbnailService.THUMBNAIL_MISSING_URL),
                Title = chatRoot.video.title ?? Translations.Strings.Unknown,
                StreamerName = chatRoot.streamer?.name ?? Translations.Strings.UnknownUser,
                StreamerId = chatRoot.streamer?.id.ToString(),
                ClipperName = chatRoot.clipper?.name ?? (chatRoot.clipper?.id is null ? "null" : Translations.Strings.UnknownUser),
                ClipperId = chatRoot.clipper?.id.ToString(),
                Time = Settings.Default.UTCVideoTime ? chatRoot.video.created_at : chatRoot.video.created_at.ToLocalTime(),
                Views = chatRoot.video.viewCount,
                Game = chatRoot.video.game ?? Translations.Strings.UnknownGame,
                Length = TimeSpan.FromSeconds(Math.Max(0.0, chatRoot.video.length)),
                TrimStart = chatRoot.video.start > 0,
                TrimEnd = chatRoot.video.end != chatRoot.video.length,
                TrimStartTime = TimeSpan.FromSeconds(Math.Max(0.0, chatRoot.video.start)),
                TrimEndTime = TimeSpan.FromSeconds(Math.Max(0.0, chatRoot.video.end))
            };
        }

        public string FilePath { get; set; } = null;
        public bool IsDownload => FilePath is null;
        public string Id { get; set; }
        public string StreamerName { get; set; }
        public string StreamerId { get; set; }
        public string ClipperName { get; set; }
        public string ClipperId { get; set; }
        public string Title { get; set; }
        public ImageSource Thumbnail { get; set; }
        public DateTime Time { get; set; }
        public TimeSpan Length { get; set; }
        public bool TrimStart { get; set; }
        public TimeSpan TrimStartTime { get; set; }
        public TimeSpan OutputTrimStartTime => TrimStart ? TrimStartTime : TimeSpan.Zero;
        public bool TrimEnd { get; set; }
        public TimeSpan RelativeTrimEndTime { get; set; }
        public TimeSpan TrimEndTime { get => Length - RelativeTrimEndTime; set => RelativeTrimEndTime = Length - value; }
        public TimeSpan OutputTrimEndTime => TrimEnd ? TrimEndTime : Length;
        public int Views { get; set; }
        public string Game { get; set; }

        [UsedImplicitly(Reason = "Used by PageQueue bindings")]
        public string LengthFormatted
        {
            get
            {
                if ((int)Length.TotalHours > 0)
                {
                    return $"{(int)Length.TotalHours}:{Length.Minutes:D2}:{Length.Seconds:D2}";
                }

                if ((int)Length.TotalMinutes > 0)
                {
                    return $"{Length.Minutes:D2}:{Length.Seconds:D2}";
                }

                return $"{Length.Seconds:D1}s";
            }
        }
    }
}
