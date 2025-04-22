using System.Security.Authentication;
using Assistant.Net.Configuration;
using Assistant.Net.Services;
using Assistant.Net.Services.Starboard;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.InactivityTracking.Extensions;
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
            .ConfigureLogging((context, logging) =>
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
            })
            .ConfigureServices((hostContext, services) =>
            {
                // --- Configuration ---
                services.AddSingleton<ConfigService>();
                services.AddSingleton(provider => provider.GetRequiredService<ConfigService>().Config);

                // --- Discord ---
                var discordConfig = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.All, // ALL Intents
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
                services.AddHttpClient();

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
                        options.ReadyTimeout = TimeSpan.FromSeconds(20);
                        logger.LogInformation("Lavalink configured: Uri={Uri}", options.BaseAddress);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Lavalink configuration is invalid or incomplete. Music features may not work.");
                    }
                });
                services.ConfigureInactivityTracking(options =>
                {
                    options.DefaultTimeout = TimeSpan.FromSeconds(30);
                    options.InactivityBehavior = PlayerInactivityBehavior.Pause;
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

                // --- Urban Dictionary Service ---
                services.AddSingleton<UrbanDictionaryService>();

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

                // --- Bot Host Service ---
                services.AddHostedService<BotHostService>();
            });
    }
}