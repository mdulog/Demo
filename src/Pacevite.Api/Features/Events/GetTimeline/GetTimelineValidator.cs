using FluentValidation;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Features.Events.GetTimeline;

public sealed class GetTimelineValidator : AbstractValidator<GetTimelineQuery>
{
    private static readonly string[] ValidEventTypes = Enum.GetNames<EventType>();

    public GetTimelineValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.EventType)
            .Must(v => ValidEventTypes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .When(x => x.EventType is not null)
            .WithMessage($"EventType must be one of: {string.Join(", ", ValidEventTypes)}.");
    }
}
