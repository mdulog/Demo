using System.Security.Claims;
using System.Text.Json;
using Mediator;
using Pacevite.Api.Contracts.Requests;

namespace Pacevite.Api.Features.Chat;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/message", StreamMessageAsync).WithName("ChatMessage");
        return app;
    }

    private static async Task StreamMessageAsync(
        SendMessageRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpResponse response,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message must be 1–2000 characters." }, ct);
            return;
        }

        if (request.History.Any(m => m.Role is not "user" and not "assistant"))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "History roles must be 'user' or 'assistant'." }, ct);
            return;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim missing from token.");

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var query = new SendMessageQuery(userId, request.Message, request.History);

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
        catch (Exception)
        {
            var errorPayload = JsonSerializer.Serialize(new { message = "An error occurred. Please try again." });
            await response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
