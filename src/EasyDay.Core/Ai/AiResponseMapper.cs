using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDay.Core.Models;
using EasyDay.Core.Parsing;

namespace EasyDay.Core.Ai;

public sealed class AiParseResponse
{
    [JsonPropertyName("events")]
    public List<AiEventDto>? Events { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class AiEventDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("allDay")]
    public bool? AllDay { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("attendees")]
    public List<string>? Attendees { get; set; }

    [JsonPropertyName("reminders")]
    public List<int>? Reminders { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("recurrence")]
    public string? Recurrence { get; set; }

    [JsonPropertyName("questions")]
    public List<AiQuestionDto>? Questions { get; set; }
}

public sealed class AiQuestionDto
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }
}

public static class AiResponseMapper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static List<EventDraft> Map(
        string json,
        string? rawText,
        EventSource source,
        DateTime now,
        TimeParseResult? hint = null)
    {
        var payload = ExtractJson(json);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new List<EventDraft>();
        }

        AiParseResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<AiParseResponse>(payload, Options);
        }
        catch (JsonException)
        {
            return new List<EventDraft>();
        }

        if (response?.Events is null || response.Events.Count == 0)
        {
            return new List<EventDraft>();
        }

        var drafts = new List<EventDraft>();
        foreach (var dto in response.Events)
        {
            var draft = MapOne(dto, rawText, source, now, hint);
            if (draft is not null)
            {
                drafts.Add(draft);
            }
        }

        return drafts;
    }

    private static EventDraft? MapOne(
        AiEventDto dto,
        string? rawText,
        EventSource source,
        DateTime now,
        TimeParseResult? hint)
    {
        var title = dto.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) && hint is not null)
        {
            title = hint.Title;
        }

        var start = ParseDateTime(dto.Start) ?? CombineHint(hint, now);
        var allDay = dto.AllDay ?? false;

        if (start is null)
        {
            start = now.Date.AddHours(9);
        }

        DateTime end;
        var parsedEnd = ParseDateTime(dto.End);
        if (parsedEnd is { } e && e > start)
        {
            end = e;
        }
        else if (allDay)
        {
            end = start.Value.Date.AddDays(1);
        }
        else
        {
            end = start.Value.AddHours(1);
        }

        if (end <= start)
        {
            end = allDay ? start.Value.Date.AddDays(1) : start.Value.AddHours(1);
        }

        var notes = dto.Notes?.Trim();
        if (dto.Attendees is { Count: > 0 })
        {
            var attendees = string.Join("、", dto.Attendees.Where(x => !string.IsNullOrWhiteSpace(x)));
            notes = string.IsNullOrWhiteSpace(notes) ? $"参与人：{attendees}" : $"{notes}\n参与人：{attendees}";
        }

        var draft = new EventDraft
        {
            Title = string.IsNullOrWhiteSpace(title) ? "未命名日程" : title!,
            Start = start.Value,
            End = end,
            AllDay = allDay,
            Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim(),
            Notes = notes,
            RawText = rawText,
            Source = source,
            Confidence = dto.Confidence ?? 0.7,
            RecurrenceRule = NormalizeRecurrence(dto.Recurrence)
        };

        if (dto.Reminders is { Count: > 0 })
        {
            draft.ReminderMinutes.AddRange(dto.Reminders.Where(x => x is >= 0 and <= 10080));
        }

        if (dto.Questions is { Count: > 0 })
        {
            foreach (var question in dto.Questions)
            {
                if (string.IsNullOrWhiteSpace(question.Question))
                {
                    continue;
                }

                draft.Questions.Add(new ClarificationQuestion
                {
                    Field = question.Field?.Trim() ?? string.Empty,
                    Question = question.Question!.Trim(),
                    Options = question.Options?
                        .Where(o => !string.IsNullOrWhiteSpace(o))
                        .Select(o => o.Trim())
                        .Take(4)
                        .ToList() ?? new List<string>()
                });
            }
        }

        draft.Normalize(now);

        if (draft.Questions.Count == 0 && draft.Confidence < 0.6)
        {
            draft.Questions.Add(new ClarificationQuestion
            {
                Field = "time",
                Question = "这条信息我拿不太准：具体是哪天、几点？",
                Options = { "今天", "明天", "后天" }
            });
        }

        if (!string.IsNullOrWhiteSpace(dto.Start) && ParseDateTime(dto.Start) is null)
        {
            draft.Warnings.Add("AI 返回的时间格式无法识别，已按本地规则推测");
        }

        return draft;
    }

    private static string? NormalizeRecurrence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains("FREQ=", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToUpperInvariant();
        }

        return null;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return DateTime.SpecifyKind(exact, DateTimeKind.Local);
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var loose))
        {
            return DateTime.SpecifyKind(loose, DateTimeKind.Local);
        }

        return null;
    }

    private static DateTime? CombineHint(TimeParseResult? hint, DateTime now)
    {
        if (hint?.Date is null && hint?.Start is null)
        {
            return null;
        }

        var date = hint!.Date ?? DateOnly.FromDateTime(now);
        var time = hint.Start ?? new TimeOnly(9, 0);
        return date.ToDateTime(time);
    }

    /// <summary>把外围的 ```json 围栏、前后废话剥掉。</summary>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = text.IndexOf('\n', fence);
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                text = text[(start + 1)..end].Trim();
            }
        }

        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first >= 0 && last > first)
        {
            text = text[first..(last + 1)];
        }

        return text;
    }
}
