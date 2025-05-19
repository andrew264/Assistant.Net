using System.Net;
using Assistant.Net.Configuration;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Admin;

[Group("admin", "Admin-only commands for managing the bot.")]
[RequireOwner]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class AdminModule(
    DiscordSocketClient client,
    ILogger<AdminModule> logger,
    IHttpClientFactory httpClientFactory,
    Config config)
    : InteractionModuleBase<SocketInteractionContext>
{
    private Task LogRateLimitInfo(IRateLimitInfo info)
    {
        logger.LogWarning(
            "Rate limit hit during avatar update. Global: {IsGlobal}, Limit: {Limit}, Remaining: {Remaining}, RetryAfter: {RetryAfter}s, Bucket: {Bucket}",
            info.IsGlobal, info.Limit, info.Remaining, info.RetryAfter, info.Bucket);
        return Task.CompletedTask;
    }


    [SlashCommand("update-avatar", "Updates the bot's avatar.")]
    public async Task UpdateAvatarAsync(
        [Summary("image", "The image file to use as the new avatar.")]
        IAttachment imageAttachment)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (imageAttachment.ContentType == null ||
            !(imageAttachment.ContentType.StartsWith("image/jpeg") ||
              imageAttachment.ContentType.StartsWith("image/png") ||
              imageAttachment.ContentType.StartsWith("image/gif")))
        {
            await FollowupAsync("The attached file does not appear to be a supported image type (JPEG, PNG, GIF).",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (imageAttachment.Size > 10 * 1024 * 1024) // 10 MB limit
        {
            await FollowupAsync("The image file is too large (max 10MB).", ephemeral: true).ConfigureAwait(false);
            return;
        }

        logger.LogInformation("Attempting to update bot avatar initiated by {User}", Context.User);

        // --- Download and Update ---
        Stream? imageStream = null;
        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(imageAttachment.Url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var image = new Image(imageStream);

            // Optional: Add RequestOptions for detailed logging
            var requestOptions = new RequestOptions
            {
                RatelimitCallback = LogRateLimitInfo,
                AuditLogReason = $"Avatar updated by {Context.User} ({Context.User.Id})"
            };

            await client.CurrentUser.ModifyAsync(props => props.Avatar = image, requestOptions).ConfigureAwait(false);

            await FollowupAsync("Bot avatar updated successfully!", ephemeral: true).ConfigureAwait(false);
            logger.LogInformation("Bot avatar successfully updated by {User}", Context.User);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download the image from URL: {ImageUrl}", imageAttachment.Url);
            await FollowupAsync("Failed to download the image. Please ensure the link is valid and accessible.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(ex, "Rate limited while trying to update avatar.");
            await FollowupAsync("I'm being rate limited by Discord. Please wait a moment and try again.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.BadRequest)
        {
            logger.LogError(ex,
                "Discord API rejected the avatar update (BadRequest/Invalid Form Body). Possible image format/size issue. Attachment: {AttachmentInfo}",
                imageAttachment.Filename);
            await FollowupAsync(
                "Discord rejected the image. It might be corrupted, in an unsupported format, or exceed dimension limits.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            logger.LogError(ex,
                "Discord API error occurred while updating avatar. Code: {DiscordCode}, Reason: {Reason}",
                ex.DiscordCode, ex.Reason);
            await FollowupAsync($"An error occurred while communicating with Discord: {ex.Reason ?? "Unknown error"}",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating the bot avatar.");
            await FollowupAsync("An unexpected error occurred. Check the logs for details.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            if (imageStream != null)
                await imageStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    [SlashCommand("update-status", "Updates the bot's status and activity.")]
    public async Task UpdateStatusAsync(
        [Summary("status", "The online status (e.g., Online, Idle, Dnd). Defaults to config value.")]
        UserStatus? status = null,
        [Summary("activity-type", "The type of activity (e.g., Playing, Listening). Defaults to config value.")]
        ActivityType? activityType = null,
        [Summary("activity-text", "The text for the activity (e.g., 'with code'). Defaults to config value.")]
        string? activityText = null)
    {
        await DeferAsync(true).ConfigureAwait(false);

        var finalStatus = status ?? (Enum.TryParse<UserStatus>(config.Client.Status, true, out var cfgStatus)
            ? cfgStatus
            : UserStatus.Online);

        var finalActivityType = activityType ??
                                (Enum.TryParse<ActivityType>(config.Client.ActivityType, true, out var cfgActivityType)
                                    ? cfgActivityType
                                    : ActivityType.Playing);

        var finalActivityText = !string.IsNullOrWhiteSpace(activityText)
            ? activityText
            : !string.IsNullOrWhiteSpace(config.Client.ActivityText)
                ? config.Client.ActivityText
                : null;

        logger.LogInformation(
            "Attempting to update bot presence initiated by {User}: Status={Status}, Activity={ActivityType} {ActivityText}",
            Context.User, finalStatus, finalActivityType, finalActivityText ?? "None");

        try
        {
            await Context.Client.SetStatusAsync(finalStatus).ConfigureAwait(false);
            logger.LogDebug("Set bot status to {Status}", finalStatus);

            if (!string.IsNullOrWhiteSpace(finalActivityText))
            {
                await Context.Client.SetActivityAsync(new Game(finalActivityText, finalActivityType)).ConfigureAwait(false);
                logger.LogDebug("Set bot activity to {ActivityType} {ActivityText}", finalActivityType,
                    finalActivityText);
            }
            else
            {
                await Context.Client.SetActivityAsync(null).ConfigureAwait(false);
                logger.LogDebug("Cleared bot activity.");
            }

            await FollowupAsync(
                $"Bot presence updated successfully: Status=`{finalStatus}`, Activity=`{finalActivityType} {finalActivityText ?? "None"}`",
                ephemeral: true).ConfigureAwait(false);
            logger.LogInformation("Bot presence successfully updated by {User}", Context.User);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update bot presence.");
            await FollowupAsync("An error occurred while updating the bot's presence. Check logs for details.",
                ephemeral: true).ConfigureAwait(false);
        }
    }
}