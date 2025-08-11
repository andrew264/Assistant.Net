using System.Reflection;
using Assistant.Net.Configuration;
using Assistant.Net.Services.GuildFeatures;
using Assistant.Net.Services.GuildFeatures.Starboard;
using Assistant.Net.Services.User;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IResult = Discord.Commands.IResult;

namespace Assistant.Net.Services.Core;

public class BotHostService(
    DiscordSocketClient client,
    InteractionService interactionService,
    CommandService commandService,
    IServiceProvider serviceProvider,
    Config config,
    ILogger<BotHostService> logger,
    IAudioService audioService,
    SurveillanceService surveillanceService,
    UserActivityTrackingService userActivityTrackingService,
    StarboardService starboardService)
    : IHostedService
{
    // ReSharper disable UnusedMember.Local
    private readonly SurveillanceService _surveillanceService = surveillanceService;
    private readonly UserActivityTrackingService _userActivityTrackingService = userActivityTrackingService;
    private readonly StarboardService _starboardService = starboardService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactionService.Log += LogAsync;
        commandService.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        client.MessageReceived += OnMessageReceivedAsync;
        commandService.CommandExecuted += OnCommandExecutedAsync;


        if (string.IsNullOrWhiteSpace(config.Client.Token))
        {
            logger.LogCritical("Bot token is missing. Cannot start.");
            // Request application shutdown
            var appLifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            appLifetime.StopApplication();
            return;
        }

        try
        {
            await client.LoginAsync(TokenType.Bot, config.Client.Token).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);
            logger.LogInformation("Bot started. Waiting for Ready event...");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to log in or start the bot.");
            var appLifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            appLifetime.StopApplication();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Bot stopping...");
        await client.LogoutAsync().ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false);
        logger.LogInformation("Bot stopped.");
    }

    private async Task OnReadyAsync()
    {
        logger.LogInformation("Bot is Ready!");

        try
        {
            // Discover and register modules
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider)
                .ConfigureAwait(false);
            logger.LogInformation("Interaction modules loaded.");

            await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider).ConfigureAwait(false);
            logger.LogInformation("Command Service modules loaded.");

            // Register commands globally or to test guilds
            if (config.Client.TestGuilds != null && config.Client.TestGuilds.Count != 0)
                foreach (var guildId in config.Client.TestGuilds)
                    try
                    {
                        await interactionService.RegisterCommandsToGuildAsync(guildId).ConfigureAwait(false);
                        logger.LogInformation("Registered commands to test guild {GuildId}", guildId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to register commands to test guild {GuildId}", guildId);
                    }
            else
                try
                {
                    await interactionService.RegisterCommandsGloballyAsync().ConfigureAwait(false);
                    logger.LogInformation("Registered commands globally.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to register commands globally");
                }

            // Initialize Lavalink after Discord client is ready
            if (config.Lavalink.IsValid)
            {
                await audioService.WaitForReadyAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("Lavalink4NET initialized.");
            }
            else
            {
                logger.LogWarning("Lavalink configuration not valid or missing. Music features may be disabled.");
            }

            // Set initial presence
            await SetBotStartPresenceAsync().ConfigureAwait(false);

            logger.LogInformation("Startup complete. Bot username: {Username}", client.CurrentUser.Username);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Error during OnReadyAsync setup.");
        }
    }

    private async Task SetBotStartPresenceAsync()
    {
        try
        {
            if (!Enum.TryParse<UserStatus>(config.Client.Status, true, out var status))
            {
                status = UserStatus.Online;
                logger.LogWarning("Failed to parse status '{Status}'. Defaulting to 'Online'.", config.Client.Status);
            }

            if (!Enum.TryParse<ActivityType>(config.Client.ActivityType, true, out var activityType))
            {
                activityType = ActivityType.Playing;
                logger.LogWarning("Failed to parse activity type '{ActivityType}'. Defaulting to 'Playing'.",
                    config.Client.ActivityType);
            }

            var activityText = config.Client.ActivityText;

            if (!string.IsNullOrEmpty(activityText))
                await client.SetActivityAsync(new Game(activityText, activityType)).ConfigureAwait(false);
            await client.SetStatusAsync(status).ConfigureAwait(false);
            logger.LogInformation("Set bot presence: Status={Status}, Activity={ActivityType} {ActivityText}", status,
                activityType, activityText ?? "None");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set initial bot presence.");
        }
    }


    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            var result = await interactionService.ExecuteCommandAsync(context, serviceProvider).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                logger.LogError("Interaction execution failed: {Error} | Reason: {Reason}", result.Error,
                    result.ErrorReason);
                try
                {
                    if (interaction.HasResponded)
                        await interaction.FollowupAsync($"Error: {result.ErrorReason}", ephemeral: true)
                            .ConfigureAwait(false);
                    else
                        await interaction.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true)
                            .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to respond to interaction error.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred during interaction handling.");
            try
            {
                if (interaction.HasResponded)
                    await interaction.FollowupAsync("An internal error occurred while processing your command.",
                        ephemeral: true).ConfigureAwait(false);
                else
                    await interaction.RespondAsync("An internal error occurred while processing your command.",
                        ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception iex)
            {
                logger.LogError(iex, "Failed to respond to interaction exception.");
            }
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage rawMessage)
    {
        // Ignore system messages, or messages from other bots
        if (rawMessage is not SocketUserMessage { Source: MessageSource.User } message)
            return;

        var argPos = 0;
        var prefix = config.Client.Prefix ?? "!";

        // Check for mention prefix or string prefix
        if (!(message.HasStringPrefix(prefix, ref argPos) ||
              message.HasMentionPrefix(client.CurrentUser, ref argPos)) ||
            message.Author.IsBot)
            return;

        var context = new SocketCommandContext(client, message);

        // Execute the command
        await commandService.ExecuteAsync(context, argPos, serviceProvider).ConfigureAwait(false);
    }

    private async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
    {
        // command not found
        if (!command.IsSpecified)
        {
            logger.LogTrace("Text command not found for message: {MessageContent} by {User}", context.Message.Content,
                context.User);
            return;
        }

        // command was successful
        if (result.IsSuccess)
        {
            logger.LogTrace("Text command '{CommandName}' executed successfully by {User}", command.Value.Name,
                context.User);
            return;
        }

        // command failed
        logger.LogError("Text command '{CommandName}' failed for {User}. Reason: {ErrorReason}", command.Value.Name,
            context.User, result.ErrorReason);
        await context.Channel.SendMessageAsync($"Error executing command: {result.ErrorReason}").ConfigureAwait(false);
    }


    private Task LogAsync(LogMessage log)
    {
        var severity = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        logger.Log(severity, log.Exception, "[{Source}] {Message}", log.Source, log.Message);
        return Task.CompletedTask;
    }
}