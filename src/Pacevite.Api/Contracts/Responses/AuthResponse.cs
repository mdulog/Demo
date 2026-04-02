namespace Pacevite.Api.Contracts.Responses;

public sealed record AuthResponse(string UserId, string Email, string Token);
