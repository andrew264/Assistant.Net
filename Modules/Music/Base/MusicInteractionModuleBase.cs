using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Base;

public abstract class MusicInteractionModuleBase(MusicService musicService, ILogger logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    protected readonly ILogger Logger = logger;
    protected readonly MusicService MusicService = musicService;

    protected async Task<(CustomPlayer? Player, bool IsError)> GetVerifiedPlayerAsync(
        PlayerChannelBehavior channelBehavior = PlayerChannelBehavior.None,
        MemberVoiceStateBehavior memberBehavior = MemberVoiceStateBehavior.Ignore,
        Func<PlayerRetrieveStatus, string>? customErrorMessageProvider = null)
    {
        var (player, retrieveStatus) = await MusicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, channelBehavior, memberBehavior).ConfigureAwait(false);

        if (player is not null) return (player, false);
        var errorMessage = customErrorMessageProvider?.Invoke(retrieveStatus)
                           ?? MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
        await RespondOrFollowupErrorAsync(errorMessage).ConfigureAwait(false);
        return (null, true);
    }

    protected async Task RespondOrFollowupAsync(string? text = null, bool ephemeral = false, Embed? embed = null,
        MessageComponent? components = null, AllowedMentions? allowedMentions = null, bool isError = false)
    {
        var effectiveEphemeral = ephemeral || isError;
        var embeds = embed != null ? new[] { embed } : null;
        var finalAllowedMentions = allowedMentions ?? AllowedMentions.None;

        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: effectiveEphemeral, embeds: embeds, components: components,
                allowedMentions: finalAllowedMentions).ConfigureAwait(false);
        else
            await RespondAsync(text, ephemeral: effectiveEphemeral, embed: embed, components: components,
                allowedMentions: finalAllowedMentions).ConfigureAwait(false);
    }

    protected async Task RespondOrFollowupErrorAsync(string errorMessage)
    {
        await RespondOrFollowupAsync(errorMessage, isError: true).ConfigureAwait(false);
    }
}