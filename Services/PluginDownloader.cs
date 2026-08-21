using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ToolHelper.Services;

/// <summary>
/// 从 GitHub Releases 下载并解压外挂插件（dbx / electerm 等）
/// </summary>
public static class PluginDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    static PluginDownloader()
    {
        // GitHub API 强制要求 User-Agent
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ToolHelper");
    }

    /// <summary>
    /// 下载 GitHub latest release 中匹配的 asset 并解压到目标目录。
    /// </summary>
    /// <param name="owner">仓库所有者</param>
    /// <param name="repo">仓库名</param>
    /// <param name="assetMatcher">asset 名称匹配函数（选中 Windows 便携版）</param>
    /// <param name="targetDir">解压目标目录（如 plugins/electerm）</param>
    /// <param name="progress">进度回调</param>
    /// <returns>release 版本标签（如 v5.2.0）</returns>
    public static async Task<string> DownloadAsync(
        string owner,
        string repo,
        Func<string, bool> assetMatcher,
        string targetDir,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"正在获取 {repo} 最新版本信息...");
        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var json = await Http.GetStringAsync(apiUrl, ct);
        var obj = JObject.Parse(json);
        var tagName = obj["tag_name"]?.ToString() ?? "";

        var assets = obj["assets"] as JArray;
        var asset = assets?.FirstOrDefault(a => assetMatcher(a["name"]?.ToString() ?? ""));
        if (asset == null)
            throw new Exception($"未在 {repo} 的 latest release 中找到匹配的 Windows 便携版");

        var fileName = asset["name"]?.ToString() ?? "plugin.pkg";
        var downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
        var size = asset["size"]?.Value<long>() ?? 0;
        progress?.Report($"找到 {fileName}（{size / 1024.0 / 1024.0:F1} MB），开始下载...");

        // 下载到系统临时目录
        var tmp = Path.Combine(Path.GetTempPath(), fileName);
        using (var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var inStream = await resp.Content.ReadAsStreamAsync(ct);
            await using var outStream = File.Create(tmp);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            var lastMb = -1L;
            while ((read = await inStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                var mb = total / (1024 * 1024);
                if (mb != lastMb)
                {
                    lastMb = mb;
                    progress?.Report($"下载中 {mb} MB ...");
                }
            }
        }

        progress?.Report("下载完成，正在解压...");
        Directory.CreateDirectory(targetDir);

        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            ExtractTarGz(tmp, targetDir);
        else if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ZipFile.ExtractToDirectory(tmp, targetDir, true);
        else
            throw new Exception($"不支持的压缩格式：{fileName}");

        try { File.Delete(tmp); } catch { }
        // 写入版本标记文件，供「插件更新」功能识别本地版本
        try { File.WriteAllText(Path.Combine(targetDir, "version.txt"), tagName); } catch { }
        progress?.Report("下载解压完成");
        return tagName;
    }

    /// <summary>获取仓库 latest release 的版本标签（如 v5.2.0），供版本检测用</summary>
    public static async Task<string> GetLatestVersionAsync(string owner, string repo, CancellationToken ct = default)
    {
        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var json = await Http.GetStringAsync(apiUrl, ct);
        return JObject.Parse(json)["tag_name"]?.ToString() ?? "";
    }

    /// <summary>读取插件目录内的版本标记（version.txt），不存在返回 null</summary>
    public static string? ReadVersionMarker(string pluginDir)
    {
        var marker = Path.Combine(pluginDir, "version.txt");
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    /// <summary>解析版本标签（剥离 v/V 前缀，取前 3 段整数，如 "v5.2.0" → 5,2,0；非法返回 null）</summary>
    public static int[]? ParseVersionTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        var parts = t.Split('.');
        var nums = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { nums[i] = 0; continue; }
            var numPart = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
            if (numPart.Length == 0) return null;
            nums[i] = int.Parse(numPart);
        }
        return nums;
    }

    /// <summary>a 是否比 b 新（逐段比较，避免字符串比较陷阱：5.10.0 &gt; 5.9.0）</summary>
    public static bool IsNewer(string a, string b)
    {
        var va = ParseVersionTag(a);
        var vb = ParseVersionTag(b);
        if (va == null) return false;
        if (vb == null) return true;
        for (int i = 0; i < 3; i++)
        {
            if (va[i] != vb[i]) return va[i] > vb[i];
        }
        return false;
    }

    /// <summary>解压 .tar.gz（先 GZip 解压，再用 TarReader）</summary>
    private static void ExtractTarGz(string tarGzPath, string targetDir)
    {
        // 全路径归一化后做前缀校验：拦截绝对路径与 "../" 相对穿越
        // （字符串级 StartsWith 会被 "out\..\evil.txt" 这类拼接绕过，必须 GetFullPath 后再比较）
        var fullTarget = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        using var gz = new GZipStream(File.OpenRead(tarGzPath), CompressionMode.Decompress);
        using var tar = new System.Formats.Tar.TarReader(gz);
        System.Formats.Tar.TarEntry? entry;
        while ((entry = tar.GetNextEntry()) != null)
        {
            // 归一化路径，防路径穿越
            var name = entry.Name.Replace('\\', '/').TrimStart('/');
            var dest = Path.GetFullPath(Path.Combine(targetDir, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"非法路径：{entry.Name}");

            if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory)
            {
                Directory.CreateDirectory(dest);
            }
            else if (entry.EntryType is System.Formats.Tar.TarEntryType.RegularFile
                     or System.Formats.Tar.TarEntryType.V7RegularFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
            }
        }
    }
}
