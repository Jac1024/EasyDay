using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyDay.App.Infrastructure;
using EasyDay.App.Services;
using EasyDay.Core.Ai;
using EasyDay.Core.Data;

namespace EasyDay.App;

public partial class App : Application
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private SingleInstance? _singleInstance;
    private AppShell? _shell;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 高缩放下必须显式开启 PerMonitorV2，否则窗口会被系统整体拉伸、坐标错位
        try
        {
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch (Exception ex)
        {
            FileLog.Error("设置 DPI 感知失败", ex);
        }

        FileLog.Info("启动 EasyDay");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileLog.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);

        // 所有窗口（含对话框）统一用应用图标，不逐个窗口写 Icon
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyWindowIcon));

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirst)
        {
            SingleInstance.NotifyExistingInstance();
            Shutdown();
            return;
        }

        _singleInstance.ShowRequested += () => _shell?.ShowWidget();
        _singleInstance.StartServer();

        try
        {
            var store = new EventStore();
            var init = store.Initialize();
            FileLog.Info(
                $"数据库就绪 v{init.Version} existed={init.Existed} recreated={init.Recreated}" +
                (init.BackupPath is null ? string.Empty : $" backup={init.BackupPath}"));

            if (init.Recreated)
            {
                MessageBox.Show(
                    "本地数据库无法读取，已备份并重建了一个空库。\n\n备份：" + init.BackupPath,
                    "EasyDay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var settings = new SettingsService();
            settings.Load();

            var aiClient = new ChatCompletionsClient(() => settings.Current);
            var parser = new AiEventParser(aiClient, () => settings.Current);

            _shell = new AppShell(store, settings, parser);
            _shell.Start();
        }
        catch (Exception ex)
        {
            FileLog.Error("启动失败", ex);
            MessageBox.Show("EasyDay 启动失败：" + ex.Message, "EasyDay", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void ApplyWindowIcon(object sender, RoutedEventArgs e)
    {
        if (sender is not Window { Icon: null } window)
        {
            return;
        }

        try
        {
            window.Icon = Imaging.CreateBitmapSourceFromHIcon(
                AppIcon.Value.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex)
        {
            FileLog.Error("设置窗口图标失败", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Error("UI 线程未处理异常", e.Exception);
        MessageBox.Show(
            "出现了未处理的错误，已记录到日志。\n\n" + e.Exception.Message,
            "EasyDay",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        _singleInstance?.Dispose();
        FileLog.Info("已退出");
        base.OnExit(e);
    }
}
