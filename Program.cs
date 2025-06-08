using System.Security.Authentication;
using Assistant.Net.Configuration;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Services.Games;
using Assistant.Net.Services.GuildFeatures;
using Assistant.Net.Services.GuildFeatures.Starboard;
using Assistant.Net.Services.Music;
using Assistant.Net.Services.User;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net;

public class Program
{
    public static async Task Main(string[] args)
    {
        await CreateHostBuilder(args).Build().RunAsync();
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
            })
            .ConfigureServices((_, services) =>
            {
                // --- Configuration ---
                services.AddSingleton<ConfigService>();
                services.AddSingleton(provider => provider.GetRequiredService<ConfigService>().Config);

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
                services.AddLavalink();
                services.ConfigureLavalink(options =>
                {
                    var config = services.BuildServiceProvider().GetRequiredService<ConfigService>().Config;
                    var logger = services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();

                    if (config.Lavalink.IsValid && !string.IsNullOrEmpty(config.Lavalink.Uri) &&
                        !string.IsNullOrEmpty(config.Lavalink.Password))
                    {
                        options.BaseAddress = new Uri(config.Lavalink.Uri);
                        options.Passphrase = config.Lavalink.Password;
                        options.ReadyTimeout = TimeSpan.FromSeconds(600);
                        logger.LogInformation("Lavalink configured: Uri={Uri}", options.BaseAddress);
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
                    options.DefaultPollInterval = TimeSpan.FromSeconds(10);
                });
                services.Configure<UsersInactivityTrackerOptions>(options =>
                {
                    options.Timeout = TimeSpan.FromSeconds(600);
                });


                // --- MongoDB ---
                services.AddSingleton<IMongoClient>(provider =>
                {
                    var config = provider.GetRequiredService<Config>().Mongo;
                    var logger = provider.GetRequiredService<ILogger<Program>>();
                    try
                    {
                        var connectionString = config.GetConnectionString();
                        var mongoSettings = MongoClientSettings.FromConnectionString(connectionString);
                        mongoSettings.ServerApi = new ServerApi(ServerApiVersion.V1);
                        mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(60);
                        mongoSettings.ConnectTimeout = TimeSpan.FromSeconds(60);

                        logger.LogInformation("Connecting to MongoDB...");
                        var client = new MongoClient(mongoSettings);

                        // Test connection
                        client.ListDatabaseNames(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                        logger.LogInformation("MongoDB connection successful.");

                        return client;
                    }
                    catch (TimeoutException tex)
                    {
                        logger.LogCritical(tex,
                            "MongoDB connection timed out. Check connection string, firewall, and server status.");
                        throw;
                    }
                    catch (AuthenticationException aex)
                    {
                        logger.LogCritical(aex, "MongoDB authentication failed. Check username/password.");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex, "Failed to configure MongoDB client.");
                        throw;
                    }
                });
                // Inject IMongoDatabase
                services.AddSingleton<IMongoDatabase>(provider =>
                {
                    var client = provider.GetRequiredService<IMongoClient>();
                    var config = provider.GetRequiredService<Config>().Mongo;
                    return client.GetDatabase(config.DatabaseName);
                });

                // --- Game Stats Service ---
                services.AddSingleton<GameStatsService>();
                services.AddSingleton<GameSessionService>();

                // --- Urban Dictionary Service ---
                services.AddSingleton<UrbanDictionaryService>();

                // --- Reddit Service ---
                services.AddSingleton<RedditService>();

                // --- User Service ---
                services.AddSingleton<UserService>();

                // --- User Activity Tracking Service ---
                services.AddSingleton<UserActivityTrackingService>();

                // --- Surveillance Service ---
                services.AddSingleton<SurveillanceService>();

                // --- Starboard Services ---
                // Config service
                services.AddSingleton<StarboardConfigService>();
                // Core service
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
                services.AddHostedService(provider => provider.GetRequiredService<ReminderService>());

                // --- Bot Host Service ---
                services.AddHostedService<BotHostService>();
            });
    }
}