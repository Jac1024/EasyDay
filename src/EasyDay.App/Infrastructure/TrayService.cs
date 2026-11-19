using System.Drawing;
using System.Windows.Forms;

namespace EasyDay.App.Infrastructure;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayService(Action show, Action newEvent, Action quickAdd, Action assistant, Action settings, Action exit)
    {
        _toggleItem = new ToolStripMenuItem("显示挂件", null, (_, _) => show());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("新建日程", null, (_, _) => newEvent()));
        menu.Items.Add(new ToolStripMenuItem("速记（Ctrl+Alt+Space）", null, (_, _) => quickAdd()));
        menu.Items.Add(new ToolStripMenuItem("问问助手（Ctrl+Alt+Q）", null, (_, _) => assistant()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("设置", null, (_, _) => settings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出 EasyDay", null, (_, _) => exit()));

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Text = "EasyDay",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void UpdateVisible(bool widgetVisible)
    {
        _toggleItem.Text = widgetVisible ? "隐藏挂件" : "显示挂件";
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = ToolTipIcon.Info;
            _icon.ShowBalloonTip(5000);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
