using AiAgentVkusvill.Api.Configuration;
using AiAgentVkusvill.Api.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace AiAgentVkusvill.Api.Services;

public sealed class AiAgentService : IAsyncDisposable
{    
    private readonly ILogger<AiAgentService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private ChatClient? _chatClient;
    private McpClient? _mcpClient;
    private List<ChatTool>? _tools;
    private bool _initialized;

    private readonly AiConfig _aiConfig;
    private readonly ProxyConfig _proxyConfig;    

    public AiAgentService(
        IOptions<AiConfig> aiConfig,
        IOptions<ProxyConfig> proxyConfig,        
        ILogger<AiAgentService> logger)
    {
        _aiConfig = aiConfig.Value;
        _proxyConfig = proxyConfig.Value;        
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var openAiOptions = new OpenAIClientOptions();            
            if (_proxyConfig.Enabled)
            {                
                var proxyUri = new Uri($"http://{_proxyConfig.Ip}:{_proxyConfig.Port}");
                var proxy = new WebProxy(proxyUri);

                if (!string.IsNullOrEmpty(_proxyConfig.Login) && !string.IsNullOrEmpty(_proxyConfig.Password))
                {
                    proxy.Credentials = new NetworkCredential(_proxyConfig.Login, _proxyConfig.Password);
                }

                var handler = new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true,
                };

                openAiOptions.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
                _logger.LogInformation("OpenAI client configured with proxy {ProxyIp}:{ProxyPort}", _proxyConfig.Ip, _proxyConfig.Port);
            }

            var openAi = new OpenAIClient(new System.ClientModel.ApiKeyCredential(_aiConfig.ApiKey), openAiOptions);
            _chatClient = openAi.GetChatClient(_aiConfig.Model);


            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_aiConfig.McpUrl)
            });

            _mcpClient = await McpClient.CreateAsync(transport, cancellationToken: ct);

            await LoadToolsAsync(ct);

            _initialized = true;
            _logger.LogInformation("AiAgentService initialized with {ToolCount} tools", _tools?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AiAgentService");
            throw;
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

        history.Add(new UserChatMessage(userMessage));

        var options = new ChatCompletionOptions { Temperature = 0.1f };

        foreach (var tool in _tools!)
            options.Tools.Add(tool);

        for (int round = 0; round < _aiConfig.MaxToolRounds; round++)
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
