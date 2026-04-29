using AiAgentVkusvill.Api.Configuration;
using AiAgentVkusvill.Api.Models;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Collections.Concurrent;

namespace AiAgentVkusvill.Api.Services;

public sealed class SessionManager : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;    
    private readonly AiConfig _aiConfig;
    private Timer? _cleanupTimer;

    public SessionManager(
        IOptions<AiConfig> aiConfig,
        ILogger<SessionManager> logger)
    {
        _aiConfig = aiConfig.Value;
        _logger = logger;
    }

    public ChatSession GetOrCreateSession(string sessionId)
    {
        var timeout = TimeSpan.FromHours(_aiConfig.SessionTimeoutHours);

        var session = _sessions.AddOrUpdate(
            sessionId,
            id => CreateSession(id),
            (id, existing) =>
            {
                if (DateTime.UtcNow - existing.LastAccessedAt > timeout)
                {
                    _logger.LogInformation("Session {SessionId} expired, creating new", id);
                    return CreateSession(id);
                }

                existing.LastAccessedAt = DateTime.UtcNow;
                return existing;
            });

        return session;
    }

    private static ChatSession CreateSession(string sessionId)
    {
        var session = new ChatSession(sessionId);

        session.Messages.Add(new SystemChatMessage(
            """
            Ты AI агент Вкусовилл — помогаешь пользователю собирать корзину покупок.

            Если для ответа нужен инструмент — обязательно вызывай tool.
            Если можешь ответить сам — не вызывай инструмент.
            Для инструментов строго используй параметры схемы.
            Не вызывай search без query.

            Если пользователь хочет начать сборку новой корзины —
            сообщи ему, что можно нажать кнопку «Новая корзина»
            для сброса текущего заказа и начала с чистого листа.
            """));

        return session;
    }

    public void ResetSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out _))
        {
            _logger.LogInformation("Session {SessionId} reset", sessionId);
        }
    }

    private void RemoveExpiredSessions()
    {
        var timeout = TimeSpan.FromHours(_aiConfig.SessionTimeoutHours);
        var cutoff = DateTime.UtcNow - timeout;

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.LastAccessedAt < cutoff)
            {
                if (_sessions.TryRemove(kvp.Key, out _))
                {
                    _logger.LogInformation("Cleaned up expired session {SessionId}", kvp.Key);
                }
            }
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cleanupTimer = new Timer(
            _ => RemoveExpiredSessions(),
            null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(30));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
