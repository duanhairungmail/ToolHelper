using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ToolHelper.Services;

/// <summary>
/// 通过 Java HTTP 代理连接 openGauss 数据库（解决 Npgsql 不支持 SHA256 认证的问题）
/// </summary>
public class OpenGaussProxyClient : IDisposable
{
    private Process? _javaProcess;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private const int ProxyPort = 18080;
    private string _host = "", _port = "", _user = "", _pass = "";
    private string _currentDb = "firesys_station";

    public bool IsConnected => _javaProcess != null && !_javaProcess.HasExited;
    public string CurrentDatabase => _currentDb;

    public async Task ConnectAsync(string host, string port, string user, string pass, string? database = null)
    {
        _host = host; _port = port; _user = user; _pass = pass;
        _currentDb = database ?? "firesys_station";

        // 如果代理已在运行，先停止
        StopProxy();
        // 等待端口释放
        await Task.Delay(500);
        // 强杀可能残留的代理进程
        KillProxyByPort(ProxyPort);

        var proxyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openGaussProxy");
        var jarPath = Path.Combine(proxyDir, "lib", "opengauss-jdbc-5.0.0.jar");
        var classPath = ".;" + jarPath;

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-cp \"{classPath}\" OpenGaussProxy {host} {port} {user} \"{pass}\"",
            WorkingDirectory = proxyDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _javaProcess = Process.Start(psi);
        if (_javaProcess == null) throw new Exception("启动 Java 代理失败");

        // 等待 READY 信号
        var ready = false;
        var timeout = DateTime.Now.AddSeconds(10);
        while (DateTime.Now < timeout)
        {
            if (_javaProcess.HasExited)
                throw new Exception($"Java 代理启动失败: {_javaProcess.StandardError.ReadToEnd()}");
            if (_javaProcess.StandardOutput.Peek() >= 0)
            {
                var line = await _javaProcess.StandardOutput.ReadLineAsync();
                if (line == "READY") { ready = true; break; }
            }
            else await Task.Delay(100);
        }
        if (!ready) throw new Exception("Java 代理启动超时");

        // 验证连接
        await ExecuteQueryAsync("SELECT 1", _currentDb);
    }

    public void SwitchDatabase(string database)
    {
        _currentDb = database;
    }

    public async Task<DataTable> ExecuteQueryAsync(string sql, string? database = null)
    {
        var db = database ?? _currentDb;
        var body = $$"""{"database":"{{EscapeJson(db)}}","sql":"{{EscapeJson(sql)}}"}""";
        var resp = await _http.PostAsync($"http://127.0.0.1:{ProxyPort}/execute",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var json = await resp.Content.ReadAsStringAsync();
        var obj = JObject.Parse(json);

        if (obj["status"]?.ToString() == "error")
            throw new Exception(obj["error"]?.ToString() ?? "未知错误");

        var dt = new DataTable();
        var cols = obj["columns"] as JArray;
        var rows = obj["rows"] as JArray;

        if (cols != null)
        {
            foreach (var col in cols)
                dt.Columns.Add(col.ToString(), typeof(string));
        }

        if (rows != null)
        {
            foreach (var row in rows)
            {
                var dr = dt.NewRow();
                if (cols != null)
                {
                    foreach (var col in cols)
                    {
                        var val = row[col.ToString()];
                        dr[col.ToString()] = val?.ToString() ?? "";
                    }
                }
                dt.Rows.Add(dr);
            }
        }
        return dt;
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, string? database = null)
    {
        var db = database ?? _currentDb;
        var body = $$"""{"database":"{{EscapeJson(db)}}","sql":"{{EscapeJson(sql)}}"}""";
        var resp = await _http.PostAsync($"http://127.0.0.1:{ProxyPort}/execute",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var json = await resp.Content.ReadAsStringAsync();
        var obj = JObject.Parse(json);

        if (obj["status"]?.ToString() == "error")
            throw new Exception(obj["error"]?.ToString() ?? "未知错误");

        return obj["affected"]?.Value<int>() ?? 0;
    }

    public async Task<string> GetVersionAsync()
    {
        var dt = await ExecuteQueryAsync("SELECT version()");
        return dt.Rows.Count > 0 ? dt.Rows[0][0]?.ToString() ?? "" : "";
    }

    public async Task<List<string>> GetDatabasesAsync()
    {
        var dt = await ExecuteQueryAsync("SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname");
        var list = new List<string>();
        foreach (DataRow row in dt.Rows)
            list.Add(row[0]?.ToString() ?? "");
        return list;
    }

    public async Task<List<string>> GetTablesAsync()
    {
        var dt = await ExecuteQueryAsync("SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename");
        var list = new List<string>();
        foreach (DataRow row in dt.Rows)
            list.Add(row[0]?.ToString() ?? "");
        return list;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public void StopProxy()
    {
        if (_javaProcess != null && !_javaProcess.HasExited)
        {
            try { _javaProcess.Kill(); _javaProcess.WaitForExit(3000); } catch { }
        }
        _javaProcess = null;
    }

    private static void KillProxyByPort(int port)
    {
        try
        {
            var connections = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners();
            foreach (var ep in connections)
            {
                if (ep.Port == port)
                {
                    // 查找并杀死占用该端口的 java 进程
                    foreach (var p in Process.GetProcessesByName("java"))
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                    break;
                }
            }
        }
        catch { /* 忽略端口检查失败 */ }
    }

    public void Dispose() => StopProxy();
}
