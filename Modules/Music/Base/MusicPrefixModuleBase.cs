using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Base;

public abstract class MusicPrefixModuleBase : ModuleBase<SocketCommandContext>
{
    protected readonly ILogger Logger;
    protected readonly MusicService MusicService;

    protected MusicPrefixModuleBase(MusicService musicService, ILogger logger)
    {
        MusicService = musicService;
        Logger = logger;
    }

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
        await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        return (null, true);
    }
}