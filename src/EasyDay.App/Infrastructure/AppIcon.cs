using System.Drawing;
using System.IO;
using System.Windows;

namespace EasyDay.App.Infrastructure;

/// <summary>
/// 应用图标。exe 图标由 csproj 的 ApplicationIcon 提供；
/// 运行时窗口和托盘从这里读同一份 Assets\app.ico，避免出现系统默认图标。
/// </summary>
public static class AppIcon
{
    private static Icon? _icon;

    public static Icon Value => _icon ??= Load();

    private static Icon Load()
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("加载应用图标失败", ex);
        }

        return SystemIcons.Application;
    }
}
