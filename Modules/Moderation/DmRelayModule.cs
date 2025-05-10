using System.Text;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Net;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Moderation;

public class DmRelayModule(
    WebhookService webhookService,
    ILogger<DmRelayModule> logger,
    IHttpClientFactory httpClientFactory)
    : ModuleBase<SocketCommandContext>
{
    private const string MessageIdPrefix = "MSGID:";

    [Command("dm", RunMode = RunMode.Async)]
    [Summary("Sends a direct message to a user.")]
    [RequireOwner]
    public async Task DmCommandAsync(IUser user, [Remainder] string? msg = null)
    {
        if (string.IsNullOrWhiteSpace(msg) && Context.Message.Attachments.Count == 0)
        {
            await ReplyAsync("Please provide a message or attachment to send.", allowedMentions: AllowedMentions.None);
            return;
        }

        var files = new List<FileAttachment>();
        var tempStreams = new List<MemoryStream>();

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            foreach (var attachment in Context.Message.Attachments)
            {
                var response = await httpClient.GetAsync(attachment.Url);
                response.EnsureSuccessStatusCode();
                var memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                files.Add(new FileAttachment(memoryStream, attachment.Filename, attachment.Description,
                    attachment.IsSpoiler()));
                tempStreams.Add(memoryStream);
            }

            // Send the DM
            var dmChannel = await user.CreateDMChannelAsync();
            var sentMessage = await dmChannel.SendFilesAsync(files, msg, embeds: Context.Message.Embeds.ToArray());
            logger.LogInformation("[DM SENT by Owner Command] to {User} ({UserId}): {Content}", user, user.Id, msg);
            await Context.Message.AddReactionAsync(Emoji.Parse("✅"));
            tempStreams.Clear();

            await LogSentDmViaWebhookAsync(user, sentMessage);

            try
            {
                await Context.Message.DeleteAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete owner !dm command message {MessageId}", Context.Message.Id);
            }
        }
        catch (HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            logger.LogError(httpEx, "Failed to send !dm command to user {UserId} (User blocked bot or disabled DMs)",
                user.Id);
            await Context.Message.AddReactionAsync(Emoji.Parse("❌"));
            await ReplyAsync($"Failed to send DM to {user.Mention}. User might have DMs disabled or blocked the bot.",
                allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send !dm command to user {UserId}", user.Id);
            await Context.Message.AddReactionAsync(Emoji.Parse("❌"));
        }
        finally
        {
            foreach (var fa in files) fa.Dispose();
            foreach (var stream in tempStreams) await stream.DisposeAsync();
        }
    }

    private async Task LogSentDmViaWebhookAsync(IUser recipientUser, IUserMessage sentDmMessage)
    {
        var logFiles = new List<FileAttachment>();
        var logTempStreams = new List<MemoryStream>();

        try
        {
            if (sentDmMessage.Attachments.Count > 0)
            {
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var attachment in sentDmMessage.Attachments)
                {
                    var response = await httpClient.GetAsync(attachment.Url);
                    response.EnsureSuccessStatusCode();
                    var memoryStream = new MemoryStream();
                    await response.Content.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    logFiles.Add(new FileAttachment(memoryStream, attachment.Filename, attachment.Description,
                        attachment.IsSpoiler()));
                    logTempStreams.Add(memoryStream);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{MessageIdPrefix}{sentDmMessage.Id}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(sentDmMessage.Content))
            {
                sb.AppendLine("- Content:");
                sb.AppendLine($"```{DmRelayService.SanitizeCodeBlock(sentDmMessage.Content)}```");
                var urlMatch = RegexPatterns.Url().Match(sentDmMessage.Content);
                if (urlMatch.Success) sb.AppendLine($"URL: {urlMatch.Groups["url"].Value}");
            }

            sb.AppendLine("----------");
            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(Context.Channel.Id,
                WebhookService.DefaultWebhookName,
                Context.Client.CurrentUser.GetDisplayAvatarUrl() ?? Context.Client.CurrentUser.GetDefaultAvatarUrl());
            if (webhookClient == null)
            {
                logger.LogWarning("Could not get webhook to log !dm command for user {UserId}", recipientUser.Id);
                return;
            }

            await webhookClient.SendFilesAsync(logFiles, sb.ToString(),
                username: Context.User.Username,
                avatarUrl: Context.User.GetDisplayAvatarUrl() ?? Context.User.GetDefaultAvatarUrl());

            logTempStreams.Clear();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log owner !dm command to relay channel for user {UserId}", recipientUser.Id);
        }
        finally
        {
            foreach (var fa in logFiles) fa.Dispose();
            foreach (var stream in logTempStreams) await stream.DisposeAsync();
        }
    }
}