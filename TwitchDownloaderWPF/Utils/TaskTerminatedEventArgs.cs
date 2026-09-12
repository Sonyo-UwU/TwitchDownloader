namespace TwitchDownloaderWPF.Utils
{
    public sealed class TaskTerminatedEventArgs(bool isSuccessful)
    {
        public bool IsSuccessful { get; } = isSuccessful;
    }
}
