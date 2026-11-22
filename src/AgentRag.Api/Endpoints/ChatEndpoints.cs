// HTTP endpoint for the chat bot.
//
//   POST /api/chat   { "message": "...", "history": [...] }  ->  { "answer": "...", "sources": [...], "toolCalls": [...] }

using AgentRag.Api.Chat;

namespace AgentRag.Api.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat", AskAsync);
    }

    private static async Task<IResult> AskAsync(ChatRequest request, ChatService chat, ILogger<ChatService> logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest("The message is empty.");
        }

        try
        {
            ChatAnswer answer = await chat.AskAsync(request, cancellationToken);
            return Results.Ok(answer);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Most often: Ai:Endpoint / Ai:ApiKey not set, or no access to the model.
            // Send the reason to the web app so the user sees it, instead of a bare "500".
            logger.LogError(ex, "The chat request failed.");
            return Results.Problem(title: "The chat bot could not answer.", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
