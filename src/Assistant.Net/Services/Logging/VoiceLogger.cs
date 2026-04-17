using System.Text;
using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class VoiceLogger(
    DiscordSocketClient client,
    ILogger<VoiceLogger> logger,
    WebhookService webhookService,
    LoggingConfigService loggingConfigService)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated += HandleVoiceStateUpdatedAsync;

        logger.LogInformation("VoiceLogger started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserVoiceStateUpdated -= HandleVoiceStateUpdatedAsync;

        logger.LogInformation("VoiceLogger stopped.");
        return Task.CompletedTask;
    }

    private Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot || user is not SocketGuildUser member) return;

            if (before.VoiceChannel?.Id == after.VoiceChannel?.Id &&
                before.IsMuted == after.IsMuted && before.IsDeafened == after.IsDeafened &&
                before.IsSelfMuted == after.IsSelfMuted && before.IsSelfDeafened == after.IsSelfDeafened &&
                before.IsStreaming == after.IsStreaming && before.IsVideoing == after.IsVideoing &&
                before.IsSuppressed == after.IsSuppressed)
                return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(member.Guild.Id, LogType.Voice)
                .ConfigureAwait(false);

            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
                .ConfigureAwait(false);
            if (webhookClient == null) return;

            var actionDescription = new StringBuilder();

            if (before.VoiceChannel?.Id != after.VoiceChannel?.Id)
            {
                if (after.VoiceChannel != null && before.VoiceChannel == null)
                    actionDescription.AppendLine(
                        $"➡️ Joined voice channel {after.VoiceChannel.Mention} (`{after.VoiceChannel.Name}`).");
                else if (before.VoiceChannel != null && after.VoiceChannel == null)
                    actionDescription.AppendLine(
                        $"⬅️ Left voice channel {before.VoiceChannel.Mention} (`{before.VoiceChannel.Name}`).");
                else if (before.VoiceChannel != null && after.VoiceChannel != null)
                    actionDescription.AppendLine(
                        $"🔄 Switched from {before.VoiceChannel.Mention} to {after.VoiceChannel.Mention}.");
            }

            if (before.IsMuted != after.IsMuted)
                actionDescription.AppendLine(after.IsMuted ? "🔇 Server Muted" : "🔊 Server Unmuted");
            if (before.IsDeafened != after.IsDeafened)
                actionDescription.AppendLine(after.IsDeafened ? "🔇 Server Deafened" : "🔊 Server Undeafened");
            if (before.IsSelfMuted != after.IsSelfMuted)
                actionDescription.AppendLine(after.IsSelfMuted ? "🎙️ Self-Muted" : "🎤 Self-Unmuted");
            if (before.IsSelfDeafened != after.IsSelfDeafened)
                actionDescription.AppendLine(after.IsSelfDeafened ? "🎧 Self-Deafened" : "🎶 Self-Undeafened");
            if (before.IsStreaming != after.IsStreaming)
                actionDescription.AppendLine(after.IsStreaming ? "🖥️ Started Streaming" : "🛑 Stopped Streaming");
            if (before.IsVideoing != after.IsVideoing)
                actionDescription.AppendLine(after.IsVideoing ? "📹 Camera On" : "🚫 Camera Off");

            if (actionDescription.Length == 0) return;

            var components = LogUiBuilder.BuildVoiceStateUpdateComponent(member, actionDescription.ToString());

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: member.DisplayName,
                    avatarUrl: member.GetDisplayAvatarUrl() ?? member.GetDefaultAvatarUrl(),
                    allowedMentions: AllowedMentions.None,
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                logger.LogInformation("[UPDATE] Voice {GuildName}: @{User}: {Action}", member.Guild.Name,
                    member.Username, actionDescription.ToString().Replace("\n", " "));

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send voice state update log via webhook for User {UserId}.", member.Id);
            }
        });
    }
}