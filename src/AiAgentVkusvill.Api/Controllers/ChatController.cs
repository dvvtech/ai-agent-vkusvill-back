using AiAgentVkusvill.Api.Models;
using AiAgentVkusvill.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Channels;

namespace AiAgentVkusvill.Api.Controllers;

[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly AiAgentService _agentService;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        AiAgentService agentService,
        SessionManager sessionManager,
        ILogger<ChatController> logger)
    {
        _agentService = agentService;
        _sessionManager = sessionManager;
        _logger = logger;
    }    

    [HttpPost("send")]
    public async Task Send([FromBody] ChatRequest request, CancellationToken ct)
    {
        var sessionId = GetSessionId();
        var session = _sessionManager.GetOrCreateSession(sessionId);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(50)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        await session.Lock.WaitAsync(ct);
        var processingTask = ProcessWithReleaseAsync(
            session, request.Message, channel.Writer, ct);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(new { type = evt.Type, data = evt.Data });
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client disconnected for session {SessionId}", sessionId);
        }

        await processingTask;
    }

    private async Task ProcessWithReleaseAsync(
        ChatSession session,
        string userMessage,
        ChannelWriter<SseEvent> writer,
        CancellationToken ct)
    {
        try
        {
            await _agentService.ProcessQueryAsync(session.Messages, userMessage, writer, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing query for session {SessionId}", session.SessionId);
            try
            {
                await writer.WriteAsync(new SseEvent("error",
                    JsonSerializer.Serialize(new { message = ex.Message })), ct);
            }
            catch { /* channel might be closed */ }
        }
        finally
        {
            session.Lock.Release();
            writer.Complete();
        }
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        var sessionId = GetSessionId();
        _sessionManager.ResetSession(sessionId);
        return Ok(new { message = "Сессия сброшена" });
    }

    private string GetSessionId()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = Guid.NewGuid().ToString();
        }

        return sessionId;
    }

    //https://api.cloud-platform.pro/ai-agent-vkusvill/chat/test
    [HttpGet("test")]
    public ActionResult<string> Test()
    {
        _logger.LogInformation("hello");
        return Ok("1278");
    }
}
