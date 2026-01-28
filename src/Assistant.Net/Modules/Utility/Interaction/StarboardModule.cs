using Assistant.Net.Data.Entities;
using Assistant.Net.Services.Features;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Utility.Interaction;

[RequireContext(ContextType.Guild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
public class StarboardModule(
    StarboardConfigService configService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string IdSelectChannel = "sb_select_channel";
    private const string IdTogglePrefix = "sb_toggle:";
    private const string IdBtnEmoji = "sb_btn:emoji";
    private const string IdBtnThreshold = "sb_btn:threshold";
    private const string IdModalEmoji = "sb_modal_emoji";
    private const string IdModalThreshold = "sb_modal_threshold";

    [SlashCommand("starboard", "Open the starboard configuration dashboard.")]
    public async Task StarboardDashboardAsync()
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);
        var components = BuildDashboardComponents(config);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [ComponentInteraction(IdSelectChannel)]
    public async Task HandleChannelSelect(string[] selectedChannelIds)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (selectedChannelIds.Length == 0) return;

        if (!ulong.TryParse(selectedChannelIds[0], out var channelId))
        {
            await FollowupAsync("Invalid channel ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var channel = Context.Guild.GetTextChannel(channelId);
        if (channel == null)
        {
            await FollowupAsync("Channel not found or is not a text channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var botUser = Context.Guild.CurrentUser;
        var perms = botUser.GetPermissions(channel);
        if (!perms.SendMessages || !perms.EmbedLinks || !perms.AttachFiles || !perms.ReadMessageHistory)
        {
            await FollowupAsync(
                $"❌ I lack necessary permissions in {channel.Mention}.\nI need: `Send Messages`, `Embed Links`, `Attach Files`, `Read Message History`.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        config.StarboardChannelId = channel.Id;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);

        var components = BuildDashboardComponents(config);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(IdTogglePrefix + "*")]
    public async Task HandleToggle(string setting)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        switch (setting)
        {
            case "enabled":
                if (config.StarboardChannelId == null && !config.IsEnabled)
                {
                    await FollowupAsync("⚠️ Please select a channel first.", ephemeral: true).ConfigureAwait(false);
                    return;
                }

                config.IsEnabled = !config.IsEnabled;
                break;
            case "self_star":
                config.AllowSelfStar = !config.AllowSelfStar;
                break;
            case "bot_msgs":
                config.AllowBotMessages = !config.AllowBotMessages;
                break;
            case "nsfw":
                config.IgnoreNsfwChannels = !config.IgnoreNsfwChannels;
                break;
            case "delete_unstarred":
                config.DeleteIfUnStarred = !config.DeleteIfUnStarred;
                break;
        }

        await configService.UpdateConfigAsync(config).ConfigureAwait(false);

        var components = BuildDashboardComponents(config);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(IdBtnEmoji)]
    public async Task HandleEmojiButton()
    {
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);
        var modal = new ModalBuilder()
            .WithTitle("Set Starboard Emoji")
            .WithCustomId(IdModalEmoji)
            .AddTextInput("Emoji", "emoji_input", placeholder: "⭐ or <:custom:123>", value: config.StarEmoji,
                maxLength: 50)
            .Build();
        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ComponentInteraction(IdBtnThreshold)]
    public async Task HandleThresholdButton()
    {
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);
        var modal = new ModalBuilder()
            .WithTitle("Set Star Threshold")
            .WithCustomId(IdModalThreshold)
            .AddTextInput("Minimum Stars", "threshold_input", placeholder: "1",
                value: config.Threshold.ToString(), maxLength: 3)
            .Build();
        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ModalInteraction(IdModalEmoji)]
    public async Task HandleEmojiModalSubmit(StarboardEmojiModal modal)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var inputEmoji = modal.EmojiInput.Trim();

        if (!StarboardConfigService.IsValidEmoji(inputEmoji))
        {
            await FollowupAsync("Invalid emoji format. Please use a standard emoji or a valid custom emoji.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (Emote.TryParse(inputEmoji, out var parsedEmoji))
        {
            var guildEmoji = await Context.Guild.GetEmoteAsync(parsedEmoji.Id).ConfigureAwait(false);
            if (guildEmoji == null)
            {
                await FollowupAsync("The provided custom emoji is not accessible in this server.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            inputEmoji = guildEmoji.ToString();
        }

        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);
        config.StarEmoji = inputEmoji;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);

        var components = BuildDashboardComponents(config);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ModalInteraction(IdModalThreshold)]
    public async Task HandleThresholdModalSubmit(StarboardThresholdModal modal)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (!int.TryParse(modal.ThresholdInput, out var newThreshold) || newThreshold < 1)
        {
            await FollowupAsync("Threshold must be a number greater than 0.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);
        config.Threshold = newThreshold;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);

        var components = BuildDashboardComponents(config);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    private MessageComponent BuildDashboardComponents(StarboardConfigEntity config)
    {
        var container = new ContainerBuilder();
        var iconUrl = Context.Guild.IconUrl ?? "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2b50.png";

        container.WithSection(new SectionBuilder()
            .AddComponent(new TextDisplayBuilder("# Starboard Settings"))
            .AddComponent(new TextDisplayBuilder($"**Current Config:** {config.StarEmoji} x {config.Threshold}"))
            .WithAccessory(new ThumbnailBuilder
                { Media = new UnfurledMediaItemProperties { Url = iconUrl } }));

        container.WithSeparator();

        // Channel Selector
        container.WithTextDisplay(new TextDisplayBuilder("## Target Channel"));
        var channelSelect = new SelectMenuBuilder()
            .WithType(ComponentType.ChannelSelect)
            .WithCustomId(IdSelectChannel)
            .WithType(ComponentType.ChannelSelect)
            .WithPlaceholder("Select a channel for starboard posts...");

        if (config.StarboardChannelId.HasValue &&
            Context.Guild.GetChannel((ulong)config.StarboardChannelId.Value) is IChannel existingChannel)
            channelSelect.WithDefaultValues(SelectMenuDefaultValue.FromChannel(existingChannel));

        container.WithActionRow(new ActionRowBuilder().WithComponents([channelSelect]));

        // Config Buttons (Emoji & Threshold)
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Change Emoji", IdBtnEmoji, ButtonStyle.Secondary, new Emoji("🎨"))
            .WithButton("Set Threshold", IdBtnThreshold, ButtonStyle.Secondary, new Emoji("🔢")));

        container.WithSeparator();

        // Toggles
        AddToggleSection(container, "Enable Starboard", "Master switch for the starboard system.",
            config.IsEnabled, "enabled", true);

        AddToggleSection(container, "Allow Self Star", "Users can star their own messages.",
            config.AllowSelfStar, "self_star");

        AddToggleSection(container, "Allow Bot Messages", "Bot messages can be starred.",
            config.AllowBotMessages, "bot_msgs");

        AddToggleSection(container, "Ignore NSFW Channels", "Do not track stars in NSFW channels.",
            config.IgnoreNsfwChannels, "nsfw");

        AddToggleSection(container, "Delete Unstarred", "Remove post if stars drop below threshold.",
            config.DeleteIfUnStarred, "delete_unstarred");

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static void AddToggleSection(ContainerBuilder container, string title, string description, bool state,
        string settingKey, bool isMaster = false)
    {
        var style = state ? ButtonStyle.Success : ButtonStyle.Secondary;
        var label = state ? "ON" : "OFF";
        var customId = $"{IdTogglePrefix}{settingKey}";

        if (isMaster && !state) style = ButtonStyle.Danger;

        container.WithSection(new SectionBuilder()
            .AddComponent(new TextDisplayBuilder($"**{title}**"))
            .AddComponent(new TextDisplayBuilder(description))
            .WithAccessory(new ButtonBuilder(label, customId, style)));
    }

    public class StarboardEmojiModal : IModal
    {
        [ModalTextInput("emoji_input")] public string EmojiInput { get; set; } = string.Empty;
        public string Title => "Set Starboard Emoji";
    }

    public class StarboardThresholdModal : IModal
    {
        [ModalTextInput("threshold_input")] public string ThresholdInput { get; set; } = string.Empty;
        public string Title => "Set Star Threshold";
    }
}