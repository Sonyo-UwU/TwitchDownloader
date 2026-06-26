using JetBrains.Annotations;
using System.Windows.Media;

namespace TwitchDownloaderWPF.TwitchTasks
{
    public class TaskData
    {
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
