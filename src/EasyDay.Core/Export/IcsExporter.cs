using System.Text;
using EasyDay.Core.Models;

namespace EasyDay.Core.Export;

public static class IcsExporter
{
    public static string Build(IEnumerable<CalendarEvent> events, DateTime? stamp = null)
    {
        var now = stamp ?? DateTime.Now;
        var builder = new StringBuilder();
        builder.AppendLine("BEGIN:VCALENDAR");
        builder.AppendLine("VERSION:2.0");
        builder.AppendLine("PRODID:-//EasyDay//Desktop Calendar//CN");
        builder.AppendLine("CALSCALE:GREGORIAN");
        builder.AppendLine("METHOD:PUBLISH");
        builder.AppendLine("X-WR-TIMEZONE:Asia/Shanghai");

        foreach (var item in events.Where(e => e.Status != EventStatus.Cancelled))
        {
            builder.AppendLine("BEGIN:VEVENT");
            builder.AppendLine($"UID:easyday-{item.Id}-{item.Start:yyyyMMddHHmm}@local");
            builder.AppendLine($"DTSTAMP:{now:yyyyMMddTHHmmss}");
            builder.AppendLine($"SUMMARY:{Escape(item.Title)}");
            builder.AppendLine($"DESCRIPTION:{Escape(BuildDescription(item))}");
            if (!string.IsNullOrWhiteSpace(item.Location))
            {
                builder.AppendLine($"LOCATION:{Escape(item.Location!)}");
            }

            if (item.AllDay)
            {
                builder.AppendLine($"DTSTART;VALUE=DATE:{item.Start:yyyyMMdd}");
                builder.AppendLine($"DTEND;VALUE=DATE:{item.End.Date:yyyyMMdd}");
            }
            else
            {
                builder.AppendLine($"DTSTART:{item.Start:yyyyMMddTHHmmss}");
                builder.AppendLine($"DTEND:{item.End:yyyyMMddTHHmmss}");
            }

            if (!string.IsNullOrWhiteSpace(item.RecurrenceRule))
            {
                builder.AppendLine($"RRULE:{item.RecurrenceRule}");
            }

            builder.AppendLine("END:VEVENT");
        }

        builder.AppendLine("END:VCALENDAR");
        return builder.ToString();
    }

    public static void ExportToFile(string path, IEnumerable<CalendarEvent> events)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, Build(events), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildDescription(CalendarEvent item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            parts.Add(item.Notes!);
        }

        parts.Add($"来源：{item.Source}");
        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            parts.Add($"地点：{item.Location}");
        }

        return string.Join("\n", parts);
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }
}
