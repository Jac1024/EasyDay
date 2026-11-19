using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Ai;
using EasyDay.Core.Data;
using EasyDay.Core.Export;
using EasyDay.Core.Models;

namespace EasyDay.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly EventStore _store;

    public SettingsWindow(SettingsService settings, EventStore store)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        LoadValues();
    }

    public bool Saved { get; private set; }

    private void LoadValues()
    {
        var current = _settings.Current;

        BaseUrlBox.Text = current.ApiBaseUrl;
        ApiKeyBox.Password = current.ApiKey;
        ModelBox.Text = current.Model;
        CityBox.Text = current.City;
        ExtraHeadersBox.Text = current.ApiExtraHeaders;
        SelectProxy(current.AiProxy);

        SelectReminder(current.DefaultReminderMinutes);
        SmartReminderBox.IsChecked = current.SmartReminders;
        ToastBox.IsChecked = current.EnableReminderToasts;
        MorningBox.IsChecked = current.EnableMorningBrief;
        EveningBox.IsChecked = current.EnableEveningBrief;

        SelectDisplayMode(current.WidgetMode);
        StartupBox.IsChecked = current.StartWithWindows;

        var all = _store.GetAll();
        DataInfoText.Text = $"共 {all.Count} 条日程、{all.Sum(x => x.Reminders.Count)} 个提醒。所有数据都保存在本机。";

        if (!current.HasAiConfig)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            StatusText.Text = "第一次使用：先填接口地址、API Key、模型，测试通过后保存。";
        }
    }

    private void SelectReminder(int minutes)
    {
        for (var i = 0; i < DefaultReminderBox.Items.Count; i++)
        {
            if (DefaultReminderBox.Items[i] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var tag) &&
                tag == minutes)
            {
                DefaultReminderBox.SelectedIndex = i;
                return;
            }
        }

        DefaultReminderBox.SelectedIndex = 2;
    }

    private int SelectedReminderMinutes()
    {
        return DefaultReminderBox.SelectedItem is ComboBoxItem item &&
               int.TryParse(item.Tag?.ToString(), out var value)
            ? value
            : 15;
    }

    private AppSettings BuildFromUi()
    {
        var settings = _settings.Current.Clone();
        settings.ApiBaseUrl = BaseUrlBox.Text.Trim();
        settings.ApiKey = ApiKeyBox.Password.Trim();
        settings.Model = ModelBox.Text.Trim();
        settings.City = string.IsNullOrWhiteSpace(CityBox.Text) ? "北京" : CityBox.Text.Trim();
        settings.ApiExtraHeaders = ExtraHeadersBox.Text.Trim();
        settings.AiProxy = SelectedProxy();

        settings.DefaultReminderMinutes = SelectedReminderMinutes();
        settings.SmartReminders = SmartReminderBox.IsChecked == true;
        settings.EnableReminderToasts = ToastBox.IsChecked == true;
        settings.EnableMorningBrief = MorningBox.IsChecked == true;
        settings.EnableEveningBrief = EveningBox.IsChecked == true;
        settings.WidgetMode = SelectedDisplayMode();
        settings.AlwaysOnTop = settings.WidgetMode == "topmost";
        settings.StartWithWindows = StartupBox.IsChecked == true;
        return settings;
    }

    private void SelectProxy(string? value)
    {
        var proxy = (value ?? string.Empty).Trim();
        if (proxy.Length == 0 || proxy.Equals("direct", StringComparison.OrdinalIgnoreCase))
        {
            ProxyModeBox.SelectedIndex = 0;
            ProxyUrlBox.Text = string.Empty;
        }
        else if (proxy.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            ProxyModeBox.SelectedIndex = 1;
            ProxyUrlBox.Text = string.Empty;
        }
        else
        {
            ProxyModeBox.SelectedIndex = 2;
            ProxyUrlBox.Text = proxy;
        }

        UpdateProxyUrlVisibility();
    }

    private string SelectedProxy()
    {
        var tag = (ProxyModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "system" => "system",
            "custom" => ProxyUrlBox.Text.Trim(),
            _ => string.Empty
        };
    }

    private void ProxyMode_Changed(object sender, SelectionChangedEventArgs e) => UpdateProxyUrlVisibility();

    private void UpdateProxyUrlVisibility()
    {
        if (ProxyUrlBox is null)
        {
            return;
        }

        var custom = (ProxyModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "custom";
        ProxyUrlBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        ProxyUrlBox.ToolTip = "例如 http://127.0.0.1:7890";
    }

    private void SelectDisplayMode(string? mode)
    {
        var target = mode is "floating" or "topmost" or "desktop" ? mode : "desktop";
        for (var i = 0; i < DisplayModeBox.Items.Count; i++)
        {
            if (DisplayModeBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                DisplayModeBox.SelectedIndex = i;
                return;
            }
        }

        DisplayModeBox.SelectedIndex = 0;
    }

    private string SelectedDisplayMode()
    {
        return DisplayModeBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? "desktop"
            : "desktop";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var candidate = BuildFromUi();

        if (string.IsNullOrWhiteSpace(candidate.ApiBaseUrl))
        {
            StatusText.Text = "接口地址不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.Model))
        {
            StatusText.Text = "模型名不能为空。";
            return;
        }

        _settings.Current = candidate;
        _settings.Save();
        StartupService.Apply(candidate.StartWithWindows);

        Saved = true;
        DialogResult = true;
    }

    private async void TestAi_Click(object sender, RoutedEventArgs e)
    {
        var temp = BuildFromUi();
        if (!temp.HasAiConfig)
        {
            TestResultText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            TestResultText.Text = "请先填写接口地址、API Key 和模型";
            return;
        }

        TestResultText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        TestResultText.Text = "识别中…";

        try
        {
            var parser = new AiEventParser(new ChatCompletionsClient(() => temp), () => temp);
            var outcome = await parser.ParseAsync(
                "明天下午3点在3楼会议室开项目评审会，提前15分钟提醒",
                null,
                EventSource.Manual,
                DateTime.Now);

            if (outcome.Drafts.Count == 0)
            {
                TestResultText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                TestResultText.Text = outcome.Error ?? "没有识别到日程";
                return;
            }

            var draft = outcome.Drafts[0];
            TestResultText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            TestResultText.Text = $"✓ {draft.Start:MM-dd HH:mm} {draft.Title}";
        }
        catch (Exception ex)
        {
            TestResultText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            TestResultText.Text = ex.Message;
        }
    }

    private void ExportIcs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "日历文件 (*.ics)|*.ics",
            FileName = $"EasyDay-{DateTime.Now:yyyyMMdd}.ics"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IcsExporter.ExportToFile(dialog.FileName, _store.GetAll());
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            StatusText.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusText.Text = "导出失败：" + ex.Message;
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", EasyDayDbContext.DefaultDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FileLog.Error("打开数据目录失败", ex);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "确定要完全退出 EasyDay 吗？退出后将不再提醒。",
            "退出 EasyDay",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        DialogResult = false;
        Application.Current.Shutdown();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
