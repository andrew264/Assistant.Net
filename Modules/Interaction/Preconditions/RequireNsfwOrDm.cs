using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Interaction.Preconditions;

public class RequireNsfwOrDmAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo command, IServiceProvider services)
    {
        if (context.Channel is ITextChannel textChannel && textChannel.IsNsfw || context.Channel is IDMChannel)
        {
            return Task.FromResult(PreconditionResult.FromSuccess());
        }

        return Task.FromResult(PreconditionResult.FromError(ErrorMessage ?? "This command works only in an NSFW channel or in DMs."));
    }
}