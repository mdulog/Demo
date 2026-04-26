using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pacevite.Api.Infrastructure.OpenApi;

// Rewrites the OpenAPI server URL to include the path prefix that the reverse
// proxy stripped before forwarding. Without this, Scalar's "Try it" button
// sends requests directly to /api/... which nginx has no route for.
internal sealed class ForwardedPrefixTransformer(IHttpContextAccessor httpContextAccessor)
    : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var prefix = httpContext?.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();

        if (string.IsNullOrEmpty(prefix))
            return Task.CompletedTask;

        var request = httpContext!.Request;
        var serverUrl = $"{request.Scheme}://{request.Host}{prefix}";

        document.Servers = [new OpenApiServer { Url = serverUrl }];

        return Task.CompletedTask;
    }
}
