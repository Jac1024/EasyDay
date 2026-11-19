using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using EasyDay.App.ViewModels;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.Views;

public partial class DraftCardWindow : Window
{
    public DraftCardWindow(IEnumerable<EventDraft> drafts, string? error, IEnumerable<CalendarEvent>? existing = null)
    {
        InitializeComponent();

        Drafts = new ObservableCollection<DraftItemViewModel>(drafts.Select(d => new DraftItemViewModel(d)));
        ItemsList.ItemsSource = Drafts;

        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorText.Text = error;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        var raw = drafts
            .Select(d => d.RawText)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(raw))
        {
            RawBox.Text = raw;
            RawExpander.Visibility = Visibility.Visible;
        }

        MarkDuplicates(existing);
        ClarifyButton.Visibility = Drafts.Any(d => d.HasQuestions) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MarkDuplicates(IEnumerable<CalendarEvent>? existing)
    {
        var known = existing?.ToList() ?? new List<CalendarEvent>();
        var seen = new List<CalendarEvent>();
        var duplicates = 0;

        foreach (var item in Drafts)
        {
            var draft = item.Build(out _);
            if (draft is null)
            {
                continue;
            }

            var match = DuplicateDetector.Find(draft, known.Concat(seen));
            if (match is not null)
            {
                item.MarkDuplicate(match);
                duplicates++;
            }

            seen.Add(draft.ToEvent());
        }

        if (duplicates > 0)
        {
            DuplicateBannerText.Text = $"{duplicates} 条与已有日程重复，已自动取消勾选；仍要添加请手动勾选。";
            DuplicateBanner.Visibility = Visibility.Visible;
        }
    }

    public ObservableCollection<DraftItemViewModel> Drafts { get; }

    public List<EventDraft> Result { get; } = new();

    public bool ReParseRequested { get; private set; }

    public List<(string Question, string Answer)> Answers { get; } = new();

    private void Clarify_Click(object sender, RoutedEventArgs e)
    {
        Answers.Clear();

        foreach (var question in Drafts.SelectMany(d => d.Questions))
        {
            if (!string.IsNullOrWhiteSpace(question.Answer))
            {
                Answers.Add((question.Text, question.Answer.Trim()));
            }
        }

        if (Answers.Count == 0)
        {
            ShowError("请先在问题下面点一个候选或补充一句，再点「补充并重新识别」。");
            return;
        }

        ReParseRequested = true;
        DialogResult = true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Result.Clear();

        foreach (var item in Drafts.Where(d => d.IsSelected))
        {
            var draft = item.Build(out var error);
            if (draft is null)
            {
                ShowError($"「{item.Title}」：{error}");
                return;
            }

            Result.Add(draft);
        }

        if (Result.Count == 0)
        {
            ShowError("至少选择一条日程。");
            return;
        }

        DialogResult = true;
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
