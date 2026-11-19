using System.Globalization;
using System.Collections.ObjectModel;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.ViewModels;

/// <summary>确认卡上的一条可编辑草稿。</summary>
public sealed class DraftItemViewModel : ObservableObject
{
    private bool _isSelected = true;
    private string _title;
    private string _dateText;
    private string _startText;
    private string _endText;
    private bool _allDay;
    private string _location;

    public DraftItemViewModel(EventDraft draft)
    {
        Original = draft;
        _title = draft.Title;
        _dateText = draft.Start.ToString("yyyy-MM-dd");
        _startText = draft.Start.ToString("HH:mm");
        _endText = draft.End.ToString("HH:mm");
        _allDay = draft.AllDay;
        _location = draft.Location ?? string.Empty;
        RawText = draft.RawText ?? string.Empty;
        Confidence = draft.Confidence;
        Warnings = draft.Warnings.ToList();

        _dateText = draft.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var question in draft.Questions)
        {
            Questions.Add(new QuestionViewModel(question));
        }
    }

    public EventDraft Original { get; }

    public ObservableCollection<QuestionViewModel> Questions { get; } = new();

    public bool HasQuestions => Questions.Count > 0;

    public string SourceText => Original.Source switch
    {
        EventSource.Screenshot => "截图识别",
        EventSource.Clipboard => "剪贴板",
        EventSource.QuickAdd => "速记",
        EventSource.Import => "导入",
        _ => "手动"
    };

    public string ConfidenceText => $"置信度 {Confidence:P0}";

    public double Confidence { get; }

    public bool IsLowConfidence => Confidence < 0.6;

    public bool IsDuplicate { get; private set; }

    public string DuplicateText { get; private set; } = string.Empty;

    public void MarkDuplicate(DuplicateMatch match)
    {
        IsDuplicate = true;
        DuplicateText = $"已存在：{match.Start:MM-dd HH:mm} {match.Existing.Title}";
        IsSelected = false;
        OnPropertyChanged(nameof(IsDuplicate));
        OnPropertyChanged(nameof(DuplicateText));
    }

    public string RawText { get; }

    public bool HasRawText => !string.IsNullOrWhiteSpace(RawText);

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    public string WarningText => string.Join("；", Warnings);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string DateText
    {
        get => _dateText;
        set => SetProperty(ref _dateText, value);
    }

    public string StartText
    {
        get => _startText;
        set => SetProperty(ref _startText, value);
    }

    public string EndText
    {
        get => _endText;
        set => SetProperty(ref _endText, value);
    }

    public bool AllDay
    {
        get => _allDay;
        set => SetProperty(ref _allDay, value);
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    /// <summary>把界面上的编辑结果写回 EventDraft；解析失败返回 null 并给出错误。</summary>
    public EventDraft? Build(out string? error)
    {
        error = null;

        if (!DateOnly.TryParseExact(DateText.Trim(), new[] { "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            error = "日期格式应为 yyyy-MM-dd";
            return null;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            error = "标题不能为空";
            return null;
        }

        var draft = new EventDraft
        {
            Title = Title.Trim(),
            Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            Notes = Original.Notes,
            Source = Original.Source,
            RawText = Original.RawText,
            Confidence = Original.Confidence,
            RecurrenceRule = Original.RecurrenceRule,
            AllDay = AllDay
        };

        draft.ReminderMinutes.AddRange(Original.ReminderMinutes);

        if (AllDay)
        {
            draft.Start = date.ToDateTime(TimeOnly.MinValue);
            draft.End = draft.Start.AddDays(1);
        }
        else
        {
            if (!TryParseTime(StartText, out var start))
            {
                error = "开始时间格式应为 HH:mm";
                return null;
            }

            if (!TryParseTime(EndText, out var end))
            {
                error = "结束时间格式应为 HH:mm";
                return null;
            }

            draft.Start = date.ToDateTime(start);
            draft.End = date.ToDateTime(end);
            if (draft.End <= draft.Start)
            {
                draft.End = draft.Start.AddHours(1);
            }
        }

        draft.Normalize();
        return draft;
    }

    private static bool TryParseTime(string text, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
