using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc.PrefixModules;

public class MiscModule(ILogger<MiscModule> logger, IHttpClientFactory httpClientFactory)
    : ModuleBase<SocketCommandContext>
{
    [Command("echo", RunMode = RunMode.Async)]
    [Summary("Echos the provided message back.")]
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public async Task EchoAsync([Remainder] string message = "")
    {
        var fileAttachments = new List<FileAttachment>();
        if (Context.Message.Attachments.Count > 0)
            fileAttachments =
                await AttachmentUtils.DownloadAttachmentsAsync(Context.Message.Attachments, httpClientFactory, logger)
                    .ConfigureAwait(false);

        var embeds = Context.Message.Embeds.Count > 0 ? Context.Message.Embeds.ToArray() : [];

        var messageReference = Context.Message.Reference;

        if (fileAttachments.Count == 0 && embeds.Length == 0 && message.Trim().Length == 0)
        {
            logger.LogInformation("Cannot echo empty message!");
            foreach (var fa in fileAttachments) fa.Dispose();
            return;
        }

        try
        {
            await Context.Channel.SendFilesAsync(
                fileAttachments,
                message,
                embeds: embeds,
                messageReference: messageReference,
                allowedMentions: AllowedMentions.None
            ).ConfigureAwait(false);
            logger.LogDebug("Echo command executed by {User} in {Guild}/{Channel}", Context.User,
                Context.Guild.Name, Context.Channel.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send echo message.");
            await ReplyAsync("Sorry, I couldn't send the echo message.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }
        finally
        {
            AttachmentUtils.DisposeFileAttachments(fileAttachments);
        }

        try
        {
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            logger.LogDebug("Deleted original echo command message.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete original echo command message.");
        }
    }
}