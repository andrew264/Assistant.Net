using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Services;

public class RoleAddService : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("assistant:addrole:*")]
    public async Task AddRoleAsync(string roleId)
    {
        var role = Context.Guild.GetRole(ulong.Parse(roleId));

        if (role == null)
        {
            await RespondAsync("Role not found", ephemeral: true);
            return;
        }

        if (Context.User is not SocketGuildUser user)
            return;

        // Remove all roles that start with "color"
        foreach (var userRole in user.Roles)
            if (userRole.Name.StartsWith("color"))
                await user.RemoveRoleAsync(userRole);

        await user.AddRoleAsync(role);
        await RespondAsync("Role added", ephemeral: true);

    }
}