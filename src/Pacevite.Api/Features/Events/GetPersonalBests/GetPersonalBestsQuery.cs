using Mediator;
using Pacevite.Api.Contracts.Responses;

namespace Pacevite.Api.Features.Events.GetPersonalBests;

public sealed record GetPersonalBestsQuery(string UserId) : IQuery<IReadOnlyList<PersonalBestResponse>>;
