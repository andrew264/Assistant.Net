using Discord;

namespace Assistant.Net.Utilities;

public static class UserExtensions
{
    extension(IUser user)
    {
        public string EffectiveAvatarUrl => user.GetDisplayAvatarUrl(size: 2048) ?? user.GetDefaultAvatarUrl();
        public string BestDisplayName => (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;
    }

    extension(IGuildUser guildUser)
    {
        public Color? TopRoleColor =>
            guildUser.RoleIds
                .Select(id => guildUser.Guild.GetRole(id))
                .Where(role => role is { IsManaged: false, Colors.PrimaryColor.RawValue: > 0 })
                .OrderByDescending(role => role.Position)
                .FirstOrDefault()?.Colors.PrimaryColor;
    }
}