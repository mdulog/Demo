using Mediator;
using Pacevite.Api.Features.Auth;

namespace Pacevite.Api.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResult>;
