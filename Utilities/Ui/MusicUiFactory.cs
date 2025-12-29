using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Lyrics;
using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Music.Logic.Player;
using Discord;
using Lavalink4NET.Players;
using System.Globalization;
using Lavalink4NET.Players.Queued;

namespace Assistant.Net.Utilities.Ui;

public static class MusicUiFactory
{
    private const string NpCustomIdPrefix = "assistant:np";
    private const string LyricsPageButtonPrefix = "assistant:lyrics_page";
    private const int QueueItemsPerPage = 10;
    private const int PlaylistItemsPerPage = 10;

    public static (MessageComponent? Components, string? ErrorMessage) BuildQueueComponents(
        CustomPlayer player,
        int currentPage,
        ulong interactionMessageId,
        ulong requesterId)
    {
        if (player.CurrentTrack is null && player.Queue.IsEmpty) return (null, "The queue is empty.");

        var componentBuilder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        if (player.CurrentTrack is not null)
        {
            var title = $"## {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}";
            var customCurrentItem = player.CurrentItem?.As<CustomTrackQueueItem>();
            if (customCurrentItem != null) title += $"\nAdded by <@{customCurrentItem.RequesterId}>";

            if (player.CurrentTrack.ArtworkUri is not null)
                container.WithSection(section =>
                {
                    section.AddComponent(new TextDisplayBuilder(title));
                    section.WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties { Url = player.CurrentTrack.ArtworkUri.ToString() }
                    });
                });
            else
                container.WithTextDisplay(new TextDisplayBuilder(title));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("**Queue**\n*Nothing is currently playing.*"));
        }

        var queueCount = player.Queue.Count;
        container.WithSeparator();
        if (queueCount > 0)
        {
            var totalPages = (int)Math.Ceiling((double)queueCount / QueueItemsPerPage);
            currentPage = Math.Clamp(currentPage, 1, totalPages);

            container.WithTextDisplay(new TextDisplayBuilder($"## Next Up ({currentPage}/{totalPages})"));

            var firstIndex = (currentPage - 1) * QueueItemsPerPage;
            var lastIndex = Math.Min(firstIndex + QueueItemsPerPage, queueCount);

            var queueListBuilder = new StringBuilder();
            for (var i = firstIndex; i < lastIndex; i++)
            {
                var trackItem = player.Queue[i];
                if (trackItem.Track is null) continue;

                var trackTitle =
                    $"{i + 1}. {trackItem.Track.Title.Truncate(100).AsMarkdownLink(trackItem.Track.Uri?.ToString())}";
                queueListBuilder.AppendLine(trackTitle);

                var customItem = trackItem.As<CustomTrackQueueItem>();
                var requesterInfo = customItem is not null
                    ? $"Added by <@{customItem.RequesterId}>"
                    : "Unknown Requester";
                var durationInfo = trackItem.Track.Duration.FormatPlayerTime();

                queueListBuilder.AppendLine($"   └ {requesterInfo} | `{durationInfo}`");
            }

            if (queueListBuilder.Length > 0)
                container.WithTextDisplay(new TextDisplayBuilder(queueListBuilder.ToString()));

            container.WithSeparator();

            if (totalPages > 1)
                container.WithActionRow(row => row
                    .WithButton("◀ Previous",
                        $"assistant:queue_page_action:{requesterId}:{interactionMessageId}:{currentPage}:prev",
                        ButtonStyle.Secondary, disabled: currentPage == 1)
                    .WithButton("Next ▶",
                        $"assistant:queue_page_action:{requesterId}:{interactionMessageId}:{currentPage}:next",
                        ButtonStyle.Secondary, disabled: currentPage == totalPages)
                );

            var loopStatus = player.RepeatMode switch
            {
                TrackRepeatMode.Queue => "🔁 Looping Queue",
                TrackRepeatMode.Track => "🔂 Looping Track",
                _ => "➡️ Loop Disabled"
            };
            var totalSongsInQueueSystem = (player.CurrentTrack != null ? 1 : 0) + queueCount;

            container.WithTextDisplay(
                new TextDisplayBuilder(
                    $"Page {currentPage}/{totalPages} • {totalSongsInQueueSystem} Song(s) • {loopStatus}"));
        }
        else
        {
            var loopStatus = player.RepeatMode == TrackRepeatMode.Track ? "🔂 Looping Track" : "➡️ Loop Disabled";
            container.WithTextDisplay(new TextDisplayBuilder($"Queue is empty • {loopStatus}"));
        }

        componentBuilder.WithContainer(container);
        return (componentBuilder.Build(), null);
    }

    public static MessageComponent BuildNowPlayingDisplay(CustomPlayer player, ulong guildId, Config config)
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        var currentTrack = player.CurrentTrack;
        var queue = player.Queue;

        if (currentTrack != null)
        {
            container.WithSection(section =>
            {
                var titleAndAuthor =
                    $"## {currentTrack.Title.AsMarkdownLink(currentTrack.Uri?.ToString())}\nby {currentTrack.Author}";
                section.AddComponent(new TextDisplayBuilder(titleAndAuthor));

                if (currentTrack.ArtworkUri != null)
                    section.WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties { Url = currentTrack.ArtworkUri.ToString() }
                    });
            });

            var customItem = player.CurrentItem?.As<CustomTrackQueueItem>();
            if (customItem != null)
                container.WithTextDisplay(new TextDisplayBuilder($"Added by: **<@{customItem.RequesterId}>**"));

            if (player.Position?.Position != null)
            {
                var position = player.Position.Value.Position;
                var progressBar = MusicUtils.CreateProgressBar(position, currentTrack.Duration, 18);
                var currentTime = position.FormatPlayerTime();
                var totalTime = currentTrack.Duration.FormatPlayerTime();
                container.WithTextDisplay(new TextDisplayBuilder($"`{currentTime}` {progressBar} `{totalTime}`"));
            }
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder(
                $"**No song currently playing**\nUse `/play` to add songs. `{config.Client.Prefix}play` also works."));
        }

        if (currentTrack != null)
            container.WithSeparator(isDivider: false, spacing: SeparatorSpacingSize.Small);

        var controlsDisabled = currentTrack == null;

        var playbackRow = new ActionRowBuilder()
            .WithButton(null, $"{NpCustomIdPrefix}:{guildId}:prev_restart", ButtonStyle.Primary, Emoji.Parse("⏮️"),
                disabled: controlsDisabled)
            .WithButton(null, $"{NpCustomIdPrefix}:{guildId}:rewind", ButtonStyle.Primary, Emoji.Parse("⏪"),
                disabled: controlsDisabled)
            .WithButton(null, $"{NpCustomIdPrefix}:{guildId}:pause_resume",
                player.State == PlayerState.Paused ? ButtonStyle.Success : ButtonStyle.Primary,
                player.State == PlayerState.Paused ? Emoji.Parse("▶️") : Emoji.Parse("⏸️"), disabled: controlsDisabled)
            .WithButton(null, $"{NpCustomIdPrefix}:{guildId}:forward", ButtonStyle.Primary, Emoji.Parse("⏩"),
                disabled: controlsDisabled)
            .WithButton(null, $"{NpCustomIdPrefix}:{guildId}:skip", ButtonStyle.Primary, Emoji.Parse("⏭️"),
                disabled: controlsDisabled);
        container.WithActionRow(playbackRow);

        container.AddComponent(new SeparatorBuilder());

        var footerText = new StringBuilder();
        if (!queue.IsEmpty)
        {
            var nextTrack = queue[0].Track;
            if (nextTrack != null)
                footerText.Append($"Next: {nextTrack.Title.Truncate(50)} | {queue.Count} in queue");
            else
                footerText.Append($"{queue.Count} songs in queue");
        }
        else
        {
            footerText.Append("Queue is empty");
        }

        switch (player.RepeatMode)
        {
            case TrackRepeatMode.Track:
                footerText.Append(" | 🔂 Looping Track");
                break;
            case TrackRepeatMode.Queue:
                footerText.Append(" | 🔁 Looping Queue");
                break;
        }

        if (footerText.Length > 0)
        {
            container.WithSeparator(isDivider: false, spacing: SeparatorSpacingSize.Small);
            container.WithTextDisplay(new TextDisplayBuilder(footerText.ToString()));
        }

        var loopEmoji = player.RepeatMode switch
        {
            TrackRepeatMode.Track => Emoji.Parse("🔂"),
            TrackRepeatMode.Queue => Emoji.Parse("🔁"),
            _ => Emoji.Parse("➡️")
        };
        var utilityRow = new ActionRowBuilder()
            .WithButton("Stop", $"{NpCustomIdPrefix}:{guildId}:stop", ButtonStyle.Danger, Emoji.Parse("⏹️"),
                disabled: controlsDisabled)
            .WithButton("Loop", $"{NpCustomIdPrefix}:{guildId}:loop", ButtonStyle.Secondary, loopEmoji,
                disabled: controlsDisabled);
        container.WithActionRow(utilityRow);

        container.AddComponent(new SeparatorBuilder());

        var currentVolumePercent = (int)(player.Volume * 100);
        var maxVolume = config.Music.MaxPlayerVolumePercent;
        var volumeRow = new ActionRowBuilder()
            .WithButton("➖", $"{NpCustomIdPrefix}:{guildId}:vol_down", ButtonStyle.Success,
                disabled: controlsDisabled || currentVolumePercent <= 0)
            .WithButton($"🔊 {currentVolumePercent}%", $"{NpCustomIdPrefix}:{guildId}:vol_display",
                ButtonStyle.Secondary, disabled: true)
            .WithButton("➕", $"{NpCustomIdPrefix}:{guildId}:vol_up", ButtonStyle.Success,
                disabled: controlsDisabled || currentVolumePercent >= maxVolume);
        container.WithActionRow(volumeRow);

        builder.WithContainer(container);
        return builder.Build();
    }

    public static (MessageComponent? Components, string? ErrorMessage) BuildShowPlaylistResponse(
        PlaylistEntity playlist,
        int currentPage, IUser requester)
    {
        var totalSongs = playlist.Items.Count;
        if (totalSongs == 0)
        {
            var emptyContainer = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder($"**🎵 Playlist: {playlist.Name.Truncate(100)}**"))
                .WithTextDisplay(new TextDisplayBuilder("*This playlist is empty.*"));
            return (new ComponentBuilderV2().WithContainer(emptyContainer).Build(), null);
        }

        var totalPages = (int)Math.Ceiling((double)totalSongs / PlaylistItemsPerPage);
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"**🎵 Playlist: {playlist.Name.Truncate(100)}**"));
                section.AddComponent(new TextDisplayBuilder($"*Created by {requester.Mention}*"));
            })
            .WithSeparator();

        var songsOnPage = playlist.Items
            .OrderBy(i => i.Position)
            .Skip((currentPage - 1) * PlaylistItemsPerPage)
            .Take(PlaylistItemsPerPage)
            .ToList();

        var songListBuilder = new StringBuilder();
        for (var i = 0; i < songsOnPage.Count; i++)
        {
            var song = songsOnPage[i].Track;
            var overallIndex = (currentPage - 1) * PlaylistItemsPerPage + i + 1;
            songListBuilder.AppendLine(
                $"{overallIndex}. {song.Title.AsMarkdownLink(song.Uri).Truncate(80)} (`{TimeSpan.FromSeconds(song.Duration):mm\\:ss}`)");
        }

        container.WithTextDisplay(new TextDisplayBuilder(songListBuilder.ToString()));
        container.WithSeparator();

        var footerText =
            $"Page {currentPage}/{totalPages}  •  {totalSongs} Songs  •  Updated {TimestampTag.FormatFromDateTime(playlist.CreatedAt, TimestampTagStyles.Relative)}";
        container.WithTextDisplay(new TextDisplayBuilder(footerText));

        var controlsRow = new ActionRowBuilder()
            .WithButton("Previous", $"assistant:playlist:show_prev:{requester.Id}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, new Emoji("◀"), disabled: currentPage == 1)
            .WithButton("Next", $"assistant:playlist:show_next:{requester.Id}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, new Emoji("▶"), disabled: currentPage == totalPages)
            .WithButton("Shuffle", $"assistant:playlist:action_shuffle:{requester.Id}:{playlist.Name}",
                ButtonStyle.Primary,
                new Emoji("🔀"))
            .WithButton("Play", $"assistant:playlist:action_play:{requester.Id}:{playlist.Name}", ButtonStyle.Success,
                new Emoji("▶️"));

        container.WithActionRow(controlsRow);
        return (new ComponentBuilderV2().WithContainer(container).Build(), null);
    }

    public static MessageComponent BuildLyricsPage(GeniusSong song, List<string> lyricsChunks,
        int currentPage)
    {
        var totalPages = lyricsChunks.Count;
        currentPage = Math.Clamp(currentPage, 0, totalPages - 1);

        var accentColor = Color.Blue;
        if (!string.IsNullOrEmpty(song.SongArtPrimaryColor))
        {
            var hexColor = song.SongArtPrimaryColor.Replace("#", "");
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var colorValue))
                accentColor = new Color(colorValue);
        }

        var container = new ContainerBuilder()
            .WithAccentColor(accentColor)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# 🎶 {song.FullTitle.Truncate(250)}"));
                section.AddComponent(new TextDisplayBuilder($"*by {song.ArtistNames}*"));

                if (!string.IsNullOrEmpty(song.SongArtImageThumbnailUrl))
                    section.WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties { Url = song.SongArtImageThumbnailUrl }
                    });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(lyricsChunks.ElementAtOrDefault(currentPage) ??
                                                    "Lyrics page out of bounds."));

        if (totalPages > 1)
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder($"Page {currentPage + 1}/{totalPages}"));
        }

        var actionRow = new ActionRowBuilder();
        var hasPagination = false;
        if (totalPages > 1)
        {
            actionRow.WithButton("Previous", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:prev",
                ButtonStyle.Secondary,
                new Emoji("◀️"), disabled: currentPage == 0);
            actionRow.WithButton("Next", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:next",
                ButtonStyle.Secondary,
                new Emoji("▶️"), disabled: currentPage == totalPages - 1);
            hasPagination = true;
        }

        actionRow.WithButton("View on Genius", style: ButtonStyle.Link, url: song.Url);

        if (hasPagination || !string.IsNullOrEmpty(song.Url))
            container.WithActionRow(actionRow);

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}