using System.Net;
using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat.Tools;

namespace Pacevite.Api.Tests.Unit.Chat;

// Note: The SSRF domain allowlist guard (returns "No results found: domain not permitted.")
// cannot be tested via the public ExecuteAsync interface since the URL is constructed
// internally from a hardcoded base domain. The guard is present for future-proofing
// when URL construction may accept external input.
[Category("Unit")]
public sealed class ScrapeRaceResultsToolHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ParsesHtmlAndReturnsText()
    {
        // Arrange
        const string html = """
            <html>
              <head><script>alert('x')</script><style>body{}</style></head>
              <body>
                <nav>Site nav</nav>
                <header>Page header</header>
                <main><p>Berlin Marathon 2024 results: 1st place John Doe 2:05:00</p></main>
                <footer>Footer content</footer>
              </body>
            </html>
            """;

        var client = new HttpClient(new FakeHttpMessageHandler(html, HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://www.worldathletics.org")
        };

        var handler = new ScrapeRaceResultsToolHandler(client);

        // Act
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"race_name":"Berlin Marathon","year":2024}""")!,
            "user-1",
            CancellationToken.None);

        // Assert
        await Assert.That(result).Contains("Berlin Marathon 2024 results");
        await Assert.That(result).DoesNotContain("Site nav");
        await Assert.That(result).DoesNotContain("Page header");
        await Assert.That(result).DoesNotContain("Footer content");
    }

    [Test]
    public async Task ExecuteAsync_HttpFailure_ReturnsNoResultsMessage()
    {
        // Arrange
        var client = new HttpClient(new FakeHttpMessageHandler(string.Empty, HttpStatusCode.NotFound))
        {
            BaseAddress = new Uri("https://www.worldathletics.org")
        };

        var handler = new ScrapeRaceResultsToolHandler(client);

        // Act
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"race_name":"Unknown Race"}""")!,
            "user-1",
            CancellationToken.None);

        // Assert
        await Assert.That(result).Contains("No results found");
    }

    private sealed class FakeHttpMessageHandler(string content, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
    }
}
