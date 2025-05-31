using Assistant.Net.Services.GuildFeatures.Starboard;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Starboard.InteractionModules;

[Group("starboard", "Manage the starboard settings.")]
[RequireContext(ContextType.Guild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
public class StarboardModule(
    StarboardConfigService configService,
    ILogger<StarboardModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("enable", "Enable the starboard system for this server.")]
    public async Task EnableAsync()
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (config.StarboardChannelId == null)
        {
            await FollowupAsync("Please set a starboard channel first using `/starboard channel`.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (config.IsEnabled)
        {
            await FollowupAsync("Starboard is already enabled.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        config.IsEnabled = true;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);
        await FollowupAsync("✅ Starboard has been enabled.", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("disable", "Disable the starboard system for this server.")]
    public async Task DisableAsync()
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (!config.IsEnabled)
        {
            await FollowupAsync("Starboard is already disabled.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        config.IsEnabled = false;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);
        await FollowupAsync("❌ Starboard has been disabled.", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("channel", "Set or remove the starboard channel.")]
    public async Task ChannelAsync(
        [Summary("channel", "The channel to use for starboard posts (leave empty to remove).")]
        ITextChannel? channel = null)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (channel != null)
        {
            var botUser = Context.Guild.CurrentUser;
            var perms = botUser.GetPermissions(channel);
            if (!perms.SendMessages || !perms.EmbedLinks || !perms.AttachFiles || !perms.ReadMessageHistory)
            {
                await FollowupAsync(
                    $"I lack necessary permissions in {channel.Mention} (Need Send Messages, Embed Links, Attach Files, Read Message History).",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            config.StarboardChannelId = channel.Id;
            await configService.UpdateConfigAsync(config).ConfigureAwait(false);
            await FollowupAsync($"Starboard channel set to {channel.Mention}.", ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            config.StarboardChannelId = null;
            config.IsEnabled = false;
            await configService.UpdateConfigAsync(config).ConfigureAwait(false);
            await FollowupAsync("Starboard channel removed. Starboard is now disabled.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("emoji", "Set the emoji used for starring messages.")]
    public async Task EmojiAsync(
        [Summary("emoji", "The emoji to use (default: ⭐). Custom emojis supported.")]
        string emoji)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (!StarboardConfigService.IsValidEmoji(emoji))
        {
            await FollowupAsync(
                "Invalid emoji format. Please provide a standard Unicode emoji or a valid custom Discord emoji (e.g., `<:name:id>`).",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (Emote.TryParse(emoji, out var parsedEmoji) && parsedEmoji != null)
            try
            {
                var guildEmoji = await Context.Guild.GetEmoteAsync(parsedEmoji.Id).ConfigureAwait(false);
                if (guildEmoji == null)
                {
                    await FollowupAsync("The provided custom emoji was not found in this server or is inaccessible.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                emoji = guildEmoji.ToString();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to verify custom emoji {EmojiId} for starboard in guild {GuildId}",
                    parsedEmoji.Id, Context.Guild.Id);
            }

        config.StarEmoji = emoji;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);
        await FollowupAsync($"Star emoji set to {config.StarEmoji}.", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("threshold", "Set the minimum stars needed to post a message.")]
    public async Task ThresholdAsync(
        [Summary("count", "The number of unique reactions required (min: 1).")] [MinValue(1)]
        int count)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        config.Threshold = count;
        await configService.UpdateConfigAsync(config).ConfigureAwait(false);
        await FollowupAsync($"Star threshold set to {count}.", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("settings", "Show or toggle current starboard settings.")]
    public async Task SettingsAsync(
        [Summary("setting", "The setting to toggle (optional).")]
        [Choice("Allow Self Star", "allow_self_star")]
        [Choice("Allow Bot Messages", "allow_bot_messages")]
        [Choice("Ignore NSFW Channels", "ignore_nsfw_channels")]
        [Choice("Delete if Unstarred Below Threshold", "delete_if_unstarred")]
        string? setting = null)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var config = await configService.GetGuildConfigAsync(Context.Guild.Id).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(setting))
        {
            bool currentValue;
            bool newValue;
            string settingNameFriendly;

            switch (setting)
            {
                case "allow_self_star":
                    currentValue = config.AllowSelfStar;
                    newValue = !currentValue;
                    config.AllowSelfStar = newValue;
                    settingNameFriendly = "Allow Self Star";
                    break;
                case "allow_bot_messages":
                    currentValue = config.AllowBotMessages;
                    newValue = !currentValue;
                    config.AllowBotMessages = newValue;
                    settingNameFriendly = "Allow Bot Messages";
                    break;
                case "ignore_nsfw_channels":
                    currentValue = config.IgnoreNsfwChannels;
                    newValue = !currentValue;
                    config.IgnoreNsfwChannels = newValue;
                    settingNameFriendly = "Ignore NSFW Channels";
                    break;
                case "delete_if_unstarred":
                    currentValue = config.DeleteIfUnStarred;
                    newValue = !currentValue;
                    config.DeleteIfUnStarred = newValue;
                    settingNameFriendly = "Delete if Unstarred";
                    break;
                default:
                    await FollowupAsync("Invalid setting specified.", ephemeral: true).ConfigureAwait(false);
                    return;
            }

            await configService.UpdateConfigAsync(config).ConfigureAwait(false);
            await FollowupAsync($"{settingNameFriendly} has been set to {(newValue ? "✅ Enabled" : "❌ Disabled")}.",
                ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            var embed = new EmbedBuilder()
                .WithTitle("⭐ Starboard Settings")
                .WithColor(Color.Gold)
                .WithTimestamp(config.UpdatedAt)
                .WithFooter("Last updated");

            var starboardChMention =
                config.StarboardChannelId.HasValue ? $"<#{config.StarboardChannelId.Value}>" : "Not Set";
            var logChMention = config.LogChannelId.HasValue ? $"<#{config.LogChannelId.Value}>" : "Not Set";

            embed.AddField("Status", config.IsEnabled ? "✅ Enabled" : "❌ Disabled", true);
            embed.AddField("Channel", starboardChMention, true);
            embed.AddField("Emoji", config.StarEmoji, true);
            embed.AddField("Threshold", config.Threshold.ToString(), true);
            embed.AddField("Allow Self Star", config.AllowSelfStar ? "✅ Yes" : "❌ No", true);
            embed.AddField("Allow Bot Messages", config.AllowBotMessages ? "✅ Yes" : "❌ No", true);
            embed.AddField("Ignore NSFW", config.IgnoreNsfwChannels ? "✅ Yes" : "❌ No", true);
            embed.AddField("Delete if Unstarred", config.DeleteIfUnStarred ? "✅ Yes" : "❌ No", true);
            embed.AddField("Log Channel", logChMention, true);

            await FollowupAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
        }
    }
}