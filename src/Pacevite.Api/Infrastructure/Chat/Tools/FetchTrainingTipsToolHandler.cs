using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class FetchTrainingTipsToolHandler(
    HttpClient httpClient,
    ILogger<FetchTrainingTipsToolHandler> logger) : IChatToolHandler
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.runnersworld.com",
        "www.triathlete.com",
        "www.hyrox.com",
        "www.outsideonline.com",
        "www.verywellfit.com",
    };

    private const int MaxResultLength = 3000;

    public FetchTrainingTipsToolHandler(HttpClient httpClient)
        : this(httpClient, NullLogger<FetchTrainingTipsToolHandler>.Instance) { }

    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        var query = input["query"]?.GetValue<string>() ?? string.Empty;

        try
        {
            var url = new Uri($"https://www.runnersworld.com/search?q={Uri.EscapeDataString(query)}");

            if (!AllowedHosts.Contains(url.Host))
                return "No results found: domain not permitted.";

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(url, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogCritical(ex, "{Method} failed for query {Query}", nameof(ExecuteAsync), query);
                throw;
            }

            if (!response.IsSuccessStatusCode)
                return "No results found.";

            var html = await response.Content.ReadAsStringAsync(ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

            var text = doc.DocumentNode.InnerText;
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "No results found.";

            if (text.Length > MaxResultLength)
                text = string.Concat(text.AsSpan(0, MaxResultLength), "… (truncated)");

            return text;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{Method} failed for query {Query}", nameof(ExecuteAsync), query);
            throw;
        }
    }
}
