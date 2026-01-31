using System.Globalization;
using System.Text;
using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Lyrics;
using Assistant.Net.Models.Music;
using Assistant.Net.Services.Music.Logic;
using Discord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

namespace Assistant.Net.Utilities.Ui;

public static class MusicUiFactory
{
    private const string NpCustomIdPrefix = "np";
    private const string LyricsPageButtonPrefix = "lyrics_page";
    private const int QueueItemsPerPage = 10;
    private const int PlaylistItemsPerPage = 10;
    private const int WrappedItemsPerPage = 5;

    public static (MessageComponent? Components, string? ErrorMessage) BuildQueueComponents(
        CustomPlayer player,
        int currentPage,
        ulong interactionMessageId,
        ulong requesterId)
    {
        if (player.CurrentTrack is null && player.Queue.IsEmpty) return (null, "The queue is empty.");

        var container = new ContainerBuilder();

        if (player.CurrentTrack is not null)
        {
            var title = $"## {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}";
            var customCurrentItem = player.CurrentItem?.As<CustomTrackQueueItem>();
            if (customCurrentItem != null) title += $"\nAdded by <@{customCurrentItem.RequesterId}>";

            if (player.CurrentTrack.ArtworkUri is not null)
                container.WithSection(new SectionBuilder()
                    .AddComponent(new TextDisplayBuilder(title))
                    .WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties { Url = player.CurrentTrack.ArtworkUri.ToString() }
                    }));
            else
                container.WithTextDisplay(new TextDisplayBuilder(title));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("**Queue**\n*Nothing is currently playing.*"));
        }

        container.WithSeparator();

        var queueCount = player.Queue.Count;
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
                container.WithActionRow(new ActionRowBuilder().WithComponents([
                    new ButtonBuilder("◀ Previous",
                            $"queue_page_action:{requesterId}:{interactionMessageId}:{currentPage}:prev",
                            ButtonStyle.Secondary)
                        .WithDisabled(currentPage == 1),
                    new ButtonBuilder("Next ▶",
                            $"queue_page_action:{requesterId}:{interactionMessageId}:{currentPage}:next",
                            ButtonStyle.Secondary)
                        .WithDisabled(currentPage == totalPages)
                ]));

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

        return (new ComponentBuilderV2(container).Build(), null);
    }

    public static MessageComponent BuildNowPlayingDisplay(CustomPlayer player, ulong guildId, int maxVolumePercent)
    {
        var currentTrack = player.CurrentTrack;
        var queue = player.Queue;
        var container = new ContainerBuilder();

        if (currentTrack != null)
        {
            var titleAndAuthor =
                $"## {currentTrack.Title.AsMarkdownLink(currentTrack.Uri?.ToString())}\nby {currentTrack.Author}";

            if (currentTrack.ArtworkUri != null)
                container.WithSection(new SectionBuilder()
                    .AddComponent(new TextDisplayBuilder(titleAndAuthor))
                    .WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties { Url = currentTrack.ArtworkUri.ToString() }
                    }));
            else
                container.WithTextDisplay(new TextDisplayBuilder(titleAndAuthor));

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
                "**No song currently playing**\nUse `/play` to add songs."));
        }

        if (currentTrack != null)
            container.WithSeparator(new SeparatorBuilder().WithSpacing(SeparatorSpacingSize.Small)
                .WithIsDivider(false));

        var controlsDisabled = currentTrack == null;
        var currentVolumePercent = (int)(player.Volume * 100);

        // --- Row 1: Stop | Prev | Pause | Skip | Loop ---
        var loopEmoji = player.RepeatMode switch
        {
            TrackRepeatMode.Track => Emoji.Parse("🔂"),
            TrackRepeatMode.Queue => Emoji.Parse("🔁"),
            _ => Emoji.Parse("➡️")
        };

        var row1 = new ActionRowBuilder().WithComponents([
            new ButtonBuilder(null, $"{NpCustomIdPrefix}:{guildId}:stop", ButtonStyle.Danger)
                .WithEmote(Emoji.Parse("⏹️"))
                .WithDisabled(controlsDisabled),
            new ButtonBuilder(null, $"{NpCustomIdPrefix}:{guildId}:prev_restart")
                .WithEmote(Emoji.Parse("⏮️"))
                .WithDisabled(controlsDisabled),
            new ButtonBuilder(null, $"{NpCustomIdPrefix}:{guildId}:pause_resume",
                    player.State == PlayerState.Paused ? ButtonStyle.Success : ButtonStyle.Primary)
                .WithEmote(player.State == PlayerState.Paused ? Emoji.Parse("▶️") : Emoji.Parse("⏸️"))
                .WithDisabled(controlsDisabled),
            new ButtonBuilder(null, $"{NpCustomIdPrefix}:{guildId}:skip")
                .WithEmote(Emoji.Parse("⏭️"))
                .WithDisabled(controlsDisabled),
            new ButtonBuilder(null, $"{NpCustomIdPrefix}:{guildId}:loop", ButtonStyle.Secondary)
                .WithEmote(loopEmoji)
                .WithDisabled(controlsDisabled)
        ]);

        container.WithActionRow(row1);

        // --- Row 2: Vol - | Save | Vol + ---
        var row2 = new ActionRowBuilder().WithComponents([
            new ButtonBuilder("Vol -", $"{NpCustomIdPrefix}:{guildId}:vol_down", ButtonStyle.Secondary)
                .WithEmote(Emoji.Parse("🔉"))
                .WithDisabled(controlsDisabled || currentVolumePercent <= 0),
            new ButtonBuilder("Save", $"{NpCustomIdPrefix}:{guildId}:add_to_playlist", ButtonStyle.Success)
                .WithEmote(Emoji.Parse("💟"))
                .WithDisabled(controlsDisabled),
            new ButtonBuilder("Vol +", $"{NpCustomIdPrefix}:{guildId}:vol_up", ButtonStyle.Secondary)
                .WithEmote(Emoji.Parse("🔊"))
                .WithDisabled(controlsDisabled || currentVolumePercent >= maxVolumePercent)
        ]);

        container.WithActionRow(row2);

        // --- Footer Text ---
        var footerText = new StringBuilder();
        if (!queue.IsEmpty)
        {
            var nextTrack = queue[0].Track;
            if (nextTrack != null)
                footerText.Append($"Next: {nextTrack.Title.Truncate(40)} | {queue.Count} in queue");
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

        footerText.Append($" | Vol: {currentVolumePercent}%");

        container.WithSeparator(new SeparatorBuilder().WithSpacing(SeparatorSpacingSize.Small)
            .WithIsDivider(false));
        container.WithTextDisplay(new TextDisplayBuilder(footerText.ToString()));

        return new ComponentBuilderV2(container).Build();
    }

    public static MessageComponent BuildAddToPlaylistMenu(List<PlaylistEntity> playlists, string songTitle)
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("## Add to Playlist"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"Select a playlist to add **{songTitle.Truncate(50)}** to:"));

        if (playlists.Count > 0)
        {
            var options = playlists.Select(p => new SelectMenuOptionBuilder()
                .WithLabel(p.Name.Truncate(50))
                .WithValue(p.Id.ToString())
                .WithDescription($"{p.Items.Count} songs")
            ).Take(25).ToList();

            container.WithActionRow(new ActionRowBuilder()
                .WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId("np:playlist:select")
                    .WithPlaceholder("Select a playlist...")
                    .WithOptions(options)
                ));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("*You don't have any playlists yet.*"));
        }

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Create New Playlist", "np:playlist:create", ButtonStyle.Secondary, new Emoji("➕"))
        );

        return new ComponentBuilderV2(container).Build();
    }

    public static MessageComponent BuildAddToPlaylistSuccess(string songTitle, string playlistName)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Green)
            .WithTextDisplay(
                new TextDisplayBuilder(
                    $"✅ Added **{songTitle.Truncate(50)}** to **{playlistName}**!"));
        return new ComponentBuilderV2(container).Build();
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
            return (new ComponentBuilderV2(emptyContainer).Build(), null);
        }

        var totalPages = (int)Math.Ceiling((double)totalSongs / PlaylistItemsPerPage);
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"**🎵 Playlist: {playlist.Name.Truncate(100)}**"))
            .WithTextDisplay(new TextDisplayBuilder($"*Created by {requester.Mention}*"))
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
            $"Page {currentPage}/{totalPages}  •  {totalSongs} Songs  •  Updated {playlist.CreatedAt.GetRelativeTime()}";
        container.WithTextDisplay(new TextDisplayBuilder(footerText));

        container.WithActionRow(new ActionRowBuilder().WithComponents([
            new ButtonBuilder("Previous", $"playlist:show_prev:{requester.Id}:{playlist.Name}:{currentPage}",
                    ButtonStyle.Secondary)
                .WithEmote(new Emoji("◀"))
                .WithDisabled(currentPage == 1),
            new ButtonBuilder("Next", $"playlist:show_next:{requester.Id}:{playlist.Name}:{currentPage}",
                    ButtonStyle.Secondary)
                .WithEmote(new Emoji("▶"))
                .WithDisabled(currentPage == totalPages),
            new ButtonBuilder("Shuffle", $"playlist:action_shuffle:{requester.Id}:{playlist.Name}")
                .WithEmote(new Emoji("🔀")),
            new ButtonBuilder("Play", $"playlist:action_play:{requester.Id}:{playlist.Name}",
                    ButtonStyle.Success)
                .WithEmote(new Emoji("▶️"))
        ]));

        return (new ComponentBuilderV2(container).Build(), null);
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
            .WithAccentColor(accentColor);

        var headerText = $"# 🎶 {song.FullTitle.Truncate(250)}";
        var subHeaderText = $"*by {song.ArtistNames}*";

        if (!string.IsNullOrEmpty(song.SongArtImageThumbnailUrl))
            container.WithSection(new SectionBuilder()
                .AddComponent(new TextDisplayBuilder(headerText))
                .AddComponent(new TextDisplayBuilder(subHeaderText))
                .WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties { Url = song.SongArtImageThumbnailUrl }
                }));
        else
            container
                .WithTextDisplay(new TextDisplayBuilder(headerText))
                .WithTextDisplay(new TextDisplayBuilder(subHeaderText));

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(lyricsChunks.ElementAtOrDefault(currentPage) ??
                                                    "Lyrics page out of bounds."));

        if (totalPages > 1)
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder($"Page {currentPage + 1}/{totalPages}"));
        }

        var buttons = new List<ButtonBuilder>();

        if (totalPages > 1)
        {
            buttons.Add(new ButtonBuilder("Previous", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:prev",
                    ButtonStyle.Secondary)
                .WithEmote(new Emoji("◀️"))
                .WithDisabled(currentPage == 0));

            buttons.Add(new ButtonBuilder("Next", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:next",
                    ButtonStyle.Secondary)
                .WithEmote(new Emoji("▶️"))
                .WithDisabled(currentPage == totalPages - 1));
        }

        if (!string.IsNullOrEmpty(song.Url))
            buttons.Add(new ButtonBuilder("View on Genius", style: ButtonStyle.Link, url: song.Url));

        if (buttons.Count > 0)
            container.WithActionRow(new ActionRowBuilder().WithComponents(buttons));

        return new ComponentBuilderV2(container).Build();
    }

    public static MessageComponent BuildWrappedComponents(
        List<TrackPlayCount> allTracks,
        int page,
        ulong targetUserId,
        string title,
        string? iconUrl)
    {
        var totalItems = allTracks.Count;
        var totalPages = (int)Math.Ceiling((double)totalItems / WrappedItemsPerPage);
        if (totalPages == 0) totalPages = 1;
        var currentPage = Math.Clamp(page, 1, totalPages);

        var container = new ContainerBuilder();

        if (!string.IsNullOrEmpty(iconUrl))
            container.WithSection(new SectionBuilder()
                .AddComponent(new TextDisplayBuilder($"# {title}"))
                .AddComponent(new TextDisplayBuilder($"**Top Songs** • {totalItems} Total"))
                .WithAccessory(new ThumbnailBuilder { Media = new UnfurledMediaItemProperties { Url = iconUrl } }));
        else
            container
                .WithTextDisplay(new TextDisplayBuilder($"# {title}"))
                .WithTextDisplay(new TextDisplayBuilder($"**Top Songs** • {totalItems} Total"));

        container.WithSeparator();

        if (totalItems == 0)
        {
            container.WithTextDisplay(new TextDisplayBuilder("No play history found."));
            return new ComponentBuilderV2(container).Build();
        }

        var tracksOnPage = allTracks
            .Skip((currentPage - 1) * WrappedItemsPerPage)
            .Take(WrappedItemsPerPage)
            .ToList();

        foreach (var trackSection in tracksOnPage.Select((item, i) =>
                 {
                     var rank = (currentPage - 1) * WrappedItemsPerPage + i + 1;
                     var duration = TimeSpan.FromSeconds(item.Track.Duration).FormatPlayerTime();

                     var section = new SectionBuilder()
                         .AddComponent(new TextDisplayBuilder(
                             $"**#{rank}** {item.Track.Title.Truncate(80).AsMarkdownLink(item.Track.Uri)}"))
                         .AddComponent(new TextDisplayBuilder(
                             $"**Artist:** {item.Track.Artist?.Truncate(50) ?? "Unknown"}"))
                         .AddComponent(new TextDisplayBuilder($"**Plays:** {item.Count} • **Duration:** {duration}"));

                     if (!string.IsNullOrEmpty(item.Track.ThumbnailUrl))
                         section.WithAccessory(new ThumbnailBuilder
                             { Media = new UnfurledMediaItemProperties { Url = item.Track.ThumbnailUrl } });

                     return section;
                 }))
            if (trackSection.Accessory != null)
                container.WithSection(trackSection);
            else
                foreach (var component in trackSection.Components)
                    if (component is TextDisplayBuilder textBuilder)
                        container.WithTextDisplay(textBuilder);

        container.WithSeparator();
        container.WithTextDisplay(
            new TextDisplayBuilder($"Page {currentPage}/{totalPages} • {DateTime.UtcNow.GetLongDateTime()}"));

        if (totalPages > 1)
            container.WithActionRow(new ActionRowBuilder().WithComponents([
                new ButtonBuilder("◀ Previous", $"wrapped:{targetUserId}:{currentPage - 1}",
                        ButtonStyle.Secondary)
                    .WithDisabled(currentPage <= 1),
                new ButtonBuilder("Next ▶", $"wrapped:{targetUserId}:{currentPage + 1}",
                        ButtonStyle.Secondary)
                    .WithDisabled(currentPage >= totalPages)
            ]));

        return new ComponentBuilderV2(container).Build();
    }
}