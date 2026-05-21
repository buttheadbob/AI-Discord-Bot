using SimpleDiscordNet.Commands;
using AI_Discord_Bot.Services;

namespace AI_Discord_Bot.Commands;

public class AskCommand
{
    [SlashCommand("ask", "Ask the AI a question about the server")]
    public async Task ExecuteAsync(InteractionContext ctx, string question)
    {
        try
        {
            var answer = await RagService.Instance.AskAsync(question);
            var response = answer.Result;
            if (string.IsNullOrWhiteSpace(response))
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
}
