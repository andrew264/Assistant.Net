using Assistant.Net.Handlers;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assistant.Net;

public class Program
{
    private static DiscordSocketClient? _client;
    private static IHost? host;

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

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices((context, services) =>
        {
            services.AddSingleton(_socketConfig);
            services.AddSingleton(config);
            services.AddSingleton(_interactionConfig);
            services.AddSingleton<DiscordSocketClient>();
            services.AddSingleton<CommandService>();
            services.AddSingleton<PrefixHandler>();
            services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>()));
            services.AddSingleton<InteractionHandler>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton<UrbanDictionaryService>();
            services.AddSingleton<MicrosoftTranslatorService>();
            services.AddSingleton<RedditService>();
            services.AddSingleton<MongoDbService>();
            services.AddLavalink();
            services.ConfigureLavalink(options =>
            {
                options.Label = "Assistant.Net";
                options.HttpClientName = config.lavalink.host;
                options.Passphrase = config.lavalink.password;
                options.BaseAddress = new Uri($"http://{options.HttpClientName}:2333");
                options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });

        });
        host = builder.Build();

        _client = host.Services.GetRequiredService<DiscordSocketClient>();
        _client.Log += LogAsync;


        await host.Services.GetRequiredService<InteractionHandler>()
            .InitializeAsync();

        await host.Services.GetRequiredService<PrefixHandler>()
            .InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, config.client.token);
        await _client.StartAsync();
        await _client.SetGameAsync(config.client.activity_text, type: config.client.getActivityType());
        await _client.SetStatusAsync(config.client.getStatus());

        await host.RunAsync();

    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }
}