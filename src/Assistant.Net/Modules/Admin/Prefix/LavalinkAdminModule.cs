using System.Text;
using Assistant.Net.Modules.Shared.Attributes;
using Discord.Commands;
using Lavalink4NET;
using Lavalink4NET.Cluster;
using Lavalink4NET.Rest;

namespace Assistant.Net.Modules.Admin.Prefix;

[RequireBotOwner]
[Group("lavalink")]
[Alias("ll")]
public class LavalinkAdminModule(IAudioService audioService) : ModuleBase<SocketCommandContext>
{
    [Command("status")]
    [Alias("nodes", "list")]
    [Summary("Lists configured Lavalink nodes and their status.")]
    [RequireBotOwner]
    public async Task ListNodesAsync()
    {
        var sb = new StringBuilder();

        if (audioService is IClusterAudioService clusterService)
        {
            sb.AppendLine("📡 **Lavalink Cluster Status:**");
            var nodes = clusterService.Nodes;

            if (nodes.Length == 0)
            {
                await ReplyAsync("No nodes configured in cluster.");
                return;
            }

            foreach (var node in nodes)
                await AppendNodeStatsAsync(sb, node.Label, node.ApiClient).ConfigureAwait(false);
        }
        else
        {
            sb.AppendLine("📡 **Lavalink Node Status:**");
            await AppendNodeStatsAsync(sb, "Primary Node", await audioService.ApiClientProvider.GetClientAsync())
                .ConfigureAwait(false);
        }

        await ReplyAsync(sb.ToString());
    }

    private static async Task AppendNodeStatsAsync(StringBuilder sb, string label, ILavalinkApiClient apiClient)
    {
        string statusEmoji;
        string loadInfo;

        try
        {
            var stats = await apiClient.RetrieveStatisticsAsync().ConfigureAwait(false);
            statusEmoji = "🟢";
            loadInfo =
                $"| Players: {stats.PlayingPlayers}/{stats.ConnectedPlayers} | CPU: {stats.ProcessorUsage.LavalinkLoad * 100:F1}% | RAM: {stats.MemoryUsage.UsedMemory / 1024 / 1024}MB";
        }
        catch
        {
            statusEmoji = "🔴";
            loadInfo = "| *Offline / Connection Refused*";
        }

        sb.AppendLine($"{statusEmoji} **{label}** ({apiClient.Endpoints.BaseAddress}) {loadInfo}");
    }
}