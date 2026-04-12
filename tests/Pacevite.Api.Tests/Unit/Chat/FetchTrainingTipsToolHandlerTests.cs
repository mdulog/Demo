using System.Net;
using System.Text.Json.Nodes;
using Pacevite.Api.Infrastructure.Chat.Tools;

namespace Pacevite.Api.Tests.Unit.Chat;

[Category("Unit")]
public sealed class FetchTrainingTipsToolHandlerTests
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
                <main><p>5 tips for marathon training: increase mileage gradually each week</p></main>
                <footer>Footer content</footer>
              </body>
            </html>
            """;

        var client = new HttpClient(new FakeHttpMessageHandler(html, HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://www.runnersworld.com")
        };

        var handler = new FetchTrainingTipsToolHandler(client);

        // Act
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"query":"marathon training tips"}""")!,
            "user-1",
            CancellationToken.None);

        // Assert
        await Assert.That(result).Contains("5 tips for marathon training");
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
            BaseAddress = new Uri("https://www.runnersworld.com")
        };

        var handler = new FetchTrainingTipsToolHandler(client);

        // Act
        var result = await handler.ExecuteAsync(
            JsonNode.Parse("""{"query":"unknown training query"}""")!,
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
