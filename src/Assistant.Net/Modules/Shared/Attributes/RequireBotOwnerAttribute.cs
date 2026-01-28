using Assistant.Net.Options;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Modules.Shared.Attributes;

/// <summary>
///     Specifies that the command can only be executed by the OwnerID defined in the bot's configuration.
/// </summary>
public class RequireBotOwnerAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context,
        ICommandInfo commandInfo, IServiceProvider services)
    {
        if (context.Client.TokenType != TokenType.Bot)
            return Task.FromResult(
                PreconditionResult.FromError(
                    $"{nameof(RequireBotOwnerAttribute)} is not supported by this {nameof(TokenType)}."));

        var option = services.GetService<IOptions<DiscordOptions>>();
        if (option == null)
        {
            Console.WriteLine("[CRITICAL] Config service not found in RequireBotOwnerAttribute. DI issue?");
            return Task.FromResult(PreconditionResult.FromError("Configuration error: Cannot verify owner."));
        }

        var ownerId = option.Value.OwnerId;

        if (ownerId is 0)
            return Task.FromResult(
                PreconditionResult.FromError(ErrorMessage ?? "Bot owner ID is not configured or is invalid."));

        return Task.FromResult(context.User.Id == ownerId
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError(ErrorMessage ?? "This command can only be run by the bot owner."));
    }
}