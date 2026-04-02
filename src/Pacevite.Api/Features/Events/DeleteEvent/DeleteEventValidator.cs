using FluentValidation;

namespace Pacevite.Api.Features.Events.DeleteEvent;

public sealed class DeleteEventValidator : AbstractValidator<DeleteEventCommand>
{
    public DeleteEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
