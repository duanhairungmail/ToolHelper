using System.IO;
using System.Diagnostics;

namespace ToolHelper.Services;

/// <summary>管理本地 Node-RED 进程及其输出。</summary>
public sealed class NodeRedProcessManager : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;

    public bool Start(string nodeExe, string redJs, string userDir, int port)
    {
        Stop();
        if (!File.Exists(nodeExe) || !File.Exists(redJs))
            return false;

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
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) OutputReceived?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) OutputReceived?.Invoke(e.Data);
        };
        process.Exited += (_, _) =>
        {
            try { ProcessExited?.Invoke(process.ExitCode); } catch { }
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            return true;
        }
        catch
        {
            process.Dispose();
            return false;
        }
    }

    public void Stop()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch { }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose() => Stop();
}
