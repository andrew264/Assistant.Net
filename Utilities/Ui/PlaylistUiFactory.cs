using System.Text;
using Assistant.Net.Data.Entities;
using Discord;

namespace Assistant.Net.Utilities.Ui;

public static class PlaylistUiFactory
{
    public enum PlaylistViewMode
    {
        View,
        Remove,
        Share,
        DeleteConfirm
    }

    public const string DashSelectId = "playlist:dash:select";
    public const string DashCreateId = "playlist:dash:create";
    public const string DashBackId = "playlist:dash:back";
    public const string DashNavPrefix = "playlist:dash:nav"; // :playlistId:page:action
    public const string DashActionPrefix = "playlist:dash:act"; // :playlistId:action
    public const string DashDeleteConfirmId = "playlist:dash:del_confirm"; // :playlistId
    public const string DashRemoveSelectId = "playlist:dash:rem_sel"; // :playlistId:page
    public const string DashShareSelectId = "playlist:dash:share_sel"; // :playlistId
    public const string DashCancelId = "playlist:dash:cancel"; // :playlistId

    private const int ItemsPerPage = 10;

    public static MessageComponent BuildPlaylistDashboard(List<PlaylistEntity> playlists, ulong userId)
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("# Playlists"))
            .WithTextDisplay(new TextDisplayBuilder("Select a playlist."));

        if (playlists.Count == 0)
            container.WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder("You don't have any playlists yet."));

        var selectOptions = playlists
            .Select(p => new SelectMenuOptionBuilder()
                .WithLabel(p.Name.Truncate(100))
                .WithValue(p.Id.ToString())
                .WithDescription($"{p.Items.Count} songs • {p.CreatedAt:MMM dd, yyyy}")
                .WithEmote(new Emoji("💿")))
            .ToList();

        ActionRowBuilder? selectRow = null;
        if (selectOptions.Count > 0)
            selectRow = new ActionRowBuilder()
                .WithComponents([
                    new SelectMenuBuilder()
                        .WithCustomId(DashSelectId)
                        .WithPlaceholder("Select a playlist...")
                        .WithOptions(selectOptions)
                ]);

        var buttonRow = new ActionRowBuilder()
            .WithButton("Create New Playlist", DashCreateId, ButtonStyle.Success, new Emoji("➕"));

        if (selectRow != null) container.WithActionRow(selectRow);
        container.WithActionRow(buttonRow);

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildPlaylistDetail(
        PlaylistEntity playlist,
        int currentPage,
        PlaylistViewMode mode = PlaylistViewMode.View,
        string? statusMessage = null)
    {
        var totalSongs = playlist.Items.Count;
        var totalPages = (int)Math.Ceiling((double)totalSongs / ItemsPerPage);
        if (totalPages == 0) totalPages = 1;
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        var container = new ContainerBuilder();

        // -- Header --
        container.WithTextDisplay(new TextDisplayBuilder($"# {playlist.Name.Truncate(80)}"));
        container.WithTextDisplay(new TextDisplayBuilder(
            $"{totalSongs} Songs • Created {playlist.CreatedAt.GetRelativeTime()}"));

        if (!string.IsNullOrEmpty(statusMessage))
            container.WithTextDisplay(new TextDisplayBuilder($"\n*{statusMessage}*"));

        container.WithSeparator();

        // -- Song List --
        if (totalSongs == 0)
        {
            container.WithTextDisplay(new TextDisplayBuilder("*This playlist is empty.*"));
        }
        else
        {
            var songsOnPage = playlist.Items
                .OrderBy(i => i.Position)
                .Skip((currentPage - 1) * ItemsPerPage)
                .Take(ItemsPerPage)
                .ToList();

            var sb = new StringBuilder();
            foreach (var item in songsOnPage)
            {
                var songTitle = item.Track.Title.Truncate(60).AsMarkdownLink(item.Track.Uri);
                var duration = TimeSpan.FromSeconds(item.Track.Duration).FormatPlayerTime();
                sb.AppendLine($"**{item.Position}.** {songTitle} `({duration})`");
            }

            container.WithTextDisplay(new TextDisplayBuilder(sb.ToString()));
        }

        container.WithSeparator();

        // -- Controls based on Mode --
        switch (mode)
        {
            case PlaylistViewMode.View:
                AddViewModeControls(container, playlist.Id, currentPage, totalPages);
                break;
            case PlaylistViewMode.Remove:
                AddRemoveModeControls(container, playlist, currentPage);
                break;
            case PlaylistViewMode.Share:
                AddShareModeControls(container, playlist.Id);
                break;
            case PlaylistViewMode.DeleteConfirm:
                AddDeleteConfirmControls(container, playlist.Id, playlist.Name);
                break;
        }

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static void AddViewModeControls(ContainerBuilder container, long playlistId, int currentPage,
        int totalPages)
    {
        // Pagination
        container.WithTextDisplay(new TextDisplayBuilder($"Page {currentPage}/{totalPages}"));
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("◀", $"{DashNavPrefix}:{playlistId}:{currentPage - 1}:prev", ButtonStyle.Secondary,
                disabled: currentPage == 1)
            .WithButton("▶", $"{DashNavPrefix}:{playlistId}:{currentPage + 1}:next", ButtonStyle.Secondary,
                disabled: currentPage == totalPages)
        );

        // Primary Actions
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Play", $"{DashActionPrefix}:{playlistId}:play", ButtonStyle.Success, new Emoji("▶️"))
            .WithButton("Shuffle", $"{DashActionPrefix}:{playlistId}:shuffle", ButtonStyle.Secondary, new Emoji("🔀"))
            .WithButton("Add Song", $"{DashActionPrefix}:{playlistId}:add", ButtonStyle.Primary, new Emoji("➕"))
        );

        // Management Actions
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Rename", $"{DashActionPrefix}:{playlistId}:rename", ButtonStyle.Secondary, new Emoji("✏️"))
            .WithButton("Remove Songs", $"{DashActionPrefix}:{playlistId}:mode_remove", ButtonStyle.Secondary,
                new Emoji("🗑️"))
            .WithButton("Share", $"{DashActionPrefix}:{playlistId}:mode_share", ButtonStyle.Secondary, new Emoji("📤"))
            .WithButton("Delete", $"{DashActionPrefix}:{playlistId}:mode_delete", ButtonStyle.Danger, new Emoji("✖️"))
        );

        // Back Button
        container.WithSeparator();
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Back to Dashboard", DashBackId, ButtonStyle.Secondary, new Emoji("↩️")));
    }

    private static void AddRemoveModeControls(ContainerBuilder container, PlaylistEntity playlist, int currentPage)
    {
        var songsOnPage = playlist.Items
            .OrderBy(i => i.Position)
            .Skip((currentPage - 1) * ItemsPerPage)
            .Take(ItemsPerPage)
            .ToList();

        if (songsOnPage.Count > 0)
        {
            var options = songsOnPage.Select(item => new SelectMenuOptionBuilder()
                .WithLabel($"{item.Position}. {item.Track.Title.Truncate(90)}")
                .WithValue(item.Position.ToString())
                .WithDescription(item.Track.Artist?.Truncate(50) ?? "Unknown Artist")
            ).ToList();

            container.WithTextDisplay(new TextDisplayBuilder("**Select songs to remove:**"));
            container.WithActionRow(new ActionRowBuilder()
                .WithComponents([
                    new SelectMenuBuilder()
                        .WithCustomId($"{DashRemoveSelectId}:{playlist.Id}:{currentPage}")
                        .WithPlaceholder("Choose songs...")
                        .WithMinValues(1)
                        .WithMaxValues(options.Count)
                        .WithOptions(options)
                ]));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("No songs on this page to remove."));
        }

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Cancel", $"{DashCancelId}:{playlist.Id}", ButtonStyle.Secondary));
    }

    private static void AddShareModeControls(ContainerBuilder container, long playlistId)
    {
        container.WithTextDisplay(new TextDisplayBuilder("**Select a user to share this playlist with:**"));

        container.WithActionRow(new ActionRowBuilder()
            .WithComponents([
                new SelectMenuBuilder()
                    .WithType(ComponentType.UserSelect)
                    .WithCustomId($"{DashShareSelectId}:{playlistId}")
                    .WithPlaceholder("Search for a user...")
            ]));

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Cancel", $"{DashCancelId}:{playlistId}", ButtonStyle.Secondary));
    }

    private static void AddDeleteConfirmControls(ContainerBuilder container, long playlistId, string playlistName)
    {
        container.WithAccentColor(Color.Red);
        container.WithTextDisplay(new TextDisplayBuilder($"⚠️ **Are you sure you want to delete '{playlistName}'?**"));
        container.WithTextDisplay(new TextDisplayBuilder("This action cannot be undone."));

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Yes, Delete", $"{DashDeleteConfirmId}:{playlistId}", ButtonStyle.Danger)
            .WithButton("Cancel", $"{DashCancelId}:{playlistId}", ButtonStyle.Secondary));
    }
}