using Assistant.Net.Utils;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Net
{
    public class Program
    {
        private static DiscordSocketClient? _client;
        private static IServiceProvider? _services;

        private static readonly DiscordSocketConfig _socketConfig = new()
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = true,
        };


        public static async Task Main(string[] args)
        {

            _services = new ServiceCollection()
                .AddSingleton(_socketConfig)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                .AddSingleton<InteractionHandler>()
                .BuildServiceProvider();

            _client = _services.GetRequiredService<DiscordSocketClient>();

            _client.Log += LogAsync;

            var config = Config.LoadFromFile("config.toml");

            await _services.GetRequiredService<InteractionHandler>()
            .InitializeAsync();

            await _client.LoginAsync(TokenType.Bot, config.client.token);
            await _client.StartAsync();
            await _client.SetGameAsync(config.client.activity_text, type: config.client.getActivityType());
            await _client.SetStatusAsync(config.client.getStatus());

            await Task.Delay(Timeout.Infinite);

        }

        private static Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }
    }
}