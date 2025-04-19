using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc;

public class MiscModule(ILogger<MiscModule> logger, HttpClient httpClient) : ModuleBase<SocketCommandContext>
{
    [Command("echo", RunMode = RunMode.Async)]
    [Summary("Echos the provided message back.")]
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public async Task EchoAsync([Remainder] string message = "")
    {
        // 1. Prepare Attachments
        var fileAttachments = new List<FileAttachment>();
        if (Context.Message.Attachments.Count > 0)
        {
            logger.LogDebug("Processing {AttachmentCount} attachments for echo command.",
                Context.Message.Attachments.Count);
            foreach (var attachment in Context.Message.Attachments)
            {
                MemoryStream? memoryStream = null;
                try
                {
                    // Download the attachment content
                    using var response = await httpClient.GetAsync(attachment.Url);
                    response.EnsureSuccessStatusCode();

                    memoryStream = new MemoryStream();
                    await response.Content.CopyToAsync(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    // Create FileAttachment (needs a Stream)
                    fileAttachments.Add(new FileAttachment(memoryStream, attachment.Filename, attachment.Description,
                        attachment.IsSpoiler()));
                    logger.LogDebug("Added attachment: {FileName}", attachment.Filename);

                    memoryStream = null;
                }
                catch (HttpRequestException ex)
                {
                    logger.LogError(ex, "Failed to download attachment: {AttachmentUrl}", attachment.Url);
                    await ReplyAsync($"Failed to download attachment: {attachment.Filename}",
                        allowedMentions: AllowedMentions.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An unexpected error occurred processing attachment: {AttachmentUrl}",
                        attachment.Url);
                    await ReplyAsync($"Error processing attachment: {attachment.Filename}",
                        allowedMentions: AllowedMentions.None);
                }
                finally
                {
                    if (memoryStream != null)
                        await memoryStream.DisposeAsync();
                }
            }
        }

        // 2. Get Embeds
        var embeds = Context.Message.Embeds.Count > 0 ? Context.Message.Embeds.ToArray() : [];

        // 3. Get Message Reference
        var messageReference = Context.Message.Reference;

        // Check
        if (fileAttachments.Count == 0 && embeds.Length == 0 && message.Trim().Length == 0)
        {
            logger.LogInformation("Cannot echo empty message!");
            foreach (var fa in fileAttachments) fa.Dispose();
            return;
        }

        // 4. Send the Echoed Message
        try
        {
            await Context.Channel.SendFilesAsync(
                fileAttachments,
                message,
                embeds: embeds,
                messageReference: messageReference,
                allowedMentions: AllowedMentions.None
            );
            logger.LogDebug("Echo command executed by {User} in {Guild}/{Channel}", Context.User,
                Context.Guild.Name, Context.Channel.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send echo message.");
            await ReplyAsync("Sorry, I couldn't send the echo message.", allowedMentions: AllowedMentions.None);
        }
        finally
        {
            foreach (var fa in fileAttachments) fa.Dispose();
        }


        // 5. Delete the Original Command Message
        try
        {
            await Context.Message.DeleteAsync();
            logger.LogDebug("Deleted original echo command message.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete original echo command message.");
        }
    }
}