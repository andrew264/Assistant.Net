using Discord;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Utilities;

public static class AttachmentUtils
{
    /// <summary>
    ///     Downloads attachments from the given URLs and prepares them as FileAttachment objects.
    ///     The caller is responsible for disposing the FileAttachment objects in the returned list.
    /// </summary>
    public static async Task<List<FileAttachment>> DownloadAttachmentsAsync(
        IEnumerable<IAttachment> attachments,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string httpClientName = "AttachmentDownloader")
    {
        var fileAttachments = new List<FileAttachment>();
        var attachmentsArray = attachments as IAttachment[] ?? attachments.ToArray();
        if (attachmentsArray.Length == 0) return fileAttachments;

        using var httpClient = httpClientFactory.CreateClient(httpClientName);

        foreach (var attachment in attachmentsArray)
        {
            MemoryStream? memoryStream = null;
            try
            {
                logger.LogTrace("Downloading attachment: {AttachmentUrl} ({FileName})", attachment.Url,
                    attachment.Filename);
                var response = await httpClient.GetAsync(attachment.Url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                memoryStream = new MemoryStream();
                await response.Content.CopyToAsync(memoryStream).ConfigureAwait(false);
                memoryStream.Position = 0;

                fileAttachments.Add(new FileAttachment(memoryStream, attachment.Filename, attachment.Description,
                    attachment.IsSpoiler()));
                logger.LogDebug("Successfully downloaded and prepared attachment: {FileName}", attachment.Filename);
                memoryStream = null;
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Failed to download attachment: {AttachmentUrl} ({FileName})", attachment.Url,
                    attachment.Filename);
                if (memoryStream != null) await memoryStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred processing attachment: {AttachmentUrl} ({FileName})",
                    attachment.Url, attachment.Filename);
                if (memoryStream != null) await memoryStream.DisposeAsync().ConfigureAwait(false);
            }
        }

        return fileAttachments;
    }

    /// <summary>
    ///     Disposes all FileAttachment objects in the list and clears the list.
    /// </summary>
    public static void DisposeFileAttachments(List<FileAttachment> attachments)
    {
        foreach (var fa in attachments) fa.Dispose();
        attachments.Clear();
    }
}