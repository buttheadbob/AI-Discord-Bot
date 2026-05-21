using SimpleDiscordNet.Commands;
using AI_Discord_Bot.Services;

namespace AI_Discord_Bot.Commands;

public class AskCommand
{
    private static readonly string[] InjectionPatterns =
    [
        "ignore previous",
        "ignore all previous",
        "ignore your instruction",
        "forget your instruction",
        "forget your prompt",
        "disregard previous",
        "disregard instruction",
        "new instructions",
        "system prompt",
        "you are now",
        "act as",
        "pretend",
        "you are a",
        "roleplay",
        "jailbreak",
        "dan ",
        "override",
        "bypass",
    ];

    [SlashCommand("ask", "Ask the AI a question about the server")]
    public async Task ExecuteAsync(InteractionContext ctx, string question)
    {
        if (IsInjectionAttempt(question))
        {
            await ctx.FollowupAsync("I can only answer questions about the server using the provided documentation.");
            return;
        }

        try
        {
            var answer = await RagService.Instance.AskAsync(question);
            var response = answer.Result;

            if (string.IsNullOrWhiteSpace(response) || answer.RelevantSources.Count == 0)
                response = "I don't have enough information to answer that question.";

            await ctx.FollowupAsync(response);
        }
        catch (InvalidOperationException)
        {
            await ctx.FollowupAsync("RAG is not initialized. Configure and initialize RAG in the bot's UI first.");
        }
        catch (Exception ex)
        {
            await ctx.FollowupAsync($"An error occurred: {ex.Message}");
        }
    }

    private static bool IsInjectionAttempt(string question)
    {
        var lower = question.ToLowerInvariant();
        foreach (var pattern in InjectionPatterns)
        {
            if (lower.Contains(pattern))
                return true;
        }
        return false;
    }
}
