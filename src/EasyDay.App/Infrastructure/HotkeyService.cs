using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EasyDay.App.Infrastructure;

/// <summary>全局热键。注册失败不抛异常，只在日志里记一笔。</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("EasyDay.HotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool Register(uint modifiers, uint virtualKey, Action action, string name)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            FileLog.Info($"热键注册失败：{name}");
            return false;
        }

        _handlers[id] = action;
        FileLog.Info($"热键注册成功：{name}");
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                handled = true;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    FileLog.Error("热键回调异常", ex);
                }
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
