using System.Globalization;
using System.Text.RegularExpressions;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Admin.Interaction;

public partial class LoggingInteractionModule(LoggingConfigService configService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private async Task<List<LogSettingsEntity>> GetAllConfigsAsync()
    {
        var configs = new List<LogSettingsEntity>();
        foreach (var type in Enum.GetValues<LogType>())
        {
            var config = await configService.GetLogConfigAsync(Context.Guild.Id, type).ConfigureAwait(false);
            configs.Add(config);
        }

        return configs;
    }

    [ComponentInteraction(LoggingUiBuilder.IdConfigToggle + ":*")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleToggle(string typeStr)
    {
        if (!Enum.TryParse<LogType>(typeStr, out var logType)) return;

        await DeferAsync().ConfigureAwait(false);
        var config = await configService.GetLogConfigAsync(Context.Guild.Id, logType).ConfigureAwait(false);

        if (config.ChannelId == null && !config.IsEnabled)
        {
            await FollowupAsync("⚠️ Please select a log channel first before enabling.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        config.IsEnabled = !config.IsEnabled;
        await configService.UpdateLogConfigAsync(config).ConfigureAwait(false);

        var allConfigs = await GetAllConfigsAsync().ConfigureAwait(false);
        var components = LoggingUiBuilder.BuildDashboard(allConfigs);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(LoggingUiBuilder.IdConfigChannel + ":*")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleChannelSelect(string typeStr, string[] selectedChannels)
    {
        if (!Enum.TryParse<LogType>(typeStr, out var logType) || selectedChannels.Length == 0) return;
        if (!ulong.TryParse(selectedChannels[0], out var channelId)) return;

        await DeferAsync().ConfigureAwait(false);
        var config = await configService.GetLogConfigAsync(Context.Guild.Id, logType).ConfigureAwait(false);

        config.ChannelId = channelId;

        await configService.UpdateLogConfigAsync(config).ConfigureAwait(false);

        var allConfigs = await GetAllConfigsAsync().ConfigureAwait(false);
        var components = LoggingUiBuilder.BuildDashboard(allConfigs);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(LoggingUiBuilder.IdConfigDelay + ":*")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleDelayButton(string typeStr)
    {
        var modal = new ModalBuilder()
            .WithTitle("Auto-Delete Delay")
            .WithCustomId($"log:modal:delay:{typeStr}")
            .AddTextInput(
                "Duration (e.g. '24h', '7 days', '0')",
                "duration_input",
                placeholder: "Enter 0 or 'never' to disable auto-delete.",
                maxLength: 50)
            .Build();

        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ModalInteraction("log:modal:delay:*")]
    public async Task HandleDelayModalSubmit(string typeStr, LogDelayModal modal)
    {
        if (!Enum.TryParse<LogType>(typeStr, out var logType)) return;

        await DeferAsync().ConfigureAwait(false);

        var input = modal.DurationInput.Trim().ToLowerInvariant();
        int newDelayMs;

        if (input == "0" || input.Contains("never") || input.Contains("permanent") || input == "none")
        {
            newDelayMs = 0;
        }
        else
        {
            var parsedSeconds = ParseDurationToSeconds(input);
            if (parsedSeconds == null)
            {
                await FollowupAsync("Invalid time format. Try '1 day', '12 hours', '30m', or '0'.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            newDelayMs = (int)(parsedSeconds.Value * 1000);
        }

        var config = await configService.GetLogConfigAsync(Context.Guild.Id, logType).ConfigureAwait(false);
        config.DeleteDelayMs = newDelayMs;
        await configService.UpdateLogConfigAsync(config).ConfigureAwait(false);

        var allConfigs = await GetAllConfigsAsync().ConfigureAwait(false);
        var components = LoggingUiBuilder.BuildDashboard(allConfigs);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    private static double? ParseDurationToSeconds(string input)
    {
        var regex = DurationRegex();
        var matches = regex.Matches(input);

        if (matches.Count == 0) return null;

        double totalSeconds = 0;

        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var val))
                continue;

            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            var multiplier = 0;

            if (unit.StartsWith('d')) multiplier = 86400; // day, days
            else if (unit.StartsWith('h')) multiplier = 3600; // h, hr, hour
            else if (unit.StartsWith('m')) multiplier = 60; // m, min, minute
            else if (unit.StartsWith('s')) multiplier = 1; // s, sec, second

            totalSeconds += val * multiplier;
        }

        return totalSeconds > 0 ? totalSeconds : null;
    }

    [GeneratedRegex(@"(?<value>\d+(\.\d+)?)\s*(?<unit>[a-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-US")]
    private static partial Regex DurationRegex();

    public class LogDelayModal : IModal
    {
        [ModalTextInput("duration_input")] public string DurationInput { get; set; } = string.Empty;
        public string Title => "Set Delay";
    }
}