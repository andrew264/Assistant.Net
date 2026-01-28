using Assistant.Net.Services.Data;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Info.Prefix;

public class InfoPrefixModule(
    DiscordSocketClient client,
    UserService userService,
    IHttpClientFactory httpClientFactory,
    ILogger<InfoPrefixModule> logger)
    : ModuleBase<SocketCommandContext>
{
    [Command("avatar")]
    [Alias("av", "pfp")]
    [Summary("Shows the avatar of a user or yourself.")]
    public async Task AvatarPrefixAsync([Summary("The user to get the avatar from.")] IUser? user = null)
    {
        var targetUser = user ?? Context.User;

        FileAttachment? fileAttachment = null;
        try
        {
            var (components, attachment, errorMessage) =
                await UserUtils.GenerateAvatarComponentsAsync(targetUser, httpClientFactory, logger)
                    .ConfigureAwait(false);
            fileAttachment = attachment;

            if (errorMessage != null || components == null || !fileAttachment.HasValue)
            {
                await ReplyAsync(errorMessage ?? "An unknown error occurred while fetching the avatar.",
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
                return;
            }

            await Context.Channel.SendFileAsync(
                fileAttachment.Value,
                components: components,
                flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None,
                messageReference: new MessageReference(Context.Message.Id)
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending avatar for {User} in prefix command", targetUser.Username);
            await ReplyAsync($"An error occurred while fetching the avatar for {targetUser.Username}.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        finally
        {
            fileAttachment?.Dispose();
        }
    }

    [Command("userinfo")]
    [Alias("user", "whois", "info")]
    [Summary("Get information about a user.")]
    public async Task UserInfoPrefixCommand([Summary("The user to get information about.")] IUser? user = null)
    {
        var targetUser = user ?? Context.User;

        var showSensitiveInfo = false;
        if (Context.User is SocketGuildUser requestingGuildUser)
            showSensitiveInfo = requestingGuildUser.GuildPermissions.Administrator;

        var components = await UserUtils.GenerateUserInfoV2Async(targetUser, showSensitiveInfo, userService, client)
            .ConfigureAwait(false);

        await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
                flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }
}