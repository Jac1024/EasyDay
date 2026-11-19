using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.ViewModels;

public sealed class EventItemViewModel
{
    public EventItemViewModel(EventOccurrence occurrence)
    {
        Occurrence = occurrence;
    }

    public EventOccurrence Occurrence { get; }

    public string TimeText => Occurrence.Source.AllDay
        ? "全天"
        : Occurrence.Start.ToString("HH:mm");

    public string EndTimeText => Occurrence.Source.AllDay
        ? string.Empty
        : Occurrence.End.ToString("HH:mm");

    public string TimeRangeText => Occurrence.Source.AllDay
        ? "全天"
        : $"{Occurrence.Start:HH:mm} – {Occurrence.End:HH:mm}";

    public string Title => Occurrence.Source.Title;

    public string LocationText => Occurrence.Source.Location ?? string.Empty;

    public bool HasLocation => !string.IsNullOrWhiteSpace(Occurrence.Source.Location);

    public bool IsCompleted => Occurrence.Source.Status == EventStatus.Completed;

    public string RecurringMark => Occurrence.IsRecurringInstance ? "↻" : string.Empty;

    public string ToolTipText
    {
        get
        {
            var lines = new List<string> { TimeRangeText, Title };
            if (HasLocation)
            {
                lines.Add(Occurrence.Source.Location!);
            }

            if (!string.IsNullOrWhiteSpace(Occurrence.Source.Notes))
            {
                lines.Add(Occurrence.Source.Notes!);
            }

            if (Occurrence.Source.Status == EventStatus.Completed)
            {
                lines.Add("（已完成）");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
