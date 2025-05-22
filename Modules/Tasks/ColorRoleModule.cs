using System.Net;
using Assistant.Net.Configuration;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Tasks;

public class ColorRoleModule(Config config, ILogger<ColorRoleModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Base Custom ID prefix
    private const string CustomIdPrefix = "assistant:color:";

    // --- Configuration ---
    private static readonly Dictionary<string, ulong> ColorRolesMap = new()
    {
        { "red", 891766305470971984 },
        { "blue", 891766503219798026 },
        { "green", 891766413721759764 },
        { "brown", 891782414412697600 },
        { "orange", 891783123711455292 },
        { "purple", 891782622374678658 },
        { "yellow", 891782804008992848 }
    };

    // Mapping: "custom_id_suffix" -> (Emoji, Row)
    private static readonly Dictionary<string, (string Emoji, int Row)> ButtonLayout = new()
    {
        { "red", ("🟥", 0) },
        { "blue", ("🟦", 0) },
        { "green", ("🟩", 0) },
        { "brown", ("🟫", 0) },
        { "orange", ("🟧", 1) },
        { "purple", ("🟪", 1) },
        { "yellow", ("🟨", 1) }
    };

    // --- Setup Command ---
    [SlashCommand("colorrolesetup", "Set up the color reaction roles message.")]
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public async Task SetupColorRolesAsync()
    {
        if (Context.Guild.Id != config.Client.HomeGuildId)
        {
            await RespondAsync("This command can only be used in the home server.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Color Roles")
            .WithDescription("Claim a colour of your choice!")
            .WithColor(Color.LightGrey)
            .Build();

        var components = new ComponentBuilder();
        foreach (var (colorSuffix, value) in ButtonLayout)
        {
            var (emoji, row) = value;

            if (ColorRolesMap.ContainsKey(colorSuffix))
                components.WithButton(
                    emote: Emoji.Parse(emoji),
                    customId: $"{CustomIdPrefix}{colorSuffix}",
                    style: ButtonStyle.Secondary,
                    row: row
                );
            else
                logger.LogWarning("Color role setup skipped button for undefined color suffix: {ColorSuffix}",
                    colorSuffix);
        }

        await RespondAsync("Setting up color roles message...", ephemeral: true).ConfigureAwait(false);
        await Context.Channel.SendMessageAsync(embed: embed, components: components.Build()).ConfigureAwait(false);

        logger.LogInformation("Color roles message deployed in Home Guild {GuildId}, Channel {ChannelId} by {User}",
            Context.Guild.Id, Context.Channel.Id, Context.User);
    }

    // --- Button Interaction Handler ---
    [ComponentInteraction("assistant:color:*", true)]
    public async Task HandleColorRoleButtonAsync()
    {
        if (Context.Interaction is not SocketMessageComponent interaction) return;

        // --- Guild Check ---
        if (Context.Guild == null || Context.Guild.Id != config.Client.HomeGuildId)
        {
            await interaction.RespondAsync("This feature is only available in the designated home server.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        // --- User & Guild Info ---
        var user = Context.User as SocketGuildUser;
        var guild = Context.Guild;
        if (user == null)
        {
            await interaction.RespondAsync("Could not retrieve your user information within the server.",
                ephemeral: true).ConfigureAwait(false);
            logger.LogWarning(
                "Could not cast Context.User to SocketGuildUser in color role handler for interaction {InteractionId}",
                interaction.Id);
            return;
        }

        // --- Role Selection ---
        var selectedCustomId = interaction.Data.CustomId;
        if (!selectedCustomId.StartsWith(CustomIdPrefix))
        {
            await interaction.RespondAsync("Invalid button ID format.", ephemeral: true).ConfigureAwait(false);
            logger.LogError("Received unexpected custom ID format '{CustomId}' in color role handler.",
                selectedCustomId);
            return;
        }

        var colorSuffix = selectedCustomId[CustomIdPrefix.Length..];

        if (!ColorRolesMap.TryGetValue(colorSuffix, out var selectedRoleId))
        {
            await interaction.RespondAsync("Invalid role selection identifier.", ephemeral: true).ConfigureAwait(false);
            logger.LogError(
                "Invalid color suffix '{ColorSuffix}' derived from custom ID '{CustomId}'. No mapping found.",
                colorSuffix, selectedCustomId);
            return;
        }

        var roleToAdd = guild.GetRole(selectedRoleId);
        if (roleToAdd == null)
        {
            await interaction.RespondAsync($"The role for '{colorSuffix}' seems to be missing on this server.",
                ephemeral: true).ConfigureAwait(false);
            logger.LogError("Color role with ID {RoleId} (Suffix: {ColorSuffix}) not found in Home Guild {GuildId}.",
                selectedRoleId, colorSuffix, guild.Id);
            return;
        }

        // --- Role Management Logic ---
        await interaction.DeferAsync(true).ConfigureAwait(false);

        var currentRoleIds = user.Roles.Select(r => r.Id).ToHashSet();
        var managedRoleIds = ColorRolesMap.Values.ToHashSet();

        // Identify existing managed roles the user has
        var rolesToRemove = (from currentRoleId in currentRoleIds
            where managedRoleIds.Contains(currentRoleId)
            select guild.GetRole(currentRoleId)).ToList();

        try
        {
            var alreadyHadRole = currentRoleIds.Contains(roleToAdd.Id);
            var rolesBeingRemoved = rolesToRemove.Where(r => r.Id != roleToAdd.Id).ToList();

            // Remove other managed roles
            if (rolesBeingRemoved.Count != 0)
            {
                await user.RemoveRolesAsync(rolesBeingRemoved).ConfigureAwait(false);
                logger.LogDebug("Removed roles [{RoleNames}] from User {UserId} before managing role {RoleToAddName}.",
                    string.Join(", ", rolesBeingRemoved.Select(r => r.Name)), user.Id, roleToAdd.Name);
            }

            // Decide whether to add or remove the clicked role
            if (alreadyHadRole)
            {
                // User clicked the role they already have - remove it
                await user.RemoveRoleAsync(roleToAdd).ConfigureAwait(false);
                logger.LogInformation("Removed role {RoleName} ({RoleId}) from User {UserId} (toggle off).",
                    roleToAdd.Name, roleToAdd.Id, user.Id);
                await interaction.FollowupAsync($"Role '{roleToAdd.Name}' removed.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                // User clicked a role they didn't have - add it
                await user.AddRoleAsync(roleToAdd).ConfigureAwait(false);
                logger.LogInformation("Added role {RoleName} ({RoleId}) to User {UserId}.", roleToAdd.Name,
                    roleToAdd.Id, user.Id);
                await interaction.FollowupAsync($"Role '{roleToAdd.Name}' added.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (HttpException httpEx) when (httpEx.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(httpEx,
                "Permission Error (Forbidden) modifying roles for User {UserId} ({Username}) in Guild {GuildId}. Role: {RoleName} ({RoleId}). Check bot permissions and role hierarchy.",
                user.Id, user.Username, guild.Id, roleToAdd.Name, roleToAdd.Id);
            await interaction.FollowupAsync(
                "I don't have permission to modify your roles. This could be due to missing permissions or the role being higher than mine in the hierarchy.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling color role button click for User {UserId}, CustomId {CustomId}",
                user.Id, selectedCustomId);
            await interaction.FollowupAsync("An error occurred while updating your roles. Please try again later.",
                ephemeral: true).ConfigureAwait(false);
        }
    }
}