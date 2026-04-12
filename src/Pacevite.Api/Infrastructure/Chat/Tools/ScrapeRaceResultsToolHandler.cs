using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class ScrapeRaceResultsToolHandler(
    HttpClient httpClient,
    ILogger<ScrapeRaceResultsToolHandler> logger) : IChatToolHandler
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.worldathletics.org",
        "results.sporthive.com",
        "www.hyrox.com",
        "www.parkrun.org.uk",
        "www.spartanrace.com",
    };

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private const int MaxResultLength = 3000;

    public ScrapeRaceResultsToolHandler(HttpClient httpClient)
        : this(httpClient, NullLogger<ScrapeRaceResultsToolHandler>.Instance) { }

    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var raceName = input["race_name"]?.GetValue<string>() ?? string.Empty;
        var year = input["year"]?.GetValue<int?>();

        try
        {
            var queryText = year.HasValue
                ? $"{raceName} {year.Value} results"
                : $"{raceName} results";

            var url = new Uri($"https://www.worldathletics.org/search?q={Uri.EscapeDataString(queryText)}");

            // SSRF protection (OWASP A10): validate URL host before every request.
            // The URL is currently constructed from a hardcoded base domain, but this guard
            // ensures the check remains enforced if URL construction is ever refactored.
            if (!AllowedHosts.Contains(url.Host))
                return "No results found: domain not permitted.";

            var response = await httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return "No results found.";

            var html = await response.Content.ReadAsStringAsync(ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

            var text = doc.DocumentNode.InnerText;
            text = WhitespacePattern.Replace(text, " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "No results found.";

            if (text.Length > MaxResultLength)
                text = string.Concat(text.AsSpan(0, MaxResultLength), "… (truncated)");

            return text;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{Method} failed for race {RaceName}", nameof(ExecuteAsync), raceName);
            throw;
        }
    }
}
