namespace Assistant.Net.Services.Games.Models;

public class GameSessionTracker<T>
{
    public GameSessionTracker(T game, ulong channelId, ulong messageId)
    {
        Game = game;
        ChannelId = channelId;
        MessageId = messageId;
        LastInteractionTime = DateTimeOffset.UtcNow;
    }

    public T Game { get; }
    public ulong ChannelId { get; }
    public ulong MessageId { get; }
    public DateTimeOffset LastInteractionTime { get; private set; }

    public void RecordInteraction()
    {
        LastInteractionTime = DateTimeOffset.UtcNow;
    }
}