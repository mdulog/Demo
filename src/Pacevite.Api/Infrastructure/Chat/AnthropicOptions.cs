namespace Pacevite.Api.Infrastructure.Chat;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
}
