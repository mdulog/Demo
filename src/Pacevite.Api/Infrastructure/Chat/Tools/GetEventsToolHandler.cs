using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pacevite.Api.Domain.Enums;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Chat.Tools;

public sealed class GetEventsToolHandler(
    AppDbContext db,
    ILogger<GetEventsToolHandler> logger) : IChatToolHandler
{
    public async ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)
    {
        try
        {
            var eventTypeStr = input["event_type"]?.GetValue<string>();
            var fromStr = input["from"]?.GetValue<string>();
            var toStr = input["to"]?.GetValue<string>();

            var query = db.Events.Where(e => e.UserId == userId);

            if (!string.IsNullOrEmpty(eventTypeStr) &&
                Enum.TryParse<EventType>(eventTypeStr, ignoreCase: true, out var eventType))
            {
                query = query.Where(e => e.EventType == eventType);
            }

            if (DateOnly.TryParse(fromStr, out var from))
                query = query.Where(e => e.EventDate >= from);

            if (DateOnly.TryParse(toStr, out var to))
                query = query.Where(e => e.EventDate <= to);

            var events = await query
                .OrderByDescending(e => e.EventDate)
                .Select(e => new
                {
                    e.Id,
                    EventType = e.EventType.ToString(),
                    e.EventName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    Completion = e.Completion.ToString(),
                    e.ElapsedSecs,
                    e.OverallRank,
                    e.AgeGroupRank,
                    e.FieldSize,
                    // AgeGroupFieldSize omitted — redundant without pairing context Claude already has from AgeGroupRank
                })
                .ToListAsync(ct);

            if (events.Count == 0)
                return "No events found for the given filters.";

            return JsonSerializer.Serialize(events);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{Method} failed for user {UserId}", nameof(ExecuteAsync), userId);
            throw;
        }
    }
}
