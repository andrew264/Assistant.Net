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
    ///     Downloads a file from the given URL and prepares it as a FileAttachment object.
    ///     The caller is responsible for disposing the FileAttachment if this method returns a non-null value.
    /// </summary>
    public static async Task<FileAttachment?> DownloadFileAsAttachmentAsync(
        string url,
        string fileName,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string httpClientName = "FileDownloader")
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning("DownloadFileAsAttachmentAsync: URL is null or empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logger.LogWarning("DownloadFileAsAttachmentAsync: FileName is null or empty for URL: {Url}", url);
            return null;
        }

        MemoryStream? memoryStream = null;
        try
        {
            using var httpClient = httpClientFactory.CreateClient(httpClientName);
            logger.LogTrace("Downloading file: {Url} as {FileName}", url, fileName);

            var response = await httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream).ConfigureAwait(false);
            memoryStream.Position = 0; // Reset stream position to the beginning for reading

            var fileAttachment = new FileAttachment(memoryStream, fileName);
            logger.LogDebug("Successfully downloaded and prepared file: {FileName} from {Url}", fileName, url);

            memoryStream = null;
            return fileAttachment;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download file from {Url} as {FileName}", url, fileName);
            if (memoryStream != null) await memoryStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred downloading file from {Url} as {FileName}", url,
                fileName);
            if (memoryStream != null) await memoryStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        finally
        {
            // If memoryStream was created but an exception occurred before it was passed to FileAttachment 
            // (or set to null), dispose it here.
            if (memoryStream != null) await memoryStream.DisposeAsync().ConfigureAwait(false);
        }
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