using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace ToolHelper.Views.Security;

public class KylinOsScanView : SshToolBaseView
{
    private Button _scanBtn = new(), _repairBtn = new(), _verifyBtn = new();
    private VulnerabilityResult? _lastResult;

    protected override PackIconKind TitleIcon => PackIconKind.ShieldAlert;
    protected override string TitleText => "KylinOS 漏洞扫描";
    protected override string DescriptionText => "检测麒麟系统 kylin-offline-upgrade 组件的本地权限提升漏洞，支持扫描、自动修复（上传补丁+安装）和验证。";

    /// <summary>
    /// 补丁目录：从 BaseDirectory 向上逐级查找 plugins/Security patch/
    /// </summary>
    private static string PatchDir
    {
        get
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(dir, "plugins", "Security patch");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "Security patch");
        }
    }

    private static List<string> GetAvailablePatches()
    {
        try
        {
            if (!Directory.Exists(PatchDir)) return new List<string>();
            return Directory.GetFiles(PatchDir, "*.deb")
                           .Select(Path.GetFileName)
                           .Where(f => f != null)
                           .Cast<string>()
                           .OrderByDescending(f =>
                           {
                               var v = ExtractVersionFromFileName(f);
                               return v != null ? DpkgVersion.Parse(v) : new DpkgVersion();
                           })
                           .ToList();
        }
        catch { return new List<string>(); }
    }

    private static string? GetBestPatch() => GetAvailablePatches().FirstOrDefault();

    private static string? ExtractVersionFromFileName(string fileName)
    {
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_');
        if (parts.Length >= 2) return parts[1];
        return null;
    }

    // ================== UI 构建 ==================

    protected override void BuildToolContent(DockPanel root, StackPanel topPanel)
    {
        // 漏洞信息区
        var infoBox = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        infoBox.Children.Add(MakeInfoRow("漏洞名称:", "麒麟离线升级工具本地权限提升漏洞"));
        infoBox.Children.Add(MakeInfoRow("影响组件:", "kylin-offline-upgrade"));
        infoBox.Children.Add(MakeInfoRow("风险等级:", "高危（本地提权 LPE）"));
        infoBox.Children.Add(MakeInfoRow("修复方式:", "安装官方安全补丁 (.deb)"));
        infoBox.Children.Add(MakeInfoRow("补丁目录:", $"plugins{Path.DirectorySeparatorChar}Security patch{Path.DirectorySeparatorChar}"));
        topPanel.Children.Add(infoBox);

        // 操作按钮行
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _scanBtn = MakeButton("扫描", DoScan, true, PackIconKind.SearchWeb);
        _scanBtn.IsEnabled = false;
        actionRow.Children.Add(_scanBtn);
        _repairBtn = MakeButton("修复", DoRepair, false, PackIconKind.Wrench);
        _repairBtn.IsEnabled = false;
        actionRow.Children.Add(_repairBtn);
        _verifyBtn = MakeButton("验证", DoVerify, false, PackIconKind.CheckCircle);
        _verifyBtn.IsEnabled = false;
        actionRow.Children.Add(_verifyBtn);
        actionRow.Children.Add(MakeButton("复制结果", CopyResult, false, PackIconKind.ContentCopy));
        StatusText.VerticalAlignment = VerticalAlignment.Center;
        StatusText.Margin = new Thickness(16, 0, 0, 0);
        StatusText.FontSize = 13;
        actionRow.Children.Add(StatusText);
        topPanel.Children.Add(actionRow);

        AppendResult("点击 [连接SSH] 连接到麒麟系统，然后点击 [扫描] 开始检测漏洞。");
    }

    protected override void OnConnected() { _scanBtn.IsEnabled = true; }

    protected override void OnDisconnected()
    {
        _scanBtn.IsEnabled = false;
        _repairBtn.IsEnabled = false;
        _verifyBtn.IsEnabled = false;
    }

    // ================== 扫描 ==================

    private async void DoScan()
    {
        _scanBtn.IsEnabled = false;
        SetStatus("正在扫描...", true);
        AppendResult("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始扫描...");

        try
        {
            var ssh = Ssh;
            if (ssh == null || !ssh.IsConnected) throw new InvalidOperationException("SSH 未连接");
            DisconnectBtn.IsEnabled = false;
            var result = await Task.Run(() => ScanInternal(ssh));
            _lastResult = result;
            DisplayScanResult(result);
            _repairBtn.IsEnabled = result.IsVulnerable;
            _verifyBtn.IsEnabled = result.Status == VulnerabilityStatus.Vulnerable || result.Status == VulnerabilityStatus.Fixed;
            SetStatus(result.IsVulnerable ? "发现漏洞" : "未发现漏洞", !result.IsVulnerable);
        }
        catch (Exception ex)
        {
            AppendResult($"扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
        }
        finally { _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private VulnerabilityResult ScanInternal(SshClient ssh)
    {
        var result = new VulnerabilityResult { ScanTime = DateTime.Now };
        var osRelease = RunCommand(ssh, "cat /etc/os-release");
        var kernel = RunCommand(ssh, "uname -r").Trim();
        var arch = RunCommand(ssh, "arch").Trim();

        result.OsName = ExtractField(osRelease, "NAME");
        result.OsVersion = ExtractField(osRelease, "VERSION");
        result.OsSp = ExtractField(osRelease, "VERSION_US");
        result.KernelVersion = kernel;
        result.Architecture = arch;

        var dpkgOutput = RunCommand(ssh, "dpkg -l kylin-offline-upgrade 2>/dev/null");
        result.CurrentVersion = ParseDpkgVersion(dpkgOutput);

        if (result.CurrentVersion == null)
        {
            result.IsVulnerable = false;
            result.Status = dpkgOutput.Contains("no packages found") || dpkgOutput.Contains("未安装")
                ? VulnerabilityStatus.NotInstalled : VulnerabilityStatus.ScanFailed;
            return result;
        }

        var bestPatch = GetBestPatch();
        result.PatchFile = bestPatch;
        result.FixedVersion = bestPatch != null ? (ExtractVersionFromFileName(bestPatch) ?? "0") : "0";

        var current = DpkgVersion.Parse(result.CurrentVersion);
        var fixed_ = DpkgVersion.Parse(result.FixedVersion);
        result.IsVulnerable = current.CompareTo(fixed_) < 0;
        result.Status = result.IsVulnerable ? VulnerabilityStatus.Vulnerable : VulnerabilityStatus.Fixed;
        return result;
    }

    private void DisplayScanResult(VulnerabilityResult r)
    {
        AppendResult($"扫描时间: {r.ScanTime:yyyy-MM-dd HH:mm:ss}");
        AppendResult($"目标系统: {r.OsName} {r.OsVersion} ({r.OsSp})");
        AppendResult($"内核版本: {r.KernelVersion}  架构: {r.Architecture}");
        AppendResult("");

        if (r.Status == VulnerabilityStatus.NotInstalled)
        {
            AppendResult("[结果] 未安装 kylin-offline-upgrade，不受此漏洞影响。");
        }
        else if (r.Status == VulnerabilityStatus.ScanFailed)
        {
            AppendResult("[结果] 扫描失败，无法获取组件版本信息。");
        }
        else if (r.IsVulnerable)
        {
            AppendResult("[结果] 发现漏洞!");
            AppendResult($"  组件: kylin-offline-upgrade");
            AppendResult($"  当前版本: {r.CurrentVersion}  <-- 存在漏洞");
            AppendResult($"  修复版本: {r.FixedVersion}  <-- 需升级到此版本");
            AppendResult($"  补丁文件: {r.PatchFile}");
            AppendResult("");
            AppendResult("漏洞描述:");
            AppendResult("  kylin-offline-upgrade 核心组件存在本地权限提升漏洞，");
            AppendResult("  普通用户可借此获得 root 权限，完全控制系统。");
            AppendResult("");
            AppendResult("修复方法:");
            AppendResult("  点击 [修复] 按钮，将自动上传并安装安全补丁。");
        }
        else
        {
            AppendResult("[结果] 未发现漏洞（已修复）");
            AppendResult($"  组件: kylin-offline-upgrade");
            AppendResult($"  当前版本: {r.CurrentVersion}");
            AppendResult($"  修复版本: {r.FixedVersion}");
            AppendResult("  状态: 已包含安全补丁，无需修复。");
        }
        AppendResult("");
    }

    // ================== 修复 ==================

    private async void DoRepair()
    {
        if (_lastResult == null || !_lastResult.IsVulnerable) { SetStatus("无需修复", false); return; }
        var ssh = Ssh;
        var sftp = Sftp;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        if (sftp == null || !sftp.IsConnected) { SetStatus("SFTP 未连接", false); return; }

        var patchFile = _lastResult.PatchFile;
        if (string.IsNullOrEmpty(patchFile))
        {
            SetStatus("补丁目录中没有 .deb 文件", false);
            AppendResult($"\n[修复失败] 补丁目录中没有找到 .deb 文件");
            AppendResult($"  目录: {PatchDir}");
            AppendResult("请将官方安全补丁 (.deb) 放入 plugins/Security patch/ 目录后重试。");
            return;
        }

        var localPath = Path.Combine(PatchDir, patchFile);
        if (!File.Exists(localPath))
        {
            var available = GetAvailablePatches();
            SetStatus($"补丁文件缺失: {patchFile}", false);
            AppendResult($"\n[修复失败] 补丁文件不存在: {localPath}");
            AppendResult($"  目录中可用文件: {(available.Count > 0 ? string.Join(", ", available) : "无")}");
            AppendResult("请将官方安全补丁 (.deb) 放入 plugins/Security patch/ 目录后重试。");
            return;
        }

        _repairBtn.IsEnabled = false;
        _scanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在修复...", true);
        AppendResult("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始修复...");

        try
        {
            var username = UserBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) username = "root";
            var password = PassBox.Password;
            await Task.Run(() => RepairInternal(ssh, sftp, patchFile, localPath, username, password));
            AppendResult("\n[修复完成] 补丁安装成功！");
            AppendResult("请点击 [验证] 确认修复是否生效。");
            _repairBtn.IsEnabled = false;
            _verifyBtn.IsEnabled = true;
            SetStatus("修复完成", true);
            MessageBox.Show($"漏洞已修复！\n\nkylin-offline-upgrade 已升级到安全版本。\n请点击 [验证] 确认修复效果。",
                "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendResult($"\n[修复失败] {ex.Message}");
            SetStatus($"修复失败: {ex.Message}", false);
            _repairBtn.IsEnabled = true;
        }
        finally { _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private void RepairInternal(SshClient ssh, SftpClient sftp, string patchFile, string localPath, string username, string password)
    {
        var remotePath = $"/home/{username}/{patchFile}";

        Dispatcher.Invoke(() => AppendResult($"[步骤1/2] 上传补丁文件..."));
        Dispatcher.Invoke(() => AppendResult($"  本地: {localPath}"));
        Dispatcher.Invoke(() => AppendResult($"  远程: {remotePath}"));

        if (sftp == null || !sftp.IsConnected) throw new InvalidOperationException("SFTP 未连接");
        using (var stream = File.OpenRead(localPath))
        {
            sftp.UploadFile(stream, remotePath, true);
        }
        Dispatcher.Invoke(() => AppendResult("  上传完成。\n"));

        Dispatcher.Invoke(() => AppendResult($"[步骤2/2] 安装补丁..."));
        string installCmd;
        if (username == "root")
        {
            installCmd = $"cd /home/{username} && dpkg -i {patchFile}";
        }
        else
        {
            var escapedPassword = password.Replace("'", "'\\''");
            installCmd = $"cd /home/{username} && echo '{escapedPassword}' | sudo -S dpkg -i {patchFile}";
        }

        Dispatcher.Invoke(() => AppendResult($"  $ cd /home/{username} && sudo dpkg -i {patchFile}"));
        var output = RunCommand(ssh, installCmd);
        Dispatcher.Invoke(() => AppendResult(output));

        if (!output.Contains("Setting up") && !output.Contains("正在设置") && !output.Contains("Unpacking"))
            throw new Exception("安装可能未成功，输出中未找到安装确认关键字。\n请确认用户具有 sudo 权限且密码正确。");

        Dispatcher.Invoke(() => AppendResult("  安装完成。\n"));
        try { RunCommand(ssh, $"rm -f {remotePath}"); } catch { }
    }

    // ================== 验证 ==================

    private async void DoVerify()
    {
        _verifyBtn.IsEnabled = false;
        SetStatus("正在验证...", true);
        AppendResult("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始验证修复结果...");

        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }
        DisconnectBtn.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => ScanInternal(ssh));
            _lastResult = result;

            if (result.Status == VulnerabilityStatus.Fixed || result.Status == VulnerabilityStatus.NotInstalled)
            {
                AppendResult("[验证通过] 漏洞已成功修复！");
                AppendResult($"  当前版本: {result.CurrentVersion ?? "未安装"}");
                AppendResult($"  修复版本: {result.FixedVersion}");
                _repairBtn.IsEnabled = false;
                SetStatus("验证通过", true);
            }
            else if (result.IsVulnerable)
            {
                AppendResult("[验证失败] 修复未生效，版本仍为: " + result.CurrentVersion);
                _repairBtn.IsEnabled = true;
                SetStatus("验证失败", false);
            }
            else
            {
                AppendResult("[验证异常] 无法确定修复状态");
                SetStatus("验证异常", false);
            }
        }
        catch (Exception ex)
        {
            AppendResult($"验证失败: {ex.Message}");
            SetStatus($"验证失败: {ex.Message}", false);
        }
        finally { _verifyBtn.IsEnabled = true; _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 辅助 ==================

    private static string? ParseDpkgVersion(string dpkgOutput)
    {
        if (string.IsNullOrWhiteSpace(dpkgOutput)) return null;
        if (dpkgOutput.Contains("no packages found")) return null;
        foreach (var line in dpkgOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ii ") || trimmed.StartsWith("hi "))
            {
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[1] == "kylin-offline-upgrade")
                    return parts[2];
            }
        }
        return null;
    }
}

// ================== DPKG 版本比较 ==================
public class DpkgVersion : IComparable<DpkgVersion>
{
    public int Epoch { get; set; }
    public string UpstreamVersion { get; set; } = "";
    public string DebianRevision { get; set; } = "0";

    public static DpkgVersion Parse(string version)
    {
        var v = new DpkgVersion();
        int colonIdx = version.IndexOf(':');
        if (colonIdx >= 0)
        {
            if (int.TryParse(version.Substring(0, colonIdx), out int ep)) v.Epoch = ep;
            version = version.Substring(colonIdx + 1);
        }
        int dashIdx = version.LastIndexOf('-');
        if (dashIdx >= 0)
        {
            v.DebianRevision = version.Substring(dashIdx + 1);
            v.UpstreamVersion = version.Substring(0, dashIdx);
        }
        else
        {
            v.UpstreamVersion = version;
        }
        return v;
    }

    public int CompareTo(DpkgVersion? other)
    {
        if (other == null) return 1;
        if (Epoch != other.Epoch) return Epoch.CompareTo(other.Epoch);
        int cmp = CompareSegments(UpstreamVersion, other.UpstreamVersion);
        if (cmp != 0) return cmp;
        return CompareSegments(DebianRevision, other.DebianRevision);
    }

    private static int CompareSegments(string a, string b)
    {
        var segsA = SplitSegments(a);
        var segsB = SplitSegments(b);
        int maxLen = Math.Max(segsA.Count, segsB.Count);
        for (int i = 0; i < maxLen; i++)
        {
            string sa = i < segsA.Count ? segsA[i] : "0";
            string sb = i < segsB.Count ? segsB[i] : "0";
            bool na = int.TryParse(sa, out int va);
            bool nb = int.TryParse(sb, out int vb);
            if (na && nb) { if (va != vb) return va.CompareTo(vb); }
            else { int sc = string.Compare(sa, sb, StringComparison.Ordinal); if (sc != 0) return sc; }
        }
        return 0;
    }

    private static List<string> SplitSegments(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool? lastIsDigit = null;
        foreach (char c in s)
        {
            bool isDigit = char.IsDigit(c);
            if (lastIsDigit.HasValue && isDigit != lastIsDigit.Value)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
            lastIsDigit = isDigit;
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}

// ================== 数据模型 ==================
public class VulnerabilityResult
{
    public bool IsVulnerable { get; set; }
    public VulnerabilityStatus Status { get; set; }
    public string? CurrentVersion { get; set; }
    public string? FixedVersion { get; set; }
    public string? PatchFile { get; set; }
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string OsSp { get; set; } = "";
    public string KernelVersion { get; set; } = "";
    public string Architecture { get; set; } = "";
    public DateTime ScanTime { get; set; }
}

public enum VulnerabilityStatus { Unknown, Vulnerable, Fixed, NotInstalled, ScanFailed }
