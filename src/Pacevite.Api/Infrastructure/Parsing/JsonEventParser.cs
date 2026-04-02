using System.Text.Json;

namespace Pacevite.Api.Infrastructure.Parsing;

// Parses a JSON array of event objects. Each object must contain the required fields;
// optional fields (ranks, location, metadata, splits) may be omitted.
// Expected shape:
// [{ "event_type": "MARATHON", "event_name": "...", "event_date": "2024-04-21",
//    "completion": "FINISHED", "elapsed_secs": 14400, ... }]
public sealed class JsonEventParser : IEventParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public bool CanParse(string contentType) =>
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParsedEvent> Parse(Stream content)
    {
        var documents = JsonSerializer.Deserialize<List<JsonElement>>(content, SerializerOptions)
            ?? throw new FormatException("JSON payload must be a non-null array.");

        return documents.Select(MapElement).ToList();
    }

    private static ParsedEvent MapElement(JsonElement el)
    {
        return new ParsedEvent
        {
            EventType = GetRequired(el, "event_type").GetString()!.ToUpperInvariant(),
            EventName = GetRequired(el, "event_name").GetString()!,
            EventDate = DateOnly.Parse(GetRequired(el, "event_date").GetString()!),
            Completion = GetRequired(el, "completion").GetString()!.ToUpperInvariant(),
            ElapsedSecs = GetRequired(el, "elapsed_secs").GetInt32(),
            OverallRank = TryGetInt(el, "overall_rank"),
            AgeGroupRank = TryGetInt(el, "ag_rank"),
            FieldSize = TryGetInt(el, "field_size"),
            AgeGroupFieldSize = TryGetInt(el, "ag_field_size"),
            Location = TryGetDict(el, "location"),
            Metadata = TryGetDict(el, "metadata"),
            Splits = TryGetSplits(el)
        };
    }

    private static JsonElement GetRequired(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var value))
            throw new FormatException($"Required JSON property '{property}' is missing.");
        return value;
    }

    private static int? TryGetInt(JsonElement el, string property) =>
        el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number
            ? val.GetInt32()
            : null;

    private static Dictionary<string, object> TryGetDict(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var val) || val.ValueKind != JsonValueKind.Object)
            return [];

        return val.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object)p.Value.GetRawText());
    }

    private static IReadOnlyList<ParsedSplit> TryGetSplits(JsonElement el)
    {
        if (!el.TryGetProperty("splits", out var splitsEl) || splitsEl.ValueKind != JsonValueKind.Array)
            return [];

        return splitsEl.EnumerateArray().Select(s => new ParsedSplit
        {
            SplitType = GetRequired(s, "split_type").GetString()!.ToUpperInvariant(),
            SplitLabel = GetRequired(s, "split_label").GetString()!,
            SplitSecs = GetRequired(s, "split_secs").GetInt32(),
            CumulativeSecs = GetRequired(s, "cumulative_secs").GetInt32(),
            Metadata = TryGetDict(s, "metadata")
        }).ToList();
    }
}
