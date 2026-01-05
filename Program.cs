using Assistant.Net.Configuration;
using Assistant.Net.Data;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.Data;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Services.Features;
using Assistant.Net.Services.Games;
using Assistant.Net.Services.Logging;
using Assistant.Net.Services.Music;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET.Cluster.Extensions;
using Lavalink4NET.Cluster.Nodes;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
            try
            {
                await dbContext.Database.EnsureCreatedAsync();
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogCritical(ex, "Failed to connect to or create the database.");
            }
        }

        await host.RunAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureLogging((_, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();

                // Set minimum log level based on config
                var configService =
                    new ConfigService(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ConfigService>());
                var config = configService.Config;
                if (Enum.TryParse<LogLevel>(config.Client.LogLevel, true, out var logLevel))
                {
                    logging.SetMinimumLevel(logLevel);
                    Console.WriteLine($"Logging level set to: {logLevel}");
                }
                else
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                    Console.WriteLine(
                        $"Warning: Could not parse LogLevel '{config.Client.LogLevel}'. Defaulting to Information.");
                }

                logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            })
            .ConfigureServices((_, services) =>
            {
                // --- Configuration ---
                services.AddSingleton<ConfigService>();
                services.AddSingleton(provider => provider.GetRequiredService<ConfigService>().Config);

                // --- Database ---
                services.AddDbContextFactory<AssistantDbContext>((provider, options) =>
                {
                    var config = provider.GetRequiredService<Config>();
                    options.UseNpgsql(config.Database.ConnectionString);
                });

                // --- Discord ---
                var discordConfig = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildBans |
                                     GatewayIntents.GuildVoiceStates | GatewayIntents.GuildMessages |
                                     GatewayIntents.GuildMessageReactions | GatewayIntents.GuildMessageTyping |
                                     GatewayIntents.DirectMessages | GatewayIntents.DirectMessageReactions |
                                     GatewayIntents.DirectMessageTyping |
                                     GatewayIntents.GuildMembers | // for UserJoined, UserLeft, GuildMemberUpdated
                                     GatewayIntents.GuildPresences | // for PresenceUpdated
                                     GatewayIntents.MessageContent, // for Prefix Commands & Message Logging
                    UseInteractionSnowflakeDate = false,
                    AlwaysDownloadUsers = true,
                    LogLevel = LogSeverity.Info,
                    MessageCacheSize = 2000
                };
                services.AddSingleton(discordConfig);
                services.AddSingleton<DiscordSocketClient>();
                services.AddSingleton<CommandService>();
                services.AddSingleton<InteractionService>(provider =>
                    new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
                        new InteractionServiceConfig
                        {
                            LogLevel = LogSeverity.Verbose
                        }));

                // --- HTTP Client ---
                services.AddHttpClient().ConfigureHttpClientDefaults(defaults => defaults.RemoveAllLoggers());

                // --- Memory Cache ---
                services.AddMemoryCache();

                // --- Lavalink ---
                services.AddLavalinkCluster<DiscordClientWrapper>();
                services.ConfigureLavalinkCluster(options =>
                {
                    var config = services.BuildServiceProvider().GetRequiredService<ConfigService>().Config;
                    var logger = services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
                    var nodesList = new List<LavalinkClusterNodeOptions>();

                    if (config.Lavalink.IsValid)
                    {
                        nodesList.AddRange(config.Lavalink.Nodes.Select(node => new LavalinkClusterNodeOptions
                            { BaseAddress = new Uri(node.Uri), Passphrase = node.Password, Label = node.Name }));
                        options.Nodes = [..nodesList];
                        options.ReadyTimeout = TimeSpan.FromSeconds(10);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Lavalink configuration is invalid or incomplete. Music features may not work.");
                    }
                });
                services.AddInactivityTracking();
                services.ConfigureInactivityTracking(options =>
                {
                    options.InactivityBehavior = PlayerInactivityBehavior.None;
                    options.DefaultPollInterval = TimeSpan.FromSeconds(600);
                });
                services.Configure<UsersInactivityTrackerOptions>(options =>
                {
                    options.Timeout = TimeSpan.FromSeconds(600);
                });

                // --- Game Stats Service ---
                services.AddSingleton<GameStatsService>();
                services.AddSingleton<GameSessionService>();

                // --- Voting Service ---
                services.AddSingleton<PollService>();

                // --- Urban Dictionary Service ---
                services.AddSingleton<UrbanDictionaryService>();

                // --- Reddit Service ---
                services.AddSingleton<RedditService>();

                // --- User/Guild Service ---
                services.AddSingleton<UserService>();
                services.AddSingleton<GuildService>();

                // --- User Activity Tracking Service ---
                services.AddSingleton<UserActivityTrackingService>();

                // --- Surveillance / Logging Services ---
                services.AddSingleton<MessageLogger>();
                services.AddSingleton<UserLogger>();
                services.AddSingleton<VoiceLogger>();
                services.AddSingleton<PresenceLogger>();

                // --- Starboard Services ---
                services.AddSingleton<StarboardConfigService>();
                services.AddSingleton<StarboardService>();

                // --- Music History Service ---
                services.AddSingleton<MusicHistoryService>();

                // --- Music Service ---
                services.AddSingleton<MusicService>();
                services.AddSingleton<NowPlayingService>();
                services.AddSingleton<PlaylistService>();
                services.AddSingleton<GeniusLyricsService>();

                // --- Webhook Service ---
                services.AddSingleton<WebhookService>();

                // --- DM Relay Service ---
                services.AddSingleton<DmRelayService>();

                // --- Reminder Service ---
                services.AddSingleton<ReminderService>();
                services.AddHostedService<ReminderWorker>();

                // --- Bot Host Service ---
                services.AddHostedService<BotHostService>();
            });
    }
}