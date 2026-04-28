using AiAgentVkusvill.Api.Models;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using System.Threading.Channels;

namespace AiAgentVkusvill.Api.Services;

public sealed class AiAgentService : IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<AiAgentService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private ChatClient? _chatClient;
    private McpClient? _mcpClient;
    private List<ChatTool>? _tools;
    private bool _initialized;

    private const string SystemPrompt = """
        Ты AI агент Вкусовилл — помогаешь пользователю собирать корзину покупок.

        Если для ответа нужен инструмент — обязательно вызывай tool.
        Если можешь ответить сам — не вызывай инструмент.
        Для инструментов строго используй параметры схемы.
        Не вызывай search без query.

        Если пользователь хочет начать сборку новой корзины —
        сообщи ему, что можно нажать кнопку «Новая корзина»
        для сброса текущего заказа и начала с чистого листа.
        """;

    public AiAgentService(IConfiguration config, ILogger<AiAgentService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var apiKey = _config["AiSettings:ApiKey"]
                         ?? throw new InvalidOperationException("AiSettings:ApiKey not configured");
            var mcpUrl = _config["AiSettings:McpUrl"]
                         ?? throw new InvalidOperationException("AiSettings:McpUrl not configured");
            var model = _config["AiSettings:Model"] ?? "gpt-4o";

            var openAi = new OpenAIClient(apiKey);
            _chatClient = openAi.GetChatClient(model);

            var transport = new HttpClientTransport(
                    new HttpClientTransportOptions
                    {
                        Endpoint =
                            new Uri(mcpUrl)
                    });

            _mcpClient = await McpClient.CreateAsync(transport);

            await LoadToolsAsync(ct);

            _initialized = true;
            _logger.LogInformation("AiAgentService initialized with {ToolCount} tools", _tools?.Count ?? 0);
        }
        catch (Exception ex)
        { 
        
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LoadToolsAsync(CancellationToken ct)
    {
        if (_tools != null) return;

        var mcpTools = await _mcpClient!.ListToolsAsync(cancellationToken: ct);

        _tools = [];

        foreach (var tool in mcpTools)
        {
            _logger.LogInformation("Loaded MCP tool: {ToolName}", tool.Name);

            var schemaJson = JsonSerializer.Serialize(tool.JsonSchema);

            _tools.Add(ChatTool.CreateFunctionTool(
                functionName: tool.Name,
                functionDescription: tool.Description,
                functionParameters: BinaryData.FromString(schemaJson)
            ));
        }
    }

    public async Task ProcessQueryAsync(
        List<ChatMessage> history,
        string userMessage,
        ChannelWriter<SseEvent> writer,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var maxToolRounds = _config.GetValue("AiSettings:MaxToolRounds", 8);

        history.Add(new UserChatMessage(userMessage));

        var options = new ChatCompletionOptions { Temperature = 0.1f };

        foreach (var tool in _tools!)
            options.Tools.Add(tool);

        for (int round = 0; round < maxToolRounds; round++)
        {
            _logger.LogInformation("Round {Round}", round + 1);

            ChatCompletion response;
            try
            {
                var completion = await _chatClient!.CompleteChatAsync(history, options, ct);
                response = completion.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI API error in round {Round}", round + 1);
                await writer.WriteAsync(new SseEvent("error",
                    JsonSerializer.Serialize(new { message = ex.Message })), ct);
                return;
            }

            history.Add(new AssistantChatMessage(response));

            if (response.ToolCalls.Count == 0)
            {
                var text = response.Content[0].Text;
                await writer.WriteAsync(new SseEvent("final_answer",
                    JsonSerializer.Serialize(new { text })), ct);
                return;
            }

            var assistantData = new
            {
                text = response.Content.FirstOrDefault()?.Text ?? "",
                toolCalls = response.ToolCalls.Select(tc => new
                {
                    name = tc.FunctionName,
                    arguments = tc.FunctionArguments.ToString()
                }).ToArray()
            };
            await writer.WriteAsync(new SseEvent("assistant_message",
                JsonSerializer.Serialize(assistantData)), ct);

            foreach (var toolCall in response.ToolCalls)
            {
                var toolResult = await ExecuteToolAsync(toolCall, ct);
                history.Add(new ToolChatMessage(toolCall.Id, toolResult));

                await writer.WriteAsync(new SseEvent("tool_result",
                    JsonSerializer.Serialize(new
                    {
                        toolName = toolCall.FunctionName,
                        result = toolResult
                    })), ct);
            }
        }

        await writer.WriteAsync(new SseEvent("error",
            JsonSerializer.Serialize(new { message = "Превышен лимит вызовов инструментов" })), ct);
    }

    private async Task<string> ExecuteToolAsync(ChatToolCall toolCall, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Calling tool {ToolName}", toolCall.FunctionName);

            var args = ParseArguments(toolCall.FunctionArguments.ToString());
            var mcpResult = await _mcpClient!.CallToolAsync(toolCall.FunctionName, args, cancellationToken: ct);

            var text = mcpResult.Content?.FirstOrDefault()?.ToString() ?? "Empty result";
            //var textParts = mcpResult.Content?
            //    .Where(c => c.Type == "text")
            //    .Select(c => c.Text) ?? [];
            //var text = string.Join("\n", textParts);

            //if (string.IsNullOrEmpty(text))
            //    text = "Empty result";

            _logger.LogInformation("Tool {ToolName} result: {Result}",
                toolCall.FunctionName,
                text.Length > 200 ? text[..200] + "..." : text);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} error", toolCall.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    private static Dictionary<string, object?> ParseArguments(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json!)
                   ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is not null)
        {
            await _mcpClient.DisposeAsync();
        }
    }
}
