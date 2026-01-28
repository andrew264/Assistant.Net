using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Commands;

namespace Assistant.Net.Modules.Admin.Prefix;

public class LoggingModule(LoggingConfigService loggingConfigService) : ModuleBase<SocketCommandContext>
{
    [Command("logging")]
    [Alias("logconfig", "logs")]
    [Summary("Opens the logging configuration dashboard.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireContext(ContextType.Guild)]
    public async Task LoggingCommandAsync()
    {
        var configs = new List<LogSettingsEntity>();

        foreach (var type in Enum.GetValues<LogType>())
        {
            var config = await loggingConfigService.GetLogConfigAsync(Context.Guild.Id, type).ConfigureAwait(false);
            configs.Add(config);
        }

        var components = LoggingUiBuilder.BuildDashboard(configs);

        await ReplyAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }
}