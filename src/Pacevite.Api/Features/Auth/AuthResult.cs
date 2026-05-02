namespace Pacevite.Api.Features.Auth;

public sealed class AuthResult
{
    public bool IsSuccess { get; private init; }
    public bool IsDuplicate { get; private init; }
    public string? UserId { get; private init; }
    public string? Email { get; private init; }
    public string? Token { get; private init; }
    public string? RefreshToken { get; private init; }
    public string? Error { get; private init; }

    private AuthResult() { }

    public static AuthResult Ok(string userId, string email, string token, string refreshToken) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Email = email,
        Token = token,
        RefreshToken = refreshToken
    };

    public static AuthResult Fail(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };

    public static AuthResult FailDuplicate(string error) => new()
    {
        IsSuccess = false,
        IsDuplicate = true,
        Error = error
    };
}
