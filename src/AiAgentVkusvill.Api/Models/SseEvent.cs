namespace AiAgentVkusvill.Api.Models;

public sealed class SseEvent
{
    public string Type { get; }
    public string Data { get; }

    public SseEvent(string type, string data)
    {
        Type = type;
        Data = data;
    }
}
