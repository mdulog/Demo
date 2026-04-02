using Pacevite.Api.Infrastructure.Parsing;
using System.Text;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Parsers;

public sealed class CsvEventParserTests
{
    private readonly CsvEventParser _parser = new();

    [Test]
    public async Task CanParse_TextCsv_ReturnsTrue()
    {
        await Assert.That(_parser.CanParse("text/csv")).IsTrue();
    }

    [Test]
    public async Task CanParse_ApplicationJson_ReturnsFalse()
    {
        await Assert.That(_parser.CanParse("application/json")).IsFalse();
    }

    [Test]
    public async Task Parse_ValidCsv_ReturnsEvents()
    {
        const string csv = """
            event_type,event_name,event_date,completion,elapsed_secs
            MARATHON,Berlin Marathon,2024-09-29,FINISHED,14400
            HYROX,HYROX Berlin,2024-11-10,FINISHED,5400
            """;

        var result = _parser.Parse(ToStream(csv));

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].EventType).IsEqualTo("MARATHON");
        await Assert.That(result[0].EventName).IsEqualTo("Berlin Marathon");
        await Assert.That(result[0].EventDate).IsEqualTo(new DateOnly(2024, 9, 29));
        await Assert.That(result[0].ElapsedSecs).IsEqualTo(14400);
    }

    [Test]
    public async Task Parse_SkipsCommentLines()
    {
        const string csv = """
            # This is a comment
            MARATHON,Berlin,2024-09-29,FINISHED,14400
            """;

        var result = _parser.Parse(ToStream(csv));

        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_WithOptionalRankColumns_PopulatesRanks()
    {
        const string csv = "MARATHON,Berlin,2024-09-29,FINISHED,14400,42,5,1000,150";

        var result = _parser.Parse(ToStream(csv));

        await Assert.That(result[0].OverallRank).IsEqualTo(42);
        await Assert.That(result[0].AgeGroupRank).IsEqualTo(5);
        await Assert.That(result[0].FieldSize).IsEqualTo(1000);
        await Assert.That(result[0].AgeGroupFieldSize).IsEqualTo(150);
    }

    [Test]
    public async Task Parse_TooFewColumns_ThrowsFormatException()
    {
        const string csv = "MARATHON,Berlin,2024-09-29";

        await Assert.That(() => _parser.Parse(ToStream(csv)))
            .Throws<FormatException>();
    }

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));
}
