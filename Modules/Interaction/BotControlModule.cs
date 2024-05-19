using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Interaction;

[Group("change", "Change Bot's stuff")]
[CommandContextType([InteractionContextType.BotDm])]
public class BotControlModule : InteractionModuleBase<SocketInteractionContext>
{
    public required HttpClient _httpClient { get; set; }

    [RequireOwner]
    [SlashCommand("status", "Change Bot's Activity and Presence")]
    public async Task SetStatusAsync(
        [Summary(description: "Select Status")] UserStatus status,
        [Summary(description: "Select Activity Type")] ActivityType activityType,
        [Summary(description: "Enter Text")] string activity)
    {
        await Context.Client.SetGameAsync(activity, type: activityType);
        await Context.Client.SetStatusAsync(status);
        await RespondAsync("Status Changed!", ephemeral: true);
    }

    [RequireOwner]
    [SlashCommand("avatar", "Change Bot's Avatar")]
    public async Task ChangeAvatarAsync(
                [Summary(description: "Add an Image")] IAttachment? attachment = null,
                [Summary(description: "Enter Image URL")] string url = "")
    {

        if (string.IsNullOrEmpty(url) && attachment == null)
        {
            await RespondAsync("Please provide an image URL or attach an image!", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        try
        {
            using var stream = attachment != null ? await _httpClient.GetStreamAsync(attachment.Url) : await _httpClient.GetStreamAsync(url);
            await Context.Client.CurrentUser.ModifyAsync(x => x.Avatar = new Image(stream));
            await FollowupAsync("Avatar Changed!");
        }
        catch (Exception e)
        {
            await FollowupAsync($"Error: {e.Message}");
        }

    }

    [RequireOwner]
    [SlashCommand("banner", "Change Bot's Banner")]
    public async Task ChangeBannerAsync(
                       [Summary(description: "Add an Image")] IAttachment? attachment = null,
                       [Summary(description: "Enter Image URL")] string url = "")
    {
        if (string.IsNullOrEmpty(url) && attachment == null)
        {
            await RespondAsync("Please provide an image URL or attach an image!", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        try
        {
            using var stream = attachment != null ? await _httpClient.GetStreamAsync(attachment.Url) : await _httpClient.GetStreamAsync(url);
            await Context.Client.CurrentUser.ModifyAsync(x => x.Banner = new Image(stream));
            await FollowupAsync("Banner Changed!");
        }
        catch (Exception e)
        {
            await FollowupAsync($"Error: {e.Message}");
        }
    }
}
