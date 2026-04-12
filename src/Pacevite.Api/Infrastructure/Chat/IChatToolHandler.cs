using System.Text.Json.Nodes;

namespace Pacevite.Api.Infrastructure.Chat;

public interface IChatToolHandler
{
    ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct);
}
