using Assistant.Net.Data;
using Assistant.Net.Data.Repositories.Impl;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Options;
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
using Discord.Rest;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Cluster;
using Lavalink4NET.Cluster.Extensions;
using Lavalink4NET.Cluster.Nodes;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

var builder = new HostApplicationBuilder(args);

builder.Services.AddSystemd();

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", false, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
var loggerConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

Log.Logger = loggerConfiguration.CreateLogger();
builder.Logging.AddSerilog();

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<LavalinkOptions>(builder.Configuration.GetSection(LavalinkOptions.SectionName));
builder.Services.Configure<RedditOptions>(builder.Configuration.GetSection(RedditOptions.SectionName));
builder.Services.Configure<MusicOptions>(builder.Configuration.GetSection(MusicOptions.SectionName));
builder.Services.Configure<ExternalApiOptions>(builder.Configuration.GetSection(ExternalApiOptions.SectionName));

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildBans |
                     GatewayIntents.GuildVoiceStates | GatewayIntents.GuildMessages |
                     GatewayIntents.GuildMessageReactions | GatewayIntents.GuildMessageTyping |
                     GatewayIntents.DirectMessages | GatewayIntents.DirectMessageReactions |
                     GatewayIntents.DirectMessageTyping |
                     GatewayIntents.GuildMembers |
                     GatewayIntents.GuildPresences |
                     GatewayIntents.MessageContent,
    UseInteractionSnowflakeDate = false,
    AlwaysDownloadUsers = true,
    LogLevel = LogSeverity.Info,
    MessageCacheSize = 2000
});

builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton<IRestClientProvider>(x => x.GetRequiredService<DiscordSocketClient>());
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton(provider =>
    new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
        new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Verbose,
            UseCompiledLambda = true
        }));

builder.Services.AddDbContextFactory<AssistantDbContext>((provider, options) =>
{
    var dbOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    options.UseNpgsql(dbOptions.ConnectionString);
});

builder.Services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();

builder.Services.AddHttpClient().ConfigureHttpClientDefaults(defaults => defaults.RemoveAllLoggers());

builder.Services.AddMemoryCache();

var lavalinkSettings = builder.Configuration.GetSection(LavalinkOptions.SectionName).Get<LavalinkOptions>();

if (lavalinkSettings is { Nodes.Count: 1 })
{
    builder.Services.AddLavalink<DiscordClientWrapper>();
    builder.Services.AddOptions<AudioServiceOptions>()
        .Configure<IOptions<LavalinkOptions>>((options, appLavalinkOptionsWrapper) =>
        {
            var node = appLavalinkOptionsWrapper.Value.Nodes.First();
            options.BaseAddress = new Uri(node.Uri);
            options.Passphrase = node.Password;
            options.Label = node.Name;
            options.ReadyTimeout = TimeSpan.FromSeconds(30);
        });
}
else
{
    builder.Services.AddLavalinkCluster<DiscordClientWrapper>();
    builder.Services.AddOptions<ClusterAudioServiceOptions>()
        .Configure<IOptions<LavalinkOptions>>((clusterOptions, appLavalinkOptionsWrapper) =>
        {
            var appLavalinkOptions = appLavalinkOptionsWrapper.Value;
            var nodesList = new List<LavalinkClusterNodeOptions>();

            if (!appLavalinkOptions.IsValid)
            {
                Log.Warning("Lavalink configuration is invalid or empty. Music features will fail.");
                return;
            }

            nodesList.AddRange(appLavalinkOptions.Nodes.Select(node => new LavalinkClusterNodeOptions
                { BaseAddress = new Uri(node.Uri), Passphrase = node.Password, Label = node.Name }));

            clusterOptions.Nodes = [.. nodesList];
            clusterOptions.ReadyTimeout = TimeSpan.FromSeconds(30);
        });
}

// Data Services
builder.Services.AddSingleton<GameStatsService>();
builder.Services.AddSingleton<MusicHistoryService>();
builder.Services.AddSingleton<PlaylistService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<GuildService>();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.AddSingleton<PollService>();
builder.Services.AddSingleton<StarboardConfigService>();
builder.Services.AddSingleton<LoggingConfigService>();
builder.Services.AddSingleton<ReminderService>();
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<MusicService>();

// External APIs
builder.Services.AddSingleton<UrbanDictionaryService>();
builder.Services.AddSingleton<RedditService>();
builder.Services.AddSingleton<GeniusLyricsService>();

// Observers (Hooking Discord events)
builder.Services.AddHostedService<UserActivityTrackingService>();
builder.Services.AddHostedService<StarboardService>();
builder.Services.AddHostedService<MessageLogger>();
builder.Services.AddHostedService<UserLogger>();
builder.Services.AddHostedService<VoiceLogger>();
builder.Services.AddHostedService<PresenceLogger>();

// Hybrid Services
builder.Services.AddSingleton<DmRelayService>();
builder.Services.AddHostedService(p => p.GetRequiredService<DmRelayService>());

builder.Services.AddSingleton<NowPlayingService>();
builder.Services.AddHostedService(p => p.GetRequiredService<NowPlayingService>());

// Background Workers
builder.Services.AddHostedService<DiscordBotService>();
builder.Services.AddHostedService<InteractionHandler>();
builder.Services.AddHostedService<ReminderWorker>();

var app = builder.Build();

// Ensure DB Creation
using (var scope = app.Services.CreateScope())
{
    var provider = scope.ServiceProvider;
    var dbContext = provider.GetRequiredService<AssistantDbContext>();
    try
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to connect to or create the database.");
    }
}

await app.RunAsync();