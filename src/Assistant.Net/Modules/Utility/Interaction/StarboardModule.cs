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
    private const string IdModalSettings = "sb_modal_settings";

    [SlashCommand("starboard", "Configure the starboard settings.")]
    public async Task StarboardCommandAsync()
    {
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        var selectedSettings = new List<string>();
        if (config.IsEnabled) selectedSettings.Add("enabled");
        if (config.AllowSelfStar) selectedSettings.Add("self_star");
        if (config.AllowBotMessages) selectedSettings.Add("bot_msgs");
        if (config.IgnoreNsfwChannels) selectedSettings.Add("nsfw");
        if (config.DeleteIfUnStarred) selectedSettings.Add("delete_unstarred");

        var currentChannel = config.StarboardChannelId.HasValue
            ? Context.Guild.GetChannel(config.StarboardChannelId.Value)
            : null;

        var modal = new StarboardSettingsModal
        {
            Channel = currentChannel != null ? [currentChannel] : null,
            EmojiInput = config.StarEmoji,
            ThresholdInput = config.Threshold.ToString(),
            Settings = selectedSettings.ToArray()
        };

        await RespondWithModalAsync(IdModalSettings, modal).ConfigureAwait(false);
    }

    [ModalInteraction(IdModalSettings)]
    public async Task HandleSettingsModalSubmit(StarboardSettingsModal modal)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        var selected = modal.Settings ?? [];

        // Validate Threshold
        if (!int.TryParse(modal.ThresholdInput, out var newThreshold) || newThreshold < 1)
        {
            await FollowupAsync("❌ Threshold must be a whole number greater than 0.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        // Validate Emoji
        var inputEmoji = modal.EmojiInput.Trim();
        if (!StarboardConfigService.IsValidEmoji(inputEmoji))
        {
            await FollowupAsync("❌ Invalid emoji format. Please use a standard emoji or a valid custom emoji.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (Emote.TryParse(inputEmoji, out var parsedEmoji))
        {
            var guildEmoji = await Context.Guild.GetEmoteAsync(parsedEmoji.Id).ConfigureAwait(false);
            if (guildEmoji == null)
            {
                await FollowupAsync("❌ The provided custom emoji is not accessible in this server.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            inputEmoji = guildEmoji.ToString();
        }

        // Validate Channel
        ulong? newChannelId = null;
        if (modal.Channel is { Length: > 0 })
        {
            var channel = modal.Channel[0];
            if (channel is not ITextChannel textChannel)
            {
                await FollowupAsync("❌ The selected channel is invalid or not a text channel.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var botUser = Context.Guild.CurrentUser;
            var perms = botUser.GetPermissions(textChannel);
            if (!perms.SendMessages || !perms.EmbedLinks || !perms.AttachFiles || !perms.ReadMessageHistory)
            {
                await FollowupAsync(
                    $"❌ I lack necessary permissions in {textChannel.Mention}.\nI need: `Send Messages`, `Embed Links`, `Attach Files`, `Read Message History`.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            newChannelId = textChannel.Id;
        }

        // Validate Enable State
        if (newChannelId == null && selected.Contains("enabled"))
        {
            await FollowupAsync("⚠️ You must select a Starboard Channel if you want to enable the starboard.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Save Configuration
        config.StarboardChannelId = newChannelId;
        config.StarEmoji = inputEmoji;
        config.Threshold = newThreshold;

        config.IsEnabled = selected.Contains("enabled");
        config.AllowSelfStar = selected.Contains("self_star");
        config.AllowBotMessages = selected.Contains("bot_msgs");
        config.IgnoreNsfwChannels = selected.Contains("nsfw");
        config.DeleteIfUnStarred = selected.Contains("delete_unstarred");

        await configService.UpdateConfigAsync(config).ConfigureAwait(false);

        await FollowupAsync("✅ Starboard configuration updated successfully!", ephemeral: true).ConfigureAwait(false);
    }

    public class StarboardSettingsModal : IModal
    {
        [RequiredInput(false)]
        [ModalChannelSelect("channel_id")]
        public IChannel[]? Channel { get; set; }

        [RequiredInput]
        [ModalTextInput("emoji_input", placeholder: "⭐ or <:custom:123>", maxLength: 50)]
        public string EmojiInput { get; set; } = string.Empty;

        [RequiredInput]
        [ModalTextInput("threshold_input", placeholder: "3", maxLength: 3)]
        public string ThresholdInput { get; set; } = string.Empty;

        [RequiredInput(false)]
        [ModalCheckboxGroup("settings", 0, 5)]
        [ModalCheckboxGroupOption("Enable Starboard", "enabled", "Master switch for the starboard system.")]
        [ModalCheckboxGroupOption("Allow Self Star", "self_star", "Users can star their own messages.")]
        [ModalCheckboxGroupOption("Allow Bot Messages", "bot_msgs", "Bot messages can be starred.")]
        [ModalCheckboxGroupOption("Ignore NSFW Channels", "nsfw", "Do not track stars in NSFW channels.")]
        [ModalCheckboxGroupOption("Delete Unstarred", "delete_unstarred", "Remove post if stars drop below threshold.")]
        public string[] Settings { get; set; } = [];

        public string Title => "Starboard Configuration";
    }
}