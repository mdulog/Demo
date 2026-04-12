using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Mediator;
using Microsoft.Extensions.Options;
using Pacevite.Api.Infrastructure.Chat;
using AnthropicTool = Anthropic.SDK.Common.Tool;
using AnthropicFunction = Anthropic.SDK.Common.Function;
using ChatSseEvent = Pacevite.Api.Infrastructure.Chat.SseEvent;

namespace Pacevite.Api.Features.Chat;

public sealed class SendMessageHandler(
    AnthropicClient anthropic,
    IChatToolExecutor toolExecutor,
    IOptions<AnthropicOptions> options) : IStreamQueryHandler<SendMessageQuery, ChatSseEvent>
{
    private const int MaxAgentTurns = 10;

    private const string SystemPrompt =
        "You are a fitness analytics assistant for Pacevite. " +
        "Help users understand their race performance, analyse trends, and compare against other athletes. " +
        "You have tools to query the user's race data and search the internet for race results and training tips. " +
        "Elapsed times are in seconds — always convert to h:mm:ss format in your responses. " +
        "Be encouraging, specific, and data-driven.";

    private static readonly JsonSerializerOptions SchemaSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly List<AnthropicTool> ToolDefinitions = BuildToolDefinitions();

    public async IAsyncEnumerable<ChatSseEvent> Handle(
        SendMessageQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = BuildMessageHistory(query);

        for (var turn = 0; turn < MaxAgentTurns; turn++)
        {
            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = options.Value.Model,
                MaxTokens = options.Value.MaxTokens,
                Stream = true,
                Tools = ToolDefinitions,
                System = [new SystemMessage(SystemPrompt)],
            };

            var outputs = new List<MessageResponse>();

            await foreach (var response in anthropic.Messages.StreamClaudeMessageAsync(parameters, ct))
            {
                if (response.Delta?.Text is { Length: > 0 } text)
                    yield return ChatSseEvent.Delta(text);

                outputs.Add(response);
            }

            var assistantMessage = new Message(outputs);
            messages.Add(assistantMessage);

            var toolUses = assistantMessage.Content
                .OfType<ToolUseContent>()
                .ToList();

            if (toolUses.Count == 0)
            {
                yield return ChatSseEvent.Done();
                yield break;
            }

            foreach (var toolUse in toolUses)
            {
                yield return ChatSseEvent.ToolStart(toolUse.Name, GetToolLabel(toolUse.Name));

                // ToolUseContent.Input is already a JsonNode — pass it directly rather than
                // round-tripping through JsonSerializer, which would be wasteful and lossy.
                var inputJson = toolUse.Input ?? JsonNode.Parse("{}")!;
                var result = await toolExecutor.ExecuteAsync(toolUse.Name, inputJson, query.UserId, ct);

                yield return ChatSseEvent.ToolEnd();

                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content =
                    [
                        new ToolResultContent
                        {
                            ToolUseId = toolUse.Id,
                            Content = [new TextContent { Text = result }],
                        }
                    ],
                });
            }
        }

        // Fallback: exhausted turn budget without a final response.
        yield return ChatSseEvent.Error("The assistant did not complete a response within the allowed number of steps.");
    }

    private static List<Message> BuildMessageHistory(SendMessageQuery query)
    {
        var messages = query.History
            .Select(h => new Message(
                h.Role == "user" ? RoleType.User : RoleType.Assistant,
                h.Content))
            .ToList();

        messages.Add(new Message(RoleType.User, query.Message));
        return messages;
    }

    private static string GetToolLabel(string toolName) => toolName switch
    {
        "get_events"          => "Looking up your events\u2026",
        "get_personal_bests"  => "Looking up your personal bests\u2026",
        "scrape_race_results" => "Searching race results\u2026",
        "fetch_training_tips" => "Fetching training tips\u2026",
        _                     => "Running tool\u2026",
    };

    private static List<AnthropicTool> BuildToolDefinitions() =>
        [
            BuildToolDefinition(
                "get_events",
                "Retrieve the user's fitness events. Optionally filter by event_type (Marathon, Hyrox, Spartan, Generic), from (ISO date), or to (ISO date).",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["event_type"] = new() { Type = "string", Description = "Event type: Marathon, Hyrox, Spartan, or Generic" },
                        ["from"]       = new() { Type = "string", Description = "Start date (YYYY-MM-DD)" },
                        ["to"]         = new() { Type = "string", Description = "End date (YYYY-MM-DD)" },
                    },
                }),

            BuildToolDefinition(
                "get_personal_bests",
                "Retrieve the user's personal best (fastest finished) time per event type.",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>(),
                }),

            BuildToolDefinition(
                "scrape_race_results",
                "Search for published race results for a specific race.",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["race_name"] = new() { Type = "string", Description = "Name of the race, e.g. 'London Marathon'" },
                        ["year"]      = new() { Type = "integer", Description = "Optional race year" },
                    },
                    Required = ["race_name"],
                }),

            BuildToolDefinition(
                "fetch_training_tips",
                "Search for training advice and tips relevant to a query.",
                new InputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Property>
                    {
                        ["query"] = new() { Type = "string", Description = "Training question or topic, e.g. 'how to improve Hyrox sled push'" },
                    },
                    Required = ["query"],
                }),
        ];

    private static AnthropicTool BuildToolDefinition(string name, string description, InputSchema schema)
    {
        var json = JsonSerializer.Serialize(schema, SchemaSerializerOptions);
        // AnthropicFunction has an implicit conversion to AnthropicTool (Common.Tool.op_Implicit).
        return new AnthropicFunction(name, description, JsonNode.Parse(json));
    }
}
