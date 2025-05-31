using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Modules.Music.Logic.Player;
using Assistant.Net.Services.Music;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Base;

public abstract class MusicPrefixModuleBase(MusicService musicService, ILogger logger)
    : ModuleBase<SocketCommandContext>
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
        await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        return (null, true);
    }
}