using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Hearthaven.Core.Chat;

/// <summary>
/// OpenAI兼容API的流式通信实现
/// 支持任意兼容OpenAI格式的Endpoint（Ollama、LM Studio、DeepSeek等）
/// </summary>
public class OpenAIProvider : IChatProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIConfig _config;

    public string ProviderName => "OpenAI Compatible";

    /// <summary>
    /// 构造 OpenAI Provider。
    /// HttpClient 必须由外部注入（建议在 UI 层创建共享实例或通过 IHttpClientFactory 管理），
    /// 避免内部 new HttpClient() 导致的 Socket 耗尽风险。
    /// </summary>
    public OpenAIProvider(OpenAIConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    /// <summary>
    /// 流式对话（支持工具调用）— 每次 yield 一个 StreamEvent。
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> StreamChatWithToolsAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(body, SseJsonContext.Options);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "Authorization", $"Bearer {_config.ApiKey}" } }
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"API 返回错误 ({(int)response.StatusCode}): {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // 流式读取空闲超时 — 30 秒内无新数据则结束
        var idleTimeout = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            using var timeoutCts = new CancellationTokenSource(idleTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            string? line;
            try
            {
                line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 空闲超时或用户取消，均正常结束
                yield break;
            }

            if (line == null) yield break;

            if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line.AsSpan(6);

            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                yield break;

            foreach (var evt in ExtractStreamEvents(data.ToString()))
                yield return evt;
        }
    }

    // ──────────────── 请求体构建 ────────────────

    private Dictionary<string, object> BuildRequestBody(ChatRequest request)
    {
        var messages = BuildMessages(request);

        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = true
        };

        // 工具定义
        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(t => t.ToApiObject()).ToList();
            body["tool_choice"] = request.ToolChoice ?? "auto";
        }

        return body;
    }

    /// <summary>
    /// 构建 messages 数组（含 tool 角色消息的 tool_call_id 字段）。
    /// </summary>
    private static List<object> BuildMessages(ChatRequest request)
    {
        var messages = new List<object>();

        foreach (var msg in request.Messages)
        {
            var dict = new Dictionary<string, object>
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            };

            // tool 角色的消息需要携带 tool_call_id
            if (msg.Role == "tool" && msg.ToolCallId != null)
            {
                dict["tool_call_id"] = msg.ToolCallId;
            }

            // assistant 角色的消息携带 tool_calls（如果有）
            if (msg.Role == "assistant" && msg.ToolCalls != null)
            {
                dict["tool_calls"] = JsonSerializer.SerializeToElement(msg.ToolCalls, SseJsonContext.Options);
            }

            // assistant 角色的消息携带 reasoning_content（DeepSeek thinking mode）
            if (msg.Role == "assistant" && msg.ReasoningContent != null)
            {
                dict["reasoning_content"] = msg.ReasoningContent;
            }

            messages.Add(dict);
        }

        return messages;
    }

    // ──────────────── SSE 解析 ────────────────

    /// <summary>
    /// 从 SSE data 行中提取 StreamEvent（文本块或工具调用块）。
    /// 一次可能 yield 0 到多个事件。
    /// </summary>
    private static IEnumerable<StreamEvent> ExtractStreamEvents(string data)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() <= 0)
                yield break;

            var choice = choices[0];

            if (!choice.TryGetProperty("delta", out var delta))
                yield break;

            // 1. 文本块
            if (delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return new TextChunk(text);
            }

            // 2. 思考内容（DeepSeek thinking mode）
            if (delta.TryGetProperty("reasoning_content", out var reasoning) &&
                reasoning.ValueKind == JsonValueKind.String)
            {
                var text = reasoning.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return new ThinkingChunk(text);
            }

            // 3. 工具调用
            if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCalls.EnumerateArray())
                {
                    if (!tc.TryGetProperty("index", out var indexProp) ||
                        indexProp.ValueKind != JsonValueKind.Number)
                        continue;

                    var index = indexProp.GetInt32();
                    string? id = null;
                    string? name = null;
                    string? args = null;

                    if (tc.TryGetProperty("id", out var idProp) &&
                        idProp.ValueKind == JsonValueKind.String)
                        id = idProp.GetString();

                    if (tc.TryGetProperty("function", out var func) &&
                        func.ValueKind == JsonValueKind.Object)
                    {
                        if (func.TryGetProperty("name", out var nameProp) &&
                            nameProp.ValueKind == JsonValueKind.String)
                            name = nameProp.GetString();

                        if (func.TryGetProperty("arguments", out var argsProp) &&
                            argsProp.ValueKind == JsonValueKind.String)
                            args = argsProp.GetString();
                    }

                    yield return new ToolCallChunk(index, id, name, args);
                }
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

