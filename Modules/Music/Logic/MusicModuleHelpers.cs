using Lavalink4NET.Players;

namespace Assistant.Net.Modules.Music.Logic;

public static class MusicModuleHelpers
{
    public static string GetPlayerRetrieveErrorMessage(PlayerRetrieveStatus status) => status switch
    {
        PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
        PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected to a voice channel.",
        PlayerRetrieveStatus.VoiceChannelMismatch => "You must be in the same voice channel as the bot.",
        PlayerRetrieveStatus.PreconditionFailed => "The bot is already connected to a different voice channel.",
        _ => "An unknown error occurred while retrieving the player."
    };
}