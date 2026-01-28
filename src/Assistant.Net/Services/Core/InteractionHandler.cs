using System.Reflection;
using Assistant.Net.Options;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IResult = Discord.Interactions.IResult;

namespace Assistant.Net.Services.Core;

public class InteractionHandler(
    DiscordSocketClient client,
    InteractionService interactionService,
    CommandService commandService,
    IServiceProvider services,
    ILogger<InteractionHandler> logger,
    IOptions<DiscordOptions> discordOptions)
    : BackgroundService
{
    private readonly DiscordOptions _config = discordOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), services).ConfigureAwait(false);
        await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), services).ConfigureAwait(false);

        client.InteractionCreated += HandleInteraction;
        client.MessageReceived += HandleMessageReceived;
        client.Ready += OnReadyAsync;

        commandService.Log += LogAsync;
    }

    private async Task OnReadyAsync()
    {
        try
        {
            if (_config.TestGuilds.Count > 0)
            {
                foreach (var guildId in _config.TestGuilds)
                {
                    await interactionService.RegisterCommandsToGuildAsync(guildId).ConfigureAwait(false);
                    logger.LogInformation("Registered commands to test guild: {GuildId}", guildId);
                }
            }
            else
            {
                await interactionService.RegisterCommandsGloballyAsync().ConfigureAwait(false);
                logger.LogInformation("Registered commands globally.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register commands.");
        }
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            var result = await interactionService.ExecuteCommandAsync(context, services).ConfigureAwait(false);

            if (!result.IsSuccess)
                await HandleInteractionExecutionResult(interaction, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Uncaught exception in interaction handler.");
            if (interaction.Type is InteractionType.ApplicationCommand or InteractionType.MessageComponent)
            {
                if (!interaction.HasResponded)
                    await interaction.RespondAsync("An internal error occurred.", ephemeral: true)
                        .ConfigureAwait(false);
                else
                    await interaction.FollowupAsync("An internal error occurred.", ephemeral: true)
                        .ConfigureAwait(false);
            }
        }
    }

    private async Task HandleInteractionExecutionResult(IDiscordInteraction interaction, IResult result)
    {
        switch (result.Error)
        {
            case InteractionCommandError.UnmetPrecondition:
            case InteractionCommandError.BadArgs:
            case InteractionCommandError.ConvertFailed:
                logger.LogWarning("Interaction Warning: {Error} - {Reason}", result.Error, result.ErrorReason);
                break;
            default:
                logger.LogError("Interaction Error: {Error} - {Reason}", result.Error, result.ErrorReason);
                break;
        }

        // Inform user if possible
        if (!interaction.HasResponded)
            await interaction.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true).ConfigureAwait(false);
        else
            try
            {
                await interaction.FollowupAsync($"Error: {result.ErrorReason}", ephemeral: true).ConfigureAwait(false);
            }
            catch
            {
                // Ignore if followup fails (e.g. unknown interaction)
            }
    }

    private async Task HandleMessageReceived(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage { Source: MessageSource.User } message) return;

        var argPos = 0;
        var prefix = _config.Prefix;

        if (!(message.HasStringPrefix(prefix, ref argPos) ||
              message.HasMentionPrefix(client.CurrentUser, ref argPos)))
            return;

        var context = new SocketCommandContext(client, message);
        var result = await commandService.ExecuteAsync(context, argPos, services).ConfigureAwait(false);

        if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
        {
            logger.LogError("Prefix Command Error: {Error} - {Reason}", result.Error, result.ErrorReason);
            await context.Channel.SendMessageAsync($"Error: {result.ErrorReason}").ConfigureAwait(false);
        }
    }

    private Task LogAsync(LogMessage msg)
    {
        logger.LogInformation("[CommandService] {Message}", msg.Message);
        return Task.CompletedTask;
    }
}