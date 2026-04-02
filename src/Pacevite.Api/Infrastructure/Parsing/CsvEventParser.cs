using System.Globalization;

namespace Pacevite.Api.Infrastructure.Parsing;

// Parses the canonical Pacevite CSV format:
// event_type,event_name,event_date,completion,elapsed_secs[,overall_rank,ag_rank,field_size,ag_field_size]
// Lines starting with '#' are treated as comments and skipped.
public sealed class CsvEventParser : IEventParser
{
    private const string ContentType = "text/csv";
    private const int RequiredColumnCount = 5;

    public bool CanParse(string contentType) =>
        contentType.StartsWith(ContentType, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParsedEvent> Parse(Stream content)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var results = new List<ParsedEvent>();
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            // Skip blank lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            // Skip header row
            if (lineNumber == 1 && line.StartsWith("event_type", StringComparison.OrdinalIgnoreCase))
                continue;

            var cols = line.Split(',');
            if (cols.Length < RequiredColumnCount)
                throw new FormatException($"CSV line {lineNumber} has {cols.Length} columns, expected at least {RequiredColumnCount}.");

            results.Add(new ParsedEvent
            {
                EventType = cols[0].Trim().ToUpperInvariant(),
                EventName = cols[1].Trim(),
                EventDate = DateOnly.ParseExact(cols[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Completion = cols[3].Trim().ToUpperInvariant(),
                ElapsedSecs = int.Parse(cols[4].Trim(), CultureInfo.InvariantCulture),
                OverallRank = cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]) ? int.Parse(cols[5].Trim(), CultureInfo.InvariantCulture) : null,
                AgeGroupRank = cols.Length > 6 && !string.IsNullOrWhiteSpace(cols[6]) ? int.Parse(cols[6].Trim(), CultureInfo.InvariantCulture) : null,
                FieldSize = cols.Length > 7 && !string.IsNullOrWhiteSpace(cols[7]) ? int.Parse(cols[7].Trim(), CultureInfo.InvariantCulture) : null,
                AgeGroupFieldSize = cols.Length > 8 && !string.IsNullOrWhiteSpace(cols[8]) ? int.Parse(cols[8].Trim(), CultureInfo.InvariantCulture) : null
            });
        }

        return results;
    }
}
