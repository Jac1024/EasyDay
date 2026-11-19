using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Assistant;
using EasyDay.Core.Data;
using EasyDay.Core.Models;

namespace EasyDay.App.Views;

/// <summary>
/// 自然语言助手：查空闲、改期、取消。
/// 和 AI 草稿卡一样，助手只给方案，用户点「确认」后才写库。
/// </summary>
public partial class AssistantWindow : Window
{
    private readonly EventStore _store;
    private readonly SettingsService _settings;
    private readonly AssistantService _assistant = new();

    private AssistantResult? _current;
    private int? _preferEventId;
    private string _lastInput = string.Empty;

    public AssistantWindow(EventStore store, SettingsService settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;

        Loaded += (_, _) => InputBox.Focus();
    }

    /// <summary>输入的是一句新建日程的话，交给 AI 草稿卡处理。</summary>
    public event Action<string>? CreateRequested;

    /// <summary>已改期/已取消，通知外层刷新挂件。</summary>
    public event Action? Changed;

    private void Ask(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            InputBox.Focus();
            return;
        }

        _lastInput = text.Trim();
        _current = _assistant.Interpret(_lastInput, _store.GetAll(), DateTime.Now, _preferEventId);
        Render(_current);
        InputBox.Focus();
    }

    private void Render(AssistantResult result)
    {
        AnswerText.Text = result.Message;
        AnswerText.Foreground = (System.Windows.Media.Brush)FindResource(
            result.Error is null ? "TextBrush" : "DangerBrush");

        CandidatePanel.Children.Clear();
        if (result.NeedsClarification)
        {
            foreach (var occurrence in result.Related.Take(3))
            {
                var button = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    Content = $"{AssistantFormat.Span(occurrence.Start, occurrence.End, DateTime.Now)}   {occurrence.Source.Title}",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 6),
                    FontSize = 12
                };

                var id = occurrence.Source.Id;
                button.Click += (_, _) =>
                {
                    _preferEventId = id;
                    Ask(_lastInput);
                };

                CandidatePanel.Children.Add(button);
            }
        }

        CreateButton.Visibility = result.Intent == AssistantIntent.Create
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyButton.Visibility = result.CanApply ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.Content = result.Intent == AssistantIntent.Cancel ? "确认取消" : "确认改期";

        StatusText.Text = result.CanApply
            ? "确认后才会写入日历，可再次修改说法"
            : "Enter 发送 · Shift+Enter 换行 · Esc 关闭";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { CanApply: true, Target: not null } plan)
        {
            return;
        }

        try
        {
            if (plan.Intent == AssistantIntent.Cancel)
            {
                _store.SetStatus(plan.Target.Id, EventStatus.Cancelled);
                AnswerText.Text = $"已取消「{plan.Target.Title}」。";
            }
            else
            {
                var target = _store.Get(plan.Target.Id);
                if (target is null || plan.NewStart is null || plan.NewEnd is null)
                {
                    AnswerText.Text = "这条日程已经不在了，可能刚被删除。";
                    return;
                }

                var offsets = ReminderOffsets(target);
                var start = plan.NewStart.Value;
                target.Start = target.AllDay ? start.Date : start;
                target.End = target.AllDay ? start.Date.AddDays(1) : plan.NewEnd.Value;
                _store.Save(target, offsets);

                AnswerText.Text = $"已改期：「{target.Title}」现在是 " +
                                  AssistantFormat.When(target.Start, target.End, DateTime.Now) + "。";
            }

            AnswerText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            ApplyButton.Visibility = Visibility.Collapsed;
            CreateButton.Visibility = Visibility.Collapsed;
            CandidatePanel.Children.Clear();
            StatusText.Text = "已保存，挂件会立即更新";

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            FileLog.Error("助手保存失败", ex);
            AnswerText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AnswerText.Text = "保存失败：" + ex.Message;
        }
    }

    private IReadOnlyList<int> ReminderOffsets(CalendarEvent target)
    {
        var offsets = target.Reminders
            .Select(r => (int)Math.Round((target.Start - r.FireAt).TotalMinutes))
            .Where(minutes => minutes > 0)
            .Distinct()
            .ToList();

        if (offsets.Count == 0)
        {
            offsets.Add(_settings.Current.DefaultReminderMinutes);
        }

        return offsets;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            return;
        }

        CreateRequested?.Invoke(InputBox.Text.Trim());
    }

    private void Example_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string text })
        {
            return;
        }

        _preferEventId = null;
        InputBox.Text = text;
        Ask(text);
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _preferEventId = null;
            Ask(InputBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
