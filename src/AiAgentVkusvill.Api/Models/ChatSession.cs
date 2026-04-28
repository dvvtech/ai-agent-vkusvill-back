using OpenAI.Chat;

namespace AiAgentVkusvill.Api.Models;

public sealed class ChatSession
{
    public string SessionId { get; }
    public List<ChatMessage> Messages { get; } = [];
    public DateTime CreatedAt { get; }
    public DateTime LastAccessedAt { get; set; }
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public ChatSession(string sessionId)
    {
        SessionId = sessionId;
        CreatedAt = DateTime.UtcNow;
        LastAccessedAt = DateTime.UtcNow;
    }
}
