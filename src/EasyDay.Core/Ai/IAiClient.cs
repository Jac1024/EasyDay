using EasyDay.Core.Models;
using EasyDay.Core.Parsing;

namespace EasyDay.Core.Ai;

public sealed record AiRequest(
    string SystemPrompt,
    string UserText,
    byte[]? ImageBytes = null,
    string ImageMime = "image/png");

/// <summary>AI 客户端抽象。实现只需返回 JSON 字符串，解析由上层负责。</summary>
public interface IAiClient
{
    Task<string> CompleteJsonAsync(AiRequest request, CancellationToken cancellationToken = default);
}
