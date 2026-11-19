using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Windows;

namespace EasyDay.App.Infrastructure;

/// <summary>单实例：第二个实例通过命名管道让第一个实例显示挂件，然后退出。</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "EasyDay.SingleInstance.Mutex";
    private const string PipeName = "EasyDay.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public SingleInstance()
    {
        // 统一加 Global\ 前缀，避免不同会话重复
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirst = createdNew;
    }

    public bool IsFirst { get; }

    public event Action? ShowRequested;

    public void StartServer()
    {
        if (!IsFirst)
        {
            return;
        }

        var thread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = "EasyDay.SingleInstancePipe"
        };
        thread.Start();
    }

    public static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(800);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("show");
        }
        catch
        {
            // 对方可能正在退出，忽略
        }
    }

    private void ServerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var command = reader.ReadLine();
                if (string.Equals(command, "show", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() => ShowRequested?.Invoke());
                }
            }
            catch
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}
