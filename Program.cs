using Assistant.Net.Handlers;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.Integrations.LyricsJava.Extensions;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assistant.Net;

public class Program
{
    private static DiscordSocketClient? _client;
    private static IHost? app;

    private static readonly DiscordSocketConfig _socketConfig = new()
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent,
        AlwaysDownloadUsers = true,
        MessageCacheSize = 1000,
    };

    private static readonly InteractionServiceConfig _interactionConfig = new()
    {
        DefaultRunMode = Discord.Interactions.RunMode.Async,
    };


    public static async Task Main(string[] args)
    {
        var config = BotConfig.LoadFromFile("config.toml");

        HostApplicationBuilder builder = new(args);
        builder.Services.AddSingleton(_socketConfig);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(_interactionConfig);
        builder.Services.AddSingleton<DiscordSocketClient>();
        builder.Services.AddSingleton<CommandService>();
        builder.Services.AddSingleton<PrefixHandler>();
        builder.Services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>()));
        builder.Services.AddSingleton<InteractionHandler>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<UrbanDictionaryService>();
        builder.Services.AddSingleton<MicrosoftTranslatorService>();
        builder.Services.AddSingleton<RedditService>();
        builder.Services.AddSingleton<MongoDbService>();
        builder.Services.AddLavalink();
        builder.Services.ConfigureLavalink(options =>
        {
            options.Label = "Assistant.Net";
            options.HttpClientName = config.lavalink.host;
            options.Passphrase = config.lavalink.password;
            options.BaseAddress = new Uri($"http://{options.HttpClientName}:2333");
            options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
        });
        app = builder.Build();

        _client = app.Services.GetRequiredService<DiscordSocketClient>();
        _client.Log += LogAsync;

        app.UseLyricsJava();
        app.UseSponsorBlock();


        await app.Services.GetRequiredService<InteractionHandler>()
            .InitializeAsync();

        await app.Services.GetRequiredService<PrefixHandler>()
            .InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, config.client.token);
        await _client.StartAsync();
        await _client.SetGameAsync(config.client.activity_text, type: config.client.getActivityType());
        await _client.SetStatusAsync(config.client.getStatus());

        await app.RunAsync();

    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }
}