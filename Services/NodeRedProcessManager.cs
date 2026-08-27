using System.Diagnostics;
using System.IO;

namespace ToolHelper.Services;

/// <summary>管理本地 Node-RED 进程及其输出。</summary>
public sealed class NodeRedProcessManager : IDisposable
{
    private readonly object _sync = new();
    private Process? _process;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                try { return _process is { HasExited: false }; }
                catch { return false; }
            }
        }
    }

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;

    public bool Start(string nodeExe, string redJs, string userDir, int port)
    {
        Stop();
        if (_disposed || !File.Exists(nodeExe) || !File.Exists(redJs)) return false;

        Directory.CreateDirectory(userDir);
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExe,
            WorkingDirectory = Path.GetDirectoryName(redJs) ?? AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(redJs);
        startInfo.ArgumentList.Add("--userDir");
        startInfo.ArgumentList.Add(userDir);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnError;
        process.Exited += OnExited;

        try
        {
            if (!process.Start()) { process.Dispose(); return false; }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_sync) _process = process;
            return true;
        }
        catch
        {
            process.Dispose();
            return false;
        }
    }

    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data)) OutputReceived?.Invoke(e.Data);
    }

    private void OnError(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data)) OutputReceived?.Invoke(e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;
        var code = -1;
        try { code = process.ExitCode; } catch { }
        try { ProcessExited?.Invoke(code); } catch { }
    }

    /// <summary>快速发出终止，不在 UI 线程等待进程退出，避免停止按钮卡顿和事件竞态。</summary>
    public void Stop()
    {
        Process? process;
        lock (_sync) process = Interlocked.Exchange(ref _process, null);
        if (process == null) return;

        try
        {
            process.OutputDataReceived -= OnOutput;
            process.ErrorDataReceived -= OnError;
            process.Exited -= OnExited;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            try { process.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}