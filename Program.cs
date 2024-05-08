using Assistant.Net.Utils;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assistant.Net
{
    public class Program
    {
        private static DiscordSocketClient? _client;

        private static readonly DiscordSocketConfig _socketConfig = new()
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = true,
        };


        public static async Task Main(string[] args)
        {

            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
            services
            .AddSingleton(x => new DiscordSocketClient(_socketConfig)))
            .Build();

            using IServiceScope serviceScope = host.Services.CreateScope();
            IServiceProvider provider = serviceScope.ServiceProvider;

            _client = provider.GetRequiredService<DiscordSocketClient>();

            _client.Log += LogAsync;

            var config = Config.LoadFromFile("config.toml");

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