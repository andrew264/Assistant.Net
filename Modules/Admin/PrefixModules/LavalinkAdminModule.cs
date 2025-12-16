using Assistant.Net.Modules.Attributes;
using Discord.Commands;
using Lavalink4NET;
using Lavalink4NET.Cluster;

namespace Assistant.Net.Modules.Admin.PrefixModules;

[RequireBotOwner]
[Group("lavalink")]
[Alias("ll")]
public class LavalinkAdminModule(IAudioService audioService) : ModuleBase<SocketCommandContext>
{
    private IClusterAudioService ClusterService =>
        audioService as IClusterAudioService ??
        throw new InvalidOperationException("Audio service is not a cluster service.");

    [Command("status")]
    [Alias("nodes", "list")]
    [Summary("Lists all configured Lavalink nodes and their status.")]
    [RequireBotOwner]
    public async Task ListNodesAsync()
    {
        var nodes = ClusterService.Nodes;
        var response = "📡 **Lavalink Nodes Status:**\n";

        if (nodes.Length == 0)
        {
            await ReplyAsync("No nodes configured.");
            return;
        }

        foreach (var node in nodes)
        {
            string statusEmoji;
            string loadInfo;

            try
            {
                var stats = await node.ApiClient.RetrieveStatisticsAsync().ConfigureAwait(false);
                statusEmoji = "🟢";
                loadInfo =
                    $"| Players: {stats.PlayingPlayers}/{stats.ConnectedPlayers} | CPU: {stats.ProcessorUsage.LavalinkLoad * 100:F1}% | RAM: {stats.MemoryUsage.UsedMemory / 1024 / 1024}MB";
            }
            catch
            {
                statusEmoji = "🔴";
                loadInfo = "| *Offline / Connection Refused*";
            }

            response += $"{statusEmoji} **{node.Label}** ({node.ApiClient.Endpoints.BaseAddress}) {loadInfo}\n";
        }

        await ReplyAsync(response);
    }
}