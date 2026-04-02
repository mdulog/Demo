namespace Pacevite.Api.Infrastructure.Parsing;

// Abstraction over CSV and JSON parsers — open for extension (new formats) without
// modifying existing parsers (OCP). Each implementation is stateless and thread-safe.
public interface IEventParser
{
    bool CanParse(string contentType);
    IReadOnlyList<ParsedEvent> Parse(Stream content);
}

public sealed class ParsedEvent
{
    public required string EventType { get; init; }
    public required string EventName { get; init; }
    public required DateOnly EventDate { get; init; }
    public required string Completion { get; init; }
    public required int ElapsedSecs { get; init; }
    public int? OverallRank { get; init; }
    public int? AgeGroupRank { get; init; }
    public int? FieldSize { get; init; }
    public int? AgeGroupFieldSize { get; init; }
    public Dictionary<string, object> Location { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = [];
    public IReadOnlyList<ParsedSplit> Splits { get; init; } = [];
}

public sealed class ParsedSplit
{
    public required string SplitType { get; init; }
    public required string SplitLabel { get; init; }
    public required int SplitSecs { get; init; }
    public required int CumulativeSecs { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = [];
}
