using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Pacevite.Api.Infrastructure.Chat;

public sealed class ChatToolExecutor(
    IReadOnlyDictionary<string, IChatToolHandler> handlers,
    ILogger<ChatToolExecutor> logger) : IChatToolExecutor
{
    public ChatToolExecutor(IReadOnlyDictionary<string, IChatToolHandler> handlers)
        : this(handlers, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatToolExecutor>.Instance) { }

    public async ValueTask<string> ExecuteAsync(
        string toolName, JsonNode input, string userId, CancellationToken ct)
    {
        if (!handlers.TryGetValue(toolName, out var handler))
        {
            logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
            return $"Unknown tool: {toolName}. Available tools: {string.Join(", ", handlers.Keys)}";
        }

        try
        {
            return await handler.ExecuteAsync(input, userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Tool handler {ToolName} threw for user {UserId}", toolName, userId);
            return $"Tool {toolName} failed: {ex.Message}";
        }
    }
}
