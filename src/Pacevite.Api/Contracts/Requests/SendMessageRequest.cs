using Pacevite.Api.Infrastructure.Chat;

namespace Pacevite.Api.Contracts.Requests;

public sealed record SendMessageRequest(
    string Message,
    IReadOnlyList<ConversationMessage> History);
