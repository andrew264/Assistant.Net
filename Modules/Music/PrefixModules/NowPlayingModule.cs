using Assistant.Net.Services.Music;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.PrefixModules;

public class NowPlayingModule(
    NowPlayingService nowPlayingService,
    MusicService musicService,
    ILogger<NowPlayingModule> logger)
    : ModuleBase<SocketCommandContext>
{
    [Command("nowplaying")]
    [Alias("np")]
    [Summary("Displays the interactive Now Playing message.")]
    [RequireContext(ContextType.Guild)]
    public async Task NowPlayingCommandAsync()
    {
        var (player, _) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);

        if (player == null || player.CurrentTrack == null)
        {
            await ReplyAsync("I am not playing anything right now.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        if (Context.Channel is not ITextChannel textChannel)
        {
            await ReplyAsync("This command can only be used in a text channel.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var npMessage =
            await nowPlayingService.CreateOrReplaceNowPlayingMessageAsync(player, textChannel, Context.User)
                .ConfigureAwait(false);

        if (npMessage != null)
        {
            logger.LogInformation("Now Playing message created/updated via prefix command in Guild {GuildId} by {User}",
                Context.Guild.Id, Context.User.Username);
            try
            {
                await Context.Message.AddReactionAsync(new Emoji("✅")).ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }
        }
        else
        {
            await ReplyAsync("Failed to create or update the Now Playing message.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
    }
}