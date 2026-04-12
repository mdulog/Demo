using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pacevite.Api.Infrastructure.Chat;

public sealed class ChatToolExecutor : IChatToolExecutor
{
    private readonly IReadOnlyDictionary<string, IChatToolHandler> _handlers;
    private readonly ILogger<ChatToolExecutor> _logger;

    public ChatToolExecutor(IReadOnlyDictionary<string, IChatToolHandler> handlers)
        : this(handlers, NullLogger<ChatToolExecutor>.Instance) { }

    public ChatToolExecutor(IReadOnlyDictionary<string, IChatToolHandler> handlers, ILogger<ChatToolExecutor> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async ValueTask<string> ExecuteAsync(
        string toolName, JsonNode input, string userId, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            _logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
            return $"Unknown tool: {toolName}. Available tools: {string.Join(", ", _handlers.Keys)}";
        }

        try
        {
            return await handler.ExecuteAsync(input, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Tool handler {ToolName} threw for user {UserId}", toolName, userId);
            return $"Tool {toolName} failed: {ex.Message}";
        }
    }
}
