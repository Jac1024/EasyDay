using System.Text;
using EasyDay.Core.Models;
using EasyDay.Core.Parsing;

namespace EasyDay.Core.Ai;

public static class PromptBuilder
{
    public static string SystemPrompt(DateTime now, string city)
    {
        var weekday = WeekdayName(now.DayOfWeek);
        var builder = new StringBuilder();
        builder.AppendLine("你是日程信息抽取引擎。把用户提供的微信群通知、聊天记录或截图，转换成结构化日程 JSON。");
        builder.AppendLine();
        builder.AppendLine($"当前时间：{now:yyyy-MM-dd HH:mm}（{weekday}），时区：Asia/Shanghai，用户所在城市：{city}。");
        builder.AppendLine();
        builder.AppendLine("只输出 JSON，不要输出任何解释文字。格式：");
        builder.AppendLine("""
        {"events":[{"title":"项目评审","start":"2026-11-18T15:00:00","end":"2026-11-18T16:00:00","allDay":false,"location":"3 楼会议室","notes":"带上需求文档","attendees":["张三"],"reminders":[15,1440],"confidence":0.9,"recurrence":null,"questions":[{"field":"time","question":"是下午 3 点还是 4 点？","options":["下午 3 点","下午 4 点"]}]}],"reason":"简短说明"}
        """);
        builder.AppendLine();
        builder.AppendLine("规则：");
        builder.AppendLine("1. 一条通知里可能有多个日程（例如宣讲会 + 双选会），必须拆成多个 events。");
        builder.AppendLine("2. 相对时间（明天、下周三、周五前、月底）按上面的当前时间换算成绝对时间，输出 24 小时制本地时间。");
        builder.AppendLine("3. 没有明确时间就 allDay=true，start 用当天 00:00:00；没有结束时间默认 +1 小时（allDay 则为次日 00:00:00）。");
        builder.AppendLine("4. 缺信息就留空，绝不编造地点、人名或链接。");
        builder.AppendLine("5. reminders 是提前分钟数数组：会议用 [15,1440]，截止/提交类用 [60,1440,4320]，出行类用 [120]。");
        builder.AppendLine("6. title 不超过 20 字，是名词短语或动宾短语，不要包含时间、日期、\"通知\"、\"@所有人\"等字样。");
        builder.AppendLine("7. 日期必须在未来一年内；实在无法确定时间时，confidence 低于 0.5。");
        builder.AppendLine("8. recurrence 仅在明确出现\"每天/每周/每月/每周一三五\"时才填写 RRULE，否则为 null。");
        builder.AppendLine("9. \"取消\"、\"改期\"类消息也要提取，并在 notes 里注明原标题与变更。");
        builder.AppendLine("10. 遇到模糊信息（时间不确定、同一句话有多个候选时间、地点/参与人缺失、相对日期指代不清）时，" +
                           "在该 event 的 questions 里提出最多 2 个简短问题，并给出 2-4 个候选 options；信息明确时 questions 为空数组。");
        builder.AppendLine("11. 如果用户提供了补充说明，必须优先采用补充说明里的信息。");
        return builder.ToString();
    }

    public static string UserPrompt(string? text, TimeParseResult? hint, bool hasImage, string? extraContext = null)
    {
        var builder = new StringBuilder();

        if (hasImage)
        {
            builder.AppendLine("请识别这张图片（微信聊天截图）里的日程信息。");
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine("原文：");
            builder.AppendLine("```");
            builder.AppendLine(text.Trim());
            builder.AppendLine("```");
        }

        if (hint is not null)
        {
            builder.AppendLine();
            builder.AppendLine("本地规则预解析结果（仅供参考，可能有误）：");
            builder.AppendLine($"- 日期：{(hint.Date?.ToString("yyyy-MM-dd") ?? "未识别")}");
            builder.AppendLine($"- 开始：{(hint.Start?.ToString("HH:mm") ?? "未识别")}");
            builder.AppendLine($"- 结束：{(hint.End?.ToString("HH:mm") ?? "未识别")}");
            builder.AppendLine($"- 地点：{hint.Location ?? "未识别"}");
            builder.AppendLine($"- 标题候选：{hint.Title ?? "无"}");
        }

        if (!string.IsNullOrWhiteSpace(extraContext))
        {
            builder.AppendLine();
            builder.AppendLine("用户对追问的补充回答（必须优先采用）：");
            builder.AppendLine(extraContext.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("请输出 JSON。");
        return builder.ToString();
    }

    private static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };
}
