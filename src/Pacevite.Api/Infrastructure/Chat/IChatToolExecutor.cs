using System.Text.Json.Nodes;

namespace Pacevite.Api.Infrastructure.Chat;

public interface IChatToolExecutor
{
    ValueTask<string> ExecuteAsync(string toolName, JsonNode input, string userId, CancellationToken ct);
}
