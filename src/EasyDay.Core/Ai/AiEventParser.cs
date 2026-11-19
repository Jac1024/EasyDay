using EasyDay.Core.Models;
using EasyDay.Core.Parsing;

namespace EasyDay.Core.Ai;

public sealed record ParseOutcome(
    IReadOnlyList<EventDraft> Drafts,
    string? Error,
    bool UsedFallback,
    string? RawJson = null);

/// <summary>
/// 摄入管线：规则预解析 → AI 抽取 → 校验 → 草稿。
/// AI 不可用时自动降级为本地规则，功能不中断。
/// </summary>
public sealed class AiEventParser
{
    private readonly IAiClient _ai;
    private readonly Func<AppSettings> _settings;

    public AiEventParser(IAiClient ai, Func<AppSettings> settings)
    {
        _ai = ai;
        _settings = settings;
    }

    public async Task<ParseOutcome> ParseAsync(
        string? text,
        byte[]? image,
        EventSource source,
        DateTime now,
        string? extraContext = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedText = string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
        var hint = normalizedText is null ? null : ChineseTimeParser.Parse(normalizedText, now);
        var fallback = normalizedText is null ? null : LocalDraftFactory.Build(normalizedText, now, source);

        var settings = _settings();

        if (!settings.HasAiConfig)
        {
            return fallback is null
                ? new ParseOutcome(
                    Array.Empty<EventDraft>(),
                    "还没有配置 AI 接口：请在设置里填写接口地址、API Key 和模型，然后再识别截图。",
                    true)
                : new ParseOutcome(
                    new[] { fallback },
                    "AI 接口没配置完整，已用本地规则解析（准确率较低）",
                    true);
        }

        if (image is null && normalizedText is null)
        {
            return new ParseOutcome(Array.Empty<EventDraft>(), "没有可识别的内容。", true);
        }

        try
        {
            var system = PromptBuilder.SystemPrompt(now, settings.City);
            var user = PromptBuilder.UserPrompt(normalizedText, hint, image is not null, extraContext);
            var json = await _ai.CompleteJsonAsync(new AiRequest(system, user, image), cancellationToken)
                .ConfigureAwait(false);

            var drafts = AiResponseMapper.Map(json, normalizedText, source, now, hint);
            if (drafts.Count == 0)
            {
                return fallback is not null
                    ? new ParseOutcome(new[] { fallback }, "AI 没有识别到日程，已用本地规则兜底。", true, json)
                    : new ParseOutcome(Array.Empty<EventDraft>(), "AI 没有识别到日程，换一张更完整的截图试试。", false, json);
            }

            return new ParseOutcome(drafts, null, false, json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return fallback is not null
                ? new ParseOutcome(new[] { fallback }, "AI 解析失败，已用本地规则兜底：" + ex.Message, true)
                : new ParseOutcome(Array.Empty<EventDraft>(), "AI 解析失败：" + ex.Message, true);
        }
    }
}
