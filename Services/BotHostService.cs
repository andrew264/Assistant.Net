using System.Reflection;
using Assistant.Net.Configuration;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class BotHostService(
    DiscordSocketClient client,
    InteractionService interactionService,
    IServiceProvider serviceProvider,
    Config config,
    ILogger<BotHostService> logger,
    IAudioService audioService)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactionService.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;

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
            await client.LoginAsync(TokenType.Bot, config.Client.Token);
            await client.StartAsync();
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
        await client.LogoutAsync();
        await client.StopAsync();
        logger.LogInformation("Bot stopped.");
    }

    private async Task OnReadyAsync()
    {
        logger.LogInformation("Bot is Ready!");

        try
        {
            // Discover and register modules
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
            logger.LogInformation("Interaction modules loaded.");

            // Register commands globally or to test guilds
            if (config.Client.TestGuilds != null && config.Client.TestGuilds.Any())
                foreach (var guildId in config.Client.TestGuilds)
                    try
                    {
                        await interactionService.RegisterCommandsToGuildAsync(guildId);
                        logger.LogInformation("Registered commands to test guild {GuildId}", guildId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to register commands to test guild {GuildId}", guildId);
                    }
            else
                try
                {
                    await interactionService.RegisterCommandsGloballyAsync();
                    logger.LogInformation("Registered commands globally.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to register commands globally");
                }

            // Initialize Lavalink after Discord client is ready
            if (config.Lavalink.IsValid)
            {
                await audioService.WaitForReadyAsync(CancellationToken.None);
                logger.LogInformation("Lavalink4NET initialized.");
            }
            else
            {
                logger.LogWarning("Lavalink configuration not valid or missing. Music features may be disabled.");
            }

            // Set initial presence
            await SetBotStartPresenceAsync();  // TODO: doesnt work; should we delay it after startup?

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
                await client.SetActivityAsync(new Game(activityText, activityType));
            await client.SetStatusAsync(status);
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
            var result = await interactionService.ExecuteCommandAsync(context, serviceProvider);

            if (!result.IsSuccess)
            {
                logger.LogError("Interaction execution failed: {Error} | Reason: {Reason}", result.Error,
                    result.ErrorReason);
                try
                {
                    if (interaction.HasResponded)
                        await interaction.FollowupAsync($"Error: {result.ErrorReason}", ephemeral: true);
                    else
                        await interaction.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true);
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
                        ephemeral: true);
                else
                    await interaction.RespondAsync("An internal error occurred while processing your command.",
                        ephemeral: true);
            }
            catch (Exception iex)
            {
                logger.LogError(iex, "Failed to respond to interaction exception.");
            }
        }
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