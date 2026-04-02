using FluentValidation;

namespace Pacevite.Api.Features.Events.Upload;

public sealed class UploadEventValidator : AbstractValidator<UploadEventCommand>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/csv",
        "application/json"
    };

    public UploadEventValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(ct => AllowedContentTypes.Any(allowed => ct.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Only text/csv and application/json are accepted.");

        RuleFor(x => x.FileStream)
            .NotNull()
            .Must(s => s.Length > 0)
            .WithMessage("Upload file must not be empty.")
            .Must(s => s.Length <= 10 * 1024 * 1024)
            .WithMessage("Upload file must not exceed 10 MB.");
    }
}
