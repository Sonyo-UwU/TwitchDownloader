using System.IO.Compression;
using TwitchDownloaderCore.Chat;
using TwitchDownloaderCore.Interfaces;
using TwitchDownloaderCore.Models;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.TwitchObjects;

namespace TwitchDownloaderCore
{
    public sealed class ChatMerger(ChatMergeOptions mergeOptions, ITaskProgress progress)
    {

        private readonly ChatMergeOptions mergeOptions = mergeOptions;
        private ChatRoot[] InputChatRoots;
        private readonly ITaskProgress _progress = progress;

        public async Task MergeAsync(CancellationToken cancellationToken)
        {
            var outputFileInfo = TwitchHelper.ClaimFile(mergeOptions.OutputFile, mergeOptions.FileCollisionCallback, _progress);
            mergeOptions.OutputFile = outputFileInfo.FullName;

            // Open the destination file so that it exists in the filesystem.
            await using var outputFs = outputFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

            try
            {
                await MergeAsyncImpl(outputFileInfo, outputFs, cancellationToken);
            }
            catch
            {
                await Task.Delay(100, CancellationToken.None);

                TwitchHelper.CleanUpClaimedFile(outputFileInfo, outputFs, _progress);

                throw;
            }
        }

        private async Task MergeAsyncImpl(FileInfo outputFileInfo, FileStream outputFs, CancellationToken cancellationToken)
        {
            if (mergeOptions.InputFiles.Any(file => !Path.GetExtension(file.Replace(".gz", ""))!.Equals(".json", StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException("Only JSON chat files can be used as update input. HTML and Text support may come in the future.");
            }

            var outputChatRoot = InitChatRoot();
            _progress.SetStatus("Combining");

            double timeOffset = 0;
            for (int i = 0; i < InputChatRoots.Length; i++)
            {
                var chatRoot = InputChatRoots[i];
                AppendChatRoot(chatRoot, outputChatRoot, timeOffset);

                timeOffset += chatRoot.video.length;
                if (i < InputChatRoots.Length - 1)
                {
                    timeOffset += mergeOptions.DelayBetweenParts;
                }
            }
            outputChatRoot.video.length = timeOffset;
            outputChatRoot.video.end = outputChatRoot.video.start + timeOffset;

            // If embedded data is empty, set it as null
            if (outputChatRoot.embeddedData.firstParty  .Count == 0 &&
                outputChatRoot.embeddedData.thirdParty  .Count == 0 &&
                outputChatRoot.embeddedData.twitchBadges.Count == 0 &&
                outputChatRoot.embeddedData.twitchBits  .Count == 0)
            {
                outputChatRoot.embeddedData = null;
            }

            _progress.ReportProgress(InputChatRoots.Length / (InputChatRoots.Length + 1));
            await SaveOutput(outputFileInfo, outputFs, outputChatRoot, cancellationToken);
            _progress.ReportProgress(100);
        }

        private ChatRoot InitChatRoot()
        {
            ChatRoot firstChatRoot = InputChatRoots[0];
            ChatRoot chatRoot = new()
            {
                FileInfo = new()
                {
                    Version = ChatRootVersion.CurrentVersion,
                    CreatedAt = DateTime.Now
                },
                streamer = new()
                {
                    name = firstChatRoot.streamer.name,
                    login = firstChatRoot.streamer.login,
                    id = firstChatRoot.streamer.id,
                },
                video = new()
                {
                    title       = firstChatRoot.video.title,
                    description = firstChatRoot.video.description,
                    id          = firstChatRoot.video.id,
                    created_at  = firstChatRoot.video.created_at,
                    start       = firstChatRoot.video.start,
                    end         = firstChatRoot.video.start,
                    length      = 0,
                    viewCount   = 0,
                    game        = firstChatRoot.video.game
                },
                comments = [],
                embeddedData = new()
                // TODO: clipper?
            };

            return chatRoot;
        }

        private static void AppendChatRoot(ChatRoot chatRoot, ChatRoot outputChatRoot, double offset)
        {
            // Video view count
            outputChatRoot.video.viewCount += chatRoot.video.viewCount;

            // Video chapters
            outputChatRoot.video.chapters.AddRange(chatRoot.video.chapters.Select(
                (chapter, i) => new VideoChapter
                {
                    id = chapter.id,
                    startMilliseconds = chapter.startMilliseconds + (int)(offset * 1000),
                    lengthMilliseconds = chapter.lengthMilliseconds,
                    _type = (i == 0 && outputChatRoot.video.chapters.Count > 0) ? "PART_CHANGE" : chapter._type,
                    description = chapter.description,
                    subDescription = chapter.subDescription,
                    thumbnailUrl = chapter.thumbnailUrl,
                    gameId = chapter.gameId,
                    gameDisplayName = chapter.gameDisplayName,
                    gameBoxArtUrl = chapter.gameBoxArtUrl
                }
            ));

            // Comments
            outputChatRoot.comments.AddRange(chatRoot.comments.Select(
                comment =>
                {
                    var copy = comment.Clone();
                    copy.content_offset_seconds += offset;
                    copy.created_at = outputChatRoot.video.created_at + TimeSpan.FromSeconds(copy.content_offset_seconds);
                    return copy;
                }
            ));

            // Embedded data
            if (chatRoot.embeddedData is not null)
            {
                // Not the most optimal, but it's fast enough anyway
                foreach (var emote in chatRoot.embeddedData.firstParty)
                {
                    if (!outputChatRoot.embeddedData.firstParty.Any(e => e.id == emote.id))
                        outputChatRoot.embeddedData.firstParty.Add(emote);
                }
                foreach (var emote in chatRoot.embeddedData.thirdParty)
                {
                    if (!outputChatRoot.embeddedData.thirdParty.Any(e => e.id == emote.id))
                        outputChatRoot.embeddedData.thirdParty.Add(emote);
                }
                foreach (var badge in chatRoot.embeddedData.twitchBadges)
                {
                    if (!outputChatRoot.embeddedData.twitchBadges.Any(b => b.name == badge.name))
                        outputChatRoot.embeddedData.twitchBadges.Add(badge);
                }
                foreach (var bit in chatRoot.embeddedData.twitchBits)
                {
                    if (!outputChatRoot.embeddedData.twitchBits.Any(b => b.prefix == bit.prefix))
                        outputChatRoot.embeddedData.twitchBits.Add(bit);
                }
            }
        }

        private async Task SaveOutput(FileInfo outputFileInfo, FileStream outputFs, ChatRoot chatRoot, CancellationToken cancellationToken)
        {
            Stream outputStream = mergeOptions.Compression switch
            {
                ChatCompression.None => outputFs,
                ChatCompression.Gzip => new GZipStream(outputFs, CompressionLevel.SmallestSize),
                _ => throw new NotSupportedException($"{mergeOptions.Compression} is not a supported chat compression.")
            };

            try
            {
                switch (mergeOptions.OutputFormat)
                {
                    case ChatFormat.Json:
                        await ChatJson.SerializeAsync(outputStream, chatRoot, cancellationToken);
                        break;
                    case ChatFormat.Html:
                        await ChatHtml.SerializeAsync(outputStream, outputFileInfo.FullName, chatRoot, _progress, chatRoot.embeddedData != null && (chatRoot.embeddedData.firstParty?.Count > 0 || chatRoot.embeddedData.twitchBadges?.Count > 0), cancellationToken);
                        break;
                    case ChatFormat.Text:
                        await ChatText.SerializeAsync(outputStream, chatRoot, mergeOptions.TextTimestampFormat);
                        break;
                    default:
                        throw new NotSupportedException($"{mergeOptions.OutputFormat} is not a supported output format.");
                }
            }
            finally
            {
                // GZipStream finishes writing on disposal, not flush.
                await outputStream.DisposeAsync();
            }
        }

        public async Task ParseJsonAsync(CancellationToken cancellationToken = new())
        {
            var chatRoots = await Task.WhenAll(mergeOptions.InputFiles.Select(async file => await ChatJson.DeserializeAsync(file, true, false, true, cancellationToken)));
            int streamer = chatRoots[0].streamer.id;
            if (chatRoots.Skip(1).Any(c => c.streamer.id != streamer))
            {
                throw new NotSupportedException("Merging chats from different streamers is not supported");
            }

            InputChatRoots = chatRoots;
        }
    }
}
