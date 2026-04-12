using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class ChatToolExecutorTests
{
    private sealed class StubHandler : IChatToolHandler
    {
        public string CalledWith { get; private set; } = string.Empty;

        public ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
        {
            CalledWith = input?.ToString() ?? string.Empty;
            return ValueTask.FromResult("stub-result");
        }
    }

    [Test]
    public async Task ExecuteAsync_KnownTool_DispatchesToCorrectHandler()
    {
        var stub = new StubHandler();
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>
        {
            ["get_events"] = stub
        });

        var result = await executor.ExecuteAsync("get_events", JsonNode.Parse("{}")!, "user-42", CancellationToken.None);

        await Assert.That(result).IsEqualTo("stub-result");
    }

    [Test]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorMessage()
    {
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>());

        var result = await executor.ExecuteAsync("nonexistent_tool", JsonNode.Parse("{}")!, "user-42", CancellationToken.None);

        await Assert.That(result).Contains("Unknown tool");
    }

    [Test]
    public async Task ExecuteAsync_KnownTool_PassesInputToHandler()
    {
        var stub = new StubHandler();
        var executor = new ChatToolExecutor(new Dictionary<string, IChatToolHandler>
        {
            ["get_events"] = stub
        });
        var input = JsonNode.Parse("""{"event_type":"Marathon"}""")!;

        await executor.ExecuteAsync("get_events", input, "user-99", CancellationToken.None);

        await Assert.That(stub.CalledWith).Contains("Marathon");
    }
}
