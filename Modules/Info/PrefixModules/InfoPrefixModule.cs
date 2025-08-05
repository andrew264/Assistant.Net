using Assistant.Net.Services.User;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Info.PrefixModules;

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
        var avatarUrl = targetUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? targetUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(avatarUrl))
        {
            await ReplyAsync("Could not retrieve avatar URL for this user.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var displayUserName =
            (targetUser as SocketGuildUser)?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;

        FileAttachment? fileAttachment = null;
        try
        {
            fileAttachment = await AttachmentUtils
                .DownloadFileAsAttachmentAsync(avatarUrl, "avatar.png", httpClientFactory, logger)
                .ConfigureAwait(false);

            if (fileAttachment == null)
            {
                await ReplyAsync($"Could not download avatar for {displayUserName}.",
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
                return;
            }

            var userColor = UserUtils.GetTopRoleColor(targetUser as SocketUser ??
                                                      Context.Guild.GetUser(targetUser.Id));

            var container = new ContainerBuilder()
                .WithAccentColor(userColor)
                .WithTextDisplay(new TextDisplayBuilder($"## {displayUserName}'s Avatar"))
                .WithMediaGallery(["attachment://avatar.png"])
                .WithActionRow(row => row.WithButton("Open Original", style: ButtonStyle.Link, url: avatarUrl));

            var components = new ComponentBuilderV2().WithContainer(container).Build();

            await Context.Channel.SendFileAsync(
                fileAttachment.Value,
                components: components,
                flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None,
                messageReference: new MessageReference(Context.Message.Id)
            ).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download avatar for {User} (URL: {AvatarUrl}) in prefix command",
                targetUser.Username, avatarUrl);
            await ReplyAsync($"Failed to download the avatar for {displayUserName}.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending avatar for {User} in prefix command", targetUser.Username);
            await ReplyAsync($"An error occurred while fetching the avatar for {displayUserName}.",
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