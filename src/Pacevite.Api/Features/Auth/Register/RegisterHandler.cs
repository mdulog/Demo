using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Register;

public sealed class RegisterHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<RegisterHandler> logger) : ICommandHandler<RegisterCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByEmailAsync(command.Email);
        if (existing is not null)
            return AuthResult.FailDuplicate("Email is already registered.");

        var user = new IdentityUser { UserName = command.Email, Email = command.Email };
        var result = await userManager.CreateAsync(user, command.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            logger.LogWarning("Registration failed: {Errors}", errors);
            return AuthResult.Fail(errors);
        }

        logger.LogInformation("User registered: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        var rawRefreshToken = await refreshTokenService.CreateAsync(user.Id, cancellationToken);
        return AuthResult.Ok(user.Id, user.Email!, token, rawRefreshToken);
    }
}
