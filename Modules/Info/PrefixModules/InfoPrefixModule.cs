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

            await Context.Channel.SendFileAsync(
                fileAttachment.Value,
                $"# {displayUserName}'s Avatar",
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

        var embed = await UserUtils.GenerateUserInfoEmbedAsync(targetUser, showSensitiveInfo, userService, client)
            .ConfigureAwait(false);

        var view = new ComponentBuilder()
            .WithButton("View Avatar", style: ButtonStyle.Link,
                url: targetUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? targetUser.GetDefaultAvatarUrl())
            .Build();

        await ReplyAsync(embed: embed, components: view, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }
}