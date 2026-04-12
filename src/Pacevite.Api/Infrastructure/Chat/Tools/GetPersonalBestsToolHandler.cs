using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class GetPersonalBestsToolHandler(
    AppDbContext db,
    ILogger<GetPersonalBestsToolHandler> logger) : IChatToolHandler
{
    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        try
        {
            var fastestPerType = await db.Events
                .Where(e => e.UserId == userId && e.Completion == CompletionStatus.Finished)
                .GroupBy(e => e.EventType)
                .Select(g => g.OrderBy(e => e.ElapsedSecs).First())
                .ToListAsync(ct);

            var personalBests = fastestPerType
                .Select(e => new
                {
                    EventType = e.EventType.ToString(),
                    e.EventName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.ElapsedSecs,
                })
                .ToList();

            if (personalBests.Count == 0)
                return "No personal bests found. The user has no finished events.";

            return JsonSerializer.Serialize(personalBests);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{Method} failed for user {UserId}", nameof(ExecuteAsync), userId);
            throw;
        }
    }
}
