using Assistant.Net.Configuration;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Net.Modules.Attributes;

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

        var config = services.GetService<Config>();
        if (config == null)
        {
            Console.WriteLine("[CRITICAL] Config service not found in RequireBotOwnerAttribute. DI issue?");
            return Task.FromResult(PreconditionResult.FromError("Configuration error: Cannot verify owner."));
        }

        var ownerId = config.Client.OwnerId;

        if (ownerId is null or 0)
            return Task.FromResult(
                PreconditionResult.FromError(ErrorMessage ?? "Bot owner ID is not configured or is invalid."));

        return Task.FromResult(context.User.Id == ownerId.Value
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError(ErrorMessage ?? "This command can only be run by the bot owner."));
    }
}