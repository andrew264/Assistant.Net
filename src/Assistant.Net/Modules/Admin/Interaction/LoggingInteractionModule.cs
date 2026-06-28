using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Admin.Interaction;

public class LoggingInteractionModule(LoggingConfigService configService)
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

    [ComponentInteraction(LoggingUiBuilder.IdConfigOpen + ":*")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleConfigOpenButton(string typeStr)
    {
        if (!Enum.TryParse<LogType>(typeStr, out var logType)) return;

        var config = await configService.GetLogConfigAsync(Context.Guild.Id, logType).ConfigureAwait(false);

        var currentDelayStr = config.DeleteDelayMs.ToString();
        string[] validDelays = ["0", "3600000", "43200000", "86400000", "604800000"];

        if (!validDelays.Contains(currentDelayStr))
            currentDelayStr = "86400000";

        var currentChannel = config.ChannelId.HasValue
            ? Context.Guild.GetChannel(config.ChannelId.Value)
            : null;

        var modal = new LoggingSettingsModal
        {
            Title = $"{typeStr} Logging",
            Channel = currentChannel != null ? [currentChannel] : null,
            IsEnabled = config.IsEnabled,
            Delay = currentDelayStr
        };

        await RespondWithModalAsync($"log:modal:cfg:{typeStr}", modal).ConfigureAwait(false);
    }

    [ModalInteraction("log:modal:cfg:*")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleConfigModalSubmit(string typeStr, LoggingSettingsModal modal)
    {
        if (!Enum.TryParse<LogType>(typeStr, out var logType)) return;

        await DeferAsync(true).ConfigureAwait(false);

        ulong? newChannelId = null;
        if (modal.Channel is { Length: > 0 })
        {
            var channel = modal.Channel[0];
            if (channel is not ITextChannel textChannel)
            {
                await FollowupAsync("❌ The selected channel is invalid or not a text channel.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var botUser = Context.Guild.CurrentUser;
            var perms = botUser.GetPermissions(textChannel);
            if (!perms.SendMessages || !perms.EmbedLinks || !perms.ViewChannel)
            {
                await FollowupAsync(
                    $"❌ I lack necessary permissions in {textChannel.Mention}.\nI need: `View Channel`, `Send Messages`, `Embed Links`.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            newChannelId = textChannel.Id;
        }

        if (modal.IsEnabled && newChannelId == null)
        {
            await FollowupAsync("⚠️ You must select a Log Channel if you want to enable this module.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(modal.Delay, out var newDelayMs))
        {
            await FollowupAsync("❌ Invalid delay value.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var config = await configService.GetLogConfigAsync(Context.Guild.Id, logType).ConfigureAwait(false);

        config.IsEnabled = modal.IsEnabled;
        config.ChannelId = newChannelId;
        config.DeleteDelayMs = newDelayMs;

        await configService.UpdateLogConfigAsync(config).ConfigureAwait(false);

        // Refresh the dashboard message
        var allConfigs = await GetAllConfigsAsync().ConfigureAwait(false);
        var components = LoggingUiBuilder.BuildDashboard(allConfigs);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    public class LoggingSettingsModal : IModal
    {
        [RequiredInput(false)]
        [ModalChannelSelect("channel_id")]
        public IChannel[]? Channel { get; set; }

        [ModalCheckbox("is_enabled")]
        [InputLabel("Enable this logging module")]
        public bool IsEnabled { get; set; }

        [RequiredInput]
        [ModalRadioGroup("delay")]
        [ModalRadioGroupOption("Permanent (No Auto-Delete)", "0")]
        [ModalRadioGroupOption("1 Hour", "3600000")]
        [ModalRadioGroupOption("12 Hours", "43200000")]
        [ModalRadioGroupOption("24 Hours", "86400000")]
        [ModalRadioGroupOption("1 Week", "604800000")]
        public string Delay { get; set; } = string.Empty;

        public string Title { get; set; } = "Logging Configuration";
    }
}