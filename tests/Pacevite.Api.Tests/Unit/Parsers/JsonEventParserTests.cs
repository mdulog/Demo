using Pacevite.Api.Infrastructure.Parsing;
using System.Text;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Parsers;

public sealed class JsonEventParserTests
{
    private readonly JsonEventParser _parser = new();

    [Test]
    public async Task CanParse_ApplicationJson_ReturnsTrue()
    {
        await Assert.That(_parser.CanParse("application/json")).IsTrue();
    }

    [Test]
    public async Task CanParse_TextCsv_ReturnsFalse()
    {
        await Assert.That(_parser.CanParse("text/csv")).IsFalse();
    }

    [Test]
    public async Task Parse_ValidJson_ReturnsEvents()
    {
        const string json = """
            [
              {
                "event_type": "HYROX",
                "event_name": "HYROX Berlin 2024",
                "event_date": "2024-11-10",
                "completion": "FINISHED",
                "elapsed_secs": 5400
              }
            ]
            """;

        var result = _parser.Parse(ToStream(json));

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].EventType).IsEqualTo("HYROX");
        await Assert.That(result[0].ElapsedSecs).IsEqualTo(5400);
        await Assert.That(result[0].OverallRank).IsNull();
    }

    [Test]
    public async Task Parse_WithSplits_MapsSplitsCorrectly()
    {
        const string json = """
            [
              {
                "event_type": "HYROX",
                "event_name": "HYROX Test",
                "event_date": "2024-11-10",
                "completion": "FINISHED",
                "elapsed_secs": 5400,
                "splits": [
                  { "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }
                ]
              }
            ]
            """;

        var result = _parser.Parse(ToStream(json));

        await Assert.That(result[0].Splits.Count).IsEqualTo(1);
        await Assert.That(result[0].Splits[0].SplitLabel).IsEqualTo("SkiErg");
        await Assert.That(result[0].Splits[0].SplitSecs).IsEqualTo(300);
    }

    [Test]
    public async Task Parse_MissingRequiredField_ThrowsFormatException()
    {
        const string json = """[{ "event_type": "MARATHON" }]""";

        await Assert.That(() => _parser.Parse(ToStream(json)))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_EmptyArray_ReturnsEmpty()
    {
        var result = _parser.Parse(ToStream("[]"));

        await Assert.That(result.Count).IsEqualTo(0);
    }

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));
}
