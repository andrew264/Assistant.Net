using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Info;

// Constants for Custom IDs
public static class IntroductionModuleConstants
{
    public const string IntroduceModalId = "assistant:introduce_modal";
    public const string IntroductionTextInputId = "assistant:introduction_text";
}

public class IntroductionModule(UserService userService, ILogger<IntroductionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("introduce", "Introduce yourself to other members of the server.")]
    public async Task IntroduceCommand()
    {
        var modal = new ModalBuilder()
            .WithTitle("Introduction")
            .WithCustomId(IntroductionModuleConstants.IntroduceModalId)
            .AddTextInput(
                "Introduce yourself",
                IntroductionModuleConstants.IntroductionTextInputId,
                TextInputStyle.Paragraph,
                "Tell us a bit about yourself...",
                8,
                1024,
                true)
            .Build();

        await RespondWithModalAsync(modal);
        logger.LogInformation("Presented introduction modal to User {UserId}", Context.User.Id);
    }

    // Modal Interaction Handler
    [ModalInteraction(IntroductionModuleConstants.IntroduceModalId)]
    public async Task HandleIntroductionModalSubmit(IntroductionModal modalData)
    {
        await DeferAsync(true);

        var introduction = modalData.Introduction;

        try
        {
            await userService.UpdateUserIntroductionAsync(Context.User.Id, introduction);
            logger.LogInformation("[INTRO ADDED] {User}: {Intro}", Context.User.Username, introduction);
            await FollowupAsync("Introduction added successfully!", ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while processing introduction modal for User {UserId}",
                Context.User.Id);
            await FollowupAsync("An error occurred while saving your introduction. Please try again later.",
                ephemeral: true);
        }
    }
}

// Helper class to easily access modal component data
public class IntroductionModal : IModal
{
    [ModalTextInput(IntroductionModuleConstants.IntroductionTextInputId)]
    public string Introduction { get; set; } = string.Empty;

    public string Title => "Introduction";
}