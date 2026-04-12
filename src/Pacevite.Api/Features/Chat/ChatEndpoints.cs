using System.Security.Claims;
using System.Text.Json;
using Mediator;
using Pacevite.Api.Contracts.Requests;

namespace Pacevite.Api.Features.Chat;

public static class ChatEndpoints
{
    private const int MaxMessageLength = 2000;
    private const string ContentTypeEventStream = "text/event-stream";
    private const string RoleUser = "user";
    private const string RoleAssistant = "assistant";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/message", StreamMessageAsync)
            .WithName("ChatMessage")
            .RequireAuthorization();
        return app;
    }

    private static async Task StreamMessageAsync(
        SendMessageRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        ILoggerFactory loggerFactory,
        HttpResponse response,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(ChatEndpoints));
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > MaxMessageLength)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message must be 1–2000 characters." }, ct);
            return;
        }

        if (request.History is not null && request.History.Any(m => m.Role is not RoleUser and not RoleAssistant))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "History roles must be 'user' or 'assistant'." }, ct);
            return;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim missing from token.");

        response.ContentType = ContentTypeEventStream;
        response.Headers.CacheControl = "no-cache";

        var query = new SendMessageQuery(userId, request.Message, request.History ?? []);

        try
        {
            await foreach (var sseEvent in mediator.CreateStream(query, ct))
            {
                await response.WriteAsync($"event: {sseEvent.Type}\ndata: {sseEvent.Data}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no action needed
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception in {Method} for user {UserId}", nameof(StreamMessageAsync), userId);
            var errorPayload = JsonSerializer.Serialize(new { message = "An error occurred. Please try again." });
            await response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
