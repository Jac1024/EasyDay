using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using EasyDay.Core.Models;

namespace EasyDay.Core.Ai;

/// <summary>兼容 /chat/completions 协议的 AI 客户端，接口地址、Key、模型都由用户配置。</summary>
public sealed class ChatCompletionsClient : IAiClient
{
    private static readonly string UserAgent = BuildUserAgent();

    private readonly Func<AppSettings> _settings;
    private readonly HttpClient? _injected;
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public ChatCompletionsClient(
        Func<AppSettings> settings,
        HttpClient? http = null)
    {
        _settings = settings;
        _injected = http;
    }

    private static string BuildUserAgent()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        }
        else
        {
            var plus = version.IndexOf('+');
            if (plus > 0)
            {
                version = version[..plus];
            }
        }

        return $"EasyDay/{version}";
    }

    private HttpClient GetHttpClient(AppSettings settings)
    {
        if (_injected is not null)
        {
            return _injected;
        }

        var proxy = (settings.AiProxy ?? string.Empty).Trim();
        var key = proxy.Length == 0 ? "direct" : proxy;
        if (_clients.TryGetValue(key, out var client))
        {
            return client;
        }

        var handler = new HttpClientHandler();
        if (proxy.Length == 0 || proxy.Equals("direct", StringComparison.OrdinalIgnoreCase))
        {
            handler.UseProxy = false;
        }
        else if (proxy.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxy);
        }

        client = new HttpClient(handler);
        _clients[key] = client;
        return client;
    }

    public async Task<string> CompleteJsonAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settings();
        if (!settings.HasAiConfig)
        {
            throw new InvalidOperationException("AI 接口未配置完整：请填写接口地址、API Key 和模型");
        }

        var url = BuildUrl(settings.ApiBaseUrl);
        object userContent = request.ImageBytes is null
            ? request.UserText
            : new object[]
            {
                new { type = "text", text = request.UserText },
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{request.ImageMime};base64,{Convert.ToBase64String(request.ImageBytes)}"
                    }
                }
            };

        var body = new
        {
            model = settings.Model,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = userContent }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        foreach (var (name, value) in ParseHeaders(settings.ApiExtraHeaders))
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.AiTimeoutSeconds, 5, 300)));

        using var response = await GetHttpClient(settings).SendAsync(message, timeout.Token).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var text = payload.Length <= 400 ? payload : payload[..400] + "…";
            throw new HttpRequestException($"AI 请求失败 ({(int)response.StatusCode})：{text}");
        }

        using var document = JsonDocument.Parse(payload);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? string.Empty;
    }

    private static string BuildUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/chat/completions";
    }

    private static List<(string Name, string Value)> ParseHeaders(string? raw)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var name = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (name.Length > 0 && value.Length > 0)
            {
                result.Add((name, value));
            }
        }

        return result;
    }
}
