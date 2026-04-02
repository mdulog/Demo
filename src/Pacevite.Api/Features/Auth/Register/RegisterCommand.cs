using Mediator;
using Pacevite.Api.Features.Auth;

namespace Pacevite.Api.Features.Auth.Register;

public sealed record RegisterCommand(string Email, string Password) : ICommand<AuthResult>;
