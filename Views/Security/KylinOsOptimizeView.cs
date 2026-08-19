using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Renci.SshNet;
using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;

namespace ToolHelper.Views.Security;

// ================== 数据模型 ==================
public class OptimizationItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string RiskLevel { get; set; } = "低";
    public string Category { get; set; } = "";
    public string ScanCmd { get; set; } = "";
    public string OptimizeCmd { get; set; } = "";
    public string VerifyCmd { get; set; } = "";
    public string RestoreCmd { get; set; } = "";
    public bool IsSelected { get; set; } = true;
    public string Status { get; set; } = "待扫描";
    public bool IsApplicable { get; set; } = false;
    public bool IsOptimized { get; set; } = false;
    public bool IsMasked { get; set; } = false;
    public string RiskNote { get; set; } = "";
    public string ScanDetail { get; set; } = "";
}

public class KylinOsOptimizeView : SshToolBaseView
{
    private Button _scanBtn = new(), _optimizeBtn = new(), _verifyBtn = new(), _restoreBtn = new();
    private ObservableCollection<OptimizationItem> _items = new();
    private DataGrid _dataGrid = new();
    private TextBlock _infoText = new();
    private TextBlock _systemInfoText = new();

    protected override PackIconKind TitleIcon => PackIconKind.ShieldAlert;
    protected override string TitleText => "KylinOS 系统优化";
    protected override string DescriptionText => "通过 SSH 远程优化麒麟系统，扫描并精简不必要的后台服务、进程和定时任务，提升系统性能与安全性";

    protected override void BuildToolContent(DockPanel root, StackPanel topPanel)
    {
        _items = new ObservableCollection<OptimizationItem>(GetOptimizationItems());

        // 信息区
        var infoBox = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        _infoText.Text = $"优化项数量: {_items.Count} 项  |  点击 [扫描] 开始检测目标系统";
        _infoText.FontSize = 12;
        _systemInfoText.Text = "目标系统: 未连接";
        _systemInfoText.FontSize = 12;
        _systemInfoText.FontWeight = FontWeights.SemiBold;
        _systemInfoText.Margin = new Thickness(0, 0, 0, 2);
        infoBox.Children.Add(_systemInfoText);
        infoBox.Children.Add(_infoText);
        infoBox.Children.Add(new TextBlock { Text = "风险提示: mask 为不可逆级停用（可用 [恢复选中] 还原），中风险项请根据业务需求谨慎选择", FontSize = 11, Foreground = Brushes.Orange, Margin = new Thickness(0, 2, 0, 0) });
        topPanel.Children.Add(infoBox);

        // 操作按钮行
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _scanBtn = MakeButton("扫描", DoScan, true, PackIconKind.SearchWeb);
        _scanBtn.IsEnabled = false;
        actionRow.Children.Add(_scanBtn);
        _optimizeBtn = MakeButton("优化选中", DoOptimize, false, PackIconKind.Flash);
        _optimizeBtn.IsEnabled = false;
        actionRow.Children.Add(_optimizeBtn);
        _verifyBtn = MakeButton("验证", DoVerify, false, PackIconKind.CheckCircle);
        _verifyBtn.IsEnabled = false;
        actionRow.Children.Add(_verifyBtn);
        _restoreBtn = MakeButton("恢复选中", DoRestore, false, PackIconKind.Undo);
        _restoreBtn.IsEnabled = false;
        actionRow.Children.Add(_restoreBtn);
        actionRow.Children.Add(MakeButton("复制结果", CopyResult, false, PackIconKind.ContentCopy));
        StatusText.VerticalAlignment = VerticalAlignment.Center;
        StatusText.Margin = new Thickness(16, 0, 0, 0);
        StatusText.FontSize = 13;
        StatusText.Text = "未扫描";
        StatusText.Foreground = Brushes.Gray;
        actionRow.Children.Add(StatusText);
        topPanel.Children.Add(actionRow);

        // DataGrid
        _dataGrid.ItemsSource = _items;
        _dataGrid.AutoGenerateColumns = false;
        _dataGrid.CanUserAddRows = false;
        _dataGrid.CanUserReorderColumns = false;
        _dataGrid.IsReadOnly = false;
        _dataGrid.SelectionMode = DataGridSelectionMode.Single;
        _dataGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _dataGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _dataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        _dataGrid.MaxHeight = 340;
        _dataGrid.MinHeight = 200;

        var colSelect = new DataGridCheckBoxColumn
        {
            Header = "选择",
            Binding = new System.Windows.Data.Binding("IsSelected") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            IsReadOnly = false
        };
        _dataGrid.Columns.Add(colSelect);
        _dataGrid.Columns.Add(new DataGridTextColumn { Header = "序号", Binding = new System.Windows.Data.Binding("Id"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        _dataGrid.Columns.Add(new DataGridTextColumn { Header = "优化项目名称", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });
        _dataGrid.Columns.Add(new DataGridTextColumn { Header = "风险", Binding = new System.Windows.Data.Binding("RiskLevel"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });

        // 状态列（彩色字体）
        var statusTemplate = new DataTemplate();
        var tbFactory = new FrameworkElementFactory(typeof(TextBlock));
        tbFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Status"));
        tbFactory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Status") { Converter = new StatusColorConverter() });
        tbFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        statusTemplate.VisualTree = tbFactory;
        _dataGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "状态",
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            IsReadOnly = true,
            CellTemplate = statusTemplate
        });
        _dataGrid.Columns.Add(new DataGridTextColumn { Header = "类别", Binding = new System.Windows.Data.Binding("Category"), Width = new DataGridLength(1, DataGridLengthUnitType.Auto), IsReadOnly = true });

        // 详情列（显示具体未优化原因，长文本省略+悬浮提示）
        var detailStyle = new Style(typeof(TextBlock));
        detailStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        detailStyle.Setters.Add(new Setter(System.Windows.Controls.ToolTipService.ToolTipProperty, new System.Windows.Data.Binding("ScanDetail")));
        _dataGrid.Columns.Add(new DataGridTextColumn { Header = "详情", Binding = new System.Windows.Data.Binding("ScanDetail"), Width = new DataGridLength(240), IsReadOnly = true, ElementStyle = detailStyle });

        topPanel.Children.Add(_dataGrid);

        // 选择按钮行
        var selectRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        selectRow.Children.Add(MakeButton("全选", SelectAll, false, PackIconKind.SelectAll));
        selectRow.Children.Add(MakeButton("全不选", SelectNone, false, PackIconKind.SelectOff));
        selectRow.Children.Add(MakeButton("反选", InvertSelection, false, PackIconKind.SwapHorizontal));
        selectRow.Children.Add(MakeButton("日志清理", ClearLog, false, PackIconKind.NotificationClearAll));
        topPanel.Children.Add(selectRow);

        AppendResult("点击 [连接SSH] 连接到麒麟系统，然后点击 [扫描] 开始检测待优化项。");
    }

    protected override void OnConnected() { _scanBtn.IsEnabled = true; _restoreBtn.IsEnabled = true; }

    protected override void OnDisconnected()
    {
        _scanBtn.IsEnabled = false;
        _optimizeBtn.IsEnabled = false;
        _verifyBtn.IsEnabled = false;
        _restoreBtn.IsEnabled = false;
    }

    // ================== 选择操作 ==================

    private void SelectAll() { foreach (var item in _items) item.IsSelected = true; RefreshGrid(); }
    private void SelectNone() { foreach (var item in _items) item.IsSelected = false; RefreshGrid(); }
    private void InvertSelection() { foreach (var item in _items) item.IsSelected = !item.IsSelected; RefreshGrid(); }
    private void ClearLog() { ResultBox.Clear(); }

    private void RefreshGrid()
    {
        _dataGrid.Items.Refresh();
    }

    // ================== 扫描 ==================

    private async void DoScan()
    {
        _scanBtn.IsEnabled = false;
        SetStatus("正在扫描...", true);
        AppendResult("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始扫描 {_items.Count} 项优化项...");

        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); _scanBtn.IsEnabled = true; return; }
        DisconnectBtn.IsEnabled = false;

        // 获取系统信息
        try
        {
            var osRelease = await Task.Run(() => RunCommand(ssh, "cat /etc/os-release"));
            var osName = ExtractField(osRelease, "PRETTY_NAME");
            Dispatcher.Invoke(() => _systemInfoText.Text = $"目标系统: {osName}");
        }
        catch { }

        try
        {
            var username = UserBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) username = "root";
            var password = PassBox.Password;

            await Task.Run(() =>
            {
                foreach (var item in _items)
                {
                    try
                    {
                        var output = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeScan(item, output);
                        item.Status = EvaluateScanResult(item, output);
                        item.IsApplicable = item.Status == "可优化";
                        if (item.Status == "不适用") item.IsSelected = false;
                        Dispatcher.Invoke(() => RefreshGrid());
                        Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name} → {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "扫描失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => RefreshGrid());
                        Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name} → 扫描失败: {ex.Message}"));
                    }
                }
            });

            UpdateSummary();
            var optimizable = _items.Count(i => i.Status == "可优化");
            var optimized = _items.Count(i => i.Status.StartsWith("已优化"));
            _optimizeBtn.IsEnabled = optimizable > 0 && _items.Any(i => i.IsSelected && i.Status == "可优化");
            _verifyBtn.IsEnabled = true;
            if (optimizable > 0)
                SetStatus($"可优化 — {optimizable} 项待处理", false);    // false=红色
            else if (optimized > 0)
                SetStatus($"已优化 — {optimized} 项已完成", true);        // true=绿色
            else
                SetStatus("无待优化项", true);
        }
        catch (Exception ex)
        {
            AppendResult($"扫描失败: {ex.Message}");
            SetStatus($"扫描失败: {ex.Message}", false);
        }
        finally { _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 标签行协议解析 ==================
    // 远程命令输出统一为 key=value 标签行：active.<unit> / enabled.<unit> / file.<path> / EXIT_CODE。
    // 逐行解析替代旧版整串 Contains 匹配，杜绝 "inactive" 含子串 "active" 等误判。

    /// <summary>判断 is-enabled 标签值是否为"单元不存在"</summary>
    private static bool IsNotFound(string value) =>
        value.Contains("No such file or directory")
        || value.Contains("Failed to get unit file state")
        || value.Contains("not-found");

    /// <summary>单元运行中（is-active = active）</summary>
    private static bool IsActiveValue(string value) => value == "active";

    /// <summary>单元开机自启未关（is-enabled = enabled / enabled-runtime）</summary>
    private static bool IsEnabledValue(string value) => value is "enabled" or "enabled-runtime";

    /// <summary>解析远程输出中的 key=value 标签行</summary>
    private static Dictionary<string, string> ParseLabels(string output)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            labels[key] = line[(eq + 1)..].Trim();
        }
        return labels;
    }

    /// <summary>提取 EXIT_CODE 标签值（无则返回 null）</summary>
    private static int? ExtractExitCode(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("EXIT_CODE=") && int.TryParse(line["EXIT_CODE=".Length..].Trim(), out var code))
                return code;
        }
        return null;
    }

    /// <summary>生成人类可读的扫描详情（逐项原因说明）</summary>
    private static string DescribeScan(OptimizationItem item, string output)
    {
        var labels = ParseLabels(output);
        var notes = new List<string>();
        foreach (var kv in labels)
        {
            if (kv.Key.StartsWith("active."))
                notes.Add(kv.Value == "active" ? $"{kv.Key["active.".Length..]} 运行中" : $"{kv.Key["active.".Length..]} 未运行");
            else if (kv.Key.StartsWith("enabled."))
            {
                var unit = kv.Key["enabled.".Length..];
                if (kv.Value == "masked") notes.Add($"{unit} 已mask停用");
                else if (IsNotFound(kv.Value)) notes.Add($"{unit} 不存在");
                else if (IsEnabledValue(kv.Value)) notes.Add($"{unit} 开机自启未关({kv.Value})");
                else notes.Add($"{unit} 开机自启已关({kv.Value})");
            }
            else if (kv.Key.StartsWith("file."))
            {
                var path = kv.Key["file.".Length..];
                var name = System.IO.Path.GetFileName(path);
                notes.Add(kv.Value switch { "EXECUTABLE" => $"{name} 可执行", "NOT_FOUND" => $"{name} 不存在", _ => $"{name} 不可执行" });
            }
            else if (kv.Key.StartsWith("desktop."))
            {
                if (kv.Value == "ENABLED") notes.Add($"自启动项 {kv.Key["desktop.".Length..]} 未禁用");
                else if (kv.Value == "DISABLED") notes.Add($"自启动项 {kv.Key["desktop.".Length..]} 已禁用");
            }
            else if (kv.Key.StartsWith("dbus."))
            {
                if (kv.Value == "ENABLED") notes.Add($"dbus激活 {kv.Key["dbus.".Length..]} 未禁用");
                else if (kv.Value == "DISABLED") notes.Add($"dbus激活 {kv.Key["dbus.".Length..]} 已禁用");
            }
            else if (kv.Key.StartsWith("proc."))
            {
                notes.Add(kv.Value == "RUNNING" ? $"残留进程 {kv.Key["proc.".Length..]} 运行中" : $"残留进程 {kv.Key["proc.".Length..]} 未运行");
            }
        }
        return notes.Count > 0 ? string.Join("; ", notes) : output.Trim();
    }

    /// <summary>若输出中任一 enabled 标签为 masked，标记该项为 mask 停用</summary>
    private static void MarkMasked(OptimizationItem item, string output)
    {
        var labels = ParseLabels(output);
        var masked = labels.Any(kv => kv.Key.StartsWith("enabled.") && kv.Value == "masked");
        item.IsMasked = masked;
        if (masked && item.Status == "已优化") item.Status = "已优化(mask)";
    }

    private static string EvaluateScanResult(OptimizationItem item, string output)
    {
        var labels = ParseLabels(output);
        if (labels.Count == 0) return "扫描失败";

        // chmod 类 — file.<path>=EXECUTABLE / NOT_EXECUTABLE / NOT_FOUND
        if (item.Category == "chmod")
        {
            var fileLabels = labels.Where(kv => kv.Key.StartsWith("file.")).ToList();
            if (fileLabels.Count == 0) return "扫描失败";
            if (fileLabels.Any(kv => kv.Value == "EXECUTABLE")) return "可优化";
            return fileLabels.All(kv => kv.Value == "NOT_FOUND") ? "不适用" : "已优化";
        }

        // autostart 类 — 四维度：可执行位(file.*) + XDG自启动(desktop.*) + dbus激活(dbus.*) + 残留进程(proc.*)
        // 任一项未禁用即"可优化"；全部达标才"已优化"（不做不适用判定：非麒麟桌面全部不存在与已优化同表现，均无需操作）
        if (item.Category == "autostart")
        {
            var fileLabels = labels.Where(kv => kv.Key.StartsWith("file.")).ToList();
            var desktopLabels = labels.Where(kv => kv.Key.StartsWith("desktop.")).ToList();
            var dbusLabels = labels.Where(kv => kv.Key.StartsWith("dbus.")).ToList();
            var procLabels = labels.Where(kv => kv.Key.StartsWith("proc.")).ToList();
            if (fileLabels.Count == 0 && desktopLabels.Count == 0 && dbusLabels.Count == 0 && procLabels.Count == 0) return "扫描失败";
            if (fileLabels.Any(kv => kv.Value == "EXECUTABLE")) return "可优化";
            if (desktopLabels.Any(kv => kv.Value == "ENABLED")) return "可优化";
            if (dbusLabels.Any(kv => kv.Value == "ENABLED")) return "可优化";
            if (procLabels.Any(kv => kv.Value == "RUNNING")) return "可优化";
            return "已优化";
        }

        // systemctl 类 — 双维度判定：运行状态(active.*) + 开机自启(enabled.*)
        // 任一维度未达标（仍在运行 / 开机自启未关）即"可优化"；两者均达标才"已优化"
        var activeLabels = labels.Where(kv => kv.Key.StartsWith("active.")).ToList();
        var enabledLabels = labels.Where(kv => kv.Key.StartsWith("enabled.")).ToList();

        // 全部单元均不存在 → 不适用
        if (enabledLabels.Count > 0 && enabledLabels.All(kv => IsNotFound(kv.Value)) && !activeLabels.Any(kv => IsActiveValue(kv.Value)))
            return "不适用";

        if (activeLabels.Any(kv => IsActiveValue(kv.Value))) return "可优化";     // 仍有服务在运行
        if (enabledLabels.Any(kv => IsEnabledValue(kv.Value))) return "可优化";   // 仍开机自启

        return "已优化";
    }

    private void UpdateSummary()
    {
        var optimizable = _items.Count(i => i.Status == "可优化");
        var optimized = _items.Count(i => i.Status.StartsWith("已优化"));
        var na = _items.Count(i => i.Status == "不适用");
        _infoText.Text = $"优化项数量: {_items.Count} 项 (可优化: {optimizable}, 已优化: {optimized}, 不适用: {na})";
    }

    // ================== 优化 ==================

    private async void DoOptimize()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        var selected = _items.Where(i => i.IsSelected && i.Status == "可优化" && !string.IsNullOrEmpty(i.OptimizeCmd)).ToList();
        if (selected.Count == 0) { SetStatus("没有选中可优化的项", false); return; }

        _optimizeBtn.IsEnabled = false;
        _scanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus($"正在优化 {selected.Count} 项...", true);
        AppendResult("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始优化 {selected.Count} 项...");

        var username = UserBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) username = "root";
        var password = PassBox.Password;

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    Dispatcher.Invoke(() => { item.Status = "优化中"; RefreshGrid(); });
                    Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name}..."));
                    Dispatcher.Invoke(() => AppendResult($"    命令: {item.OptimizeCmd}"));

                    try
                    {
                        var cmd = RunCommandSudo(ssh, item.OptimizeCmd, username, password);
                        if (!string.IsNullOrWhiteSpace(cmd))
                            Dispatcher.Invoke(() => AppendResult($"    输出: {cmd.Trim()}"));
                        var exitCode = ExtractExitCode(cmd);
                        if (exitCode != null && exitCode != 0)
                            Dispatcher.Invoke(() => AppendResult($"    警告: 停用命令退出码 {exitCode}（部分单元可能不存在，以验证为准）"));
                        // 立即验证（双维度：运行状态 + 开机自启）
                        if (!string.IsNullOrEmpty(item.VerifyCmd))
                        {
                            Thread.Sleep(500); // 等待服务完全停止
                            var verifyOutput = RunCommand(ssh, item.VerifyCmd);
                            item.Status = EvaluateVerifyResult(item, verifyOutput);
                            MarkMasked(item, verifyOutput);
                            Dispatcher.Invoke(() => AppendResult($"    验证: {verifyOutput.Trim()}"));
                            Dispatcher.Invoke(() => AppendResult($"    → {item.Status}"));
                        }
                        else
                        {
                            item.Status = "已优化";
                            Dispatcher.Invoke(() => AppendResult($"    → {item.Status}"));
                        }
                        item.IsOptimized = item.Status.StartsWith("已优化");
                    }
                    catch (Exception ex)
                    {
                        item.Status = "失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => AppendResult($"    → 失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshGrid());
                }
            });

            UpdateSummary();
            SetStatus("优化完成", true);
        }
        catch (Exception ex)
        {
            AppendResult($"优化失败: {ex.Message}");
            SetStatus($"优化失败: {ex.Message}", false);
        }
        finally { _optimizeBtn.IsEnabled = true; _scanBtn.IsEnabled = true; _verifyBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    private static string EvaluateVerifyResult(OptimizationItem item, string output)
    {
        var labels = ParseLabels(output);
        if (labels.Count == 0) return "验证异常";

        // chmod 类 — 任一文件仍可执行 → 失败
        if (item.Category == "chmod")
            return labels.Any(kv => kv.Key.StartsWith("file.") && kv.Value == "EXECUTABLE") ? "失败" : "已优化";

        // autostart 类 — 任一维度未禁用（可执行/自启动未禁/dbus未禁/进程仍在）→ 失败
        if (item.Category == "autostart")
            return labels.Any(kv =>
                (kv.Key.StartsWith("file.") && kv.Value == "EXECUTABLE")
                || (kv.Key.StartsWith("desktop.") && kv.Value == "ENABLED")
                || (kv.Key.StartsWith("dbus.") && kv.Value == "ENABLED")
                || (kv.Key.StartsWith("proc.") && kv.Value == "RUNNING")) ? "失败" : "已优化";

        // systemctl 类 — 双维度：仍在运行 或 仍开机自启 → 失败
        bool stillRunning = labels.Any(kv => kv.Key.StartsWith("active.") && IsActiveValue(kv.Value));
        bool stillEnabled = labels.Any(kv => kv.Key.StartsWith("enabled.") && IsEnabledValue(kv.Value));
        return stillRunning || stillEnabled ? "失败" : "已优化";
    }

    // ================== 验证 ==================

    private async void DoVerify()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        _verifyBtn.IsEnabled = false;
        _scanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus("正在验证...", true);
        AppendResult("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始逐项验证...");

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in _items)
                {
                    if (item.Status == "不适用" || item.Status == "待扫描") continue;

                    try
                    {
                        // 验证 = 重新扫描：所有项 ScanCmd 与 VerifyCmd 一致，统一走扫描判定（可优化/已优化/不适用）
                        var output = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeScan(item, output);
                        item.Status = EvaluateScanResult(item, output);
                        item.IsApplicable = item.Status == "可优化";
                        if (item.Status == "不适用") item.IsSelected = false;
                        MarkMasked(item, output);
                        Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name} → {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "验证失败";
                        Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name} → 验证失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshGrid());
                }
            });

            UpdateSummary();
            SetStatus("验证完成", true);
        }
        catch (Exception ex)
        {
            AppendResult($"验证失败: {ex.Message}");
            SetStatus($"验证失败: {ex.Message}", false);
        }
        finally { _verifyBtn.IsEnabled = true; _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 恢复 ==================

    private async void DoRestore()
    {
        var ssh = Ssh;
        if (ssh == null || !ssh.IsConnected) { SetStatus("SSH 未连接", false); return; }

        var selected = _items.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.RestoreCmd)).ToList();
        if (selected.Count == 0) { SetStatus("没有选中可恢复的项", false); return; }

        _restoreBtn.IsEnabled = false;
        _scanBtn.IsEnabled = false;
        DisconnectBtn.IsEnabled = false;
        SetStatus($"正在恢复 {selected.Count} 项...", true);
        AppendResult("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        AppendResult($"[{DateTime.Now:HH:mm:ss}] 开始恢复 {selected.Count} 项...");

        var username = UserBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) username = "root";
        var password = PassBox.Password;

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    Dispatcher.Invoke(() => { item.Status = "恢复中"; RefreshGrid(); });
                    Dispatcher.Invoke(() => AppendResult($"  [{item.Id}] {item.Name}..."));
                    Dispatcher.Invoke(() => AppendResult($"    命令: {item.RestoreCmd}"));

                    try
                    {
                        var cmd = RunCommandSudo(ssh, item.RestoreCmd, username, password);
                        if (!string.IsNullOrWhiteSpace(cmd))
                            Dispatcher.Invoke(() => AppendResult($"    输出: {cmd.Trim()}"));
                        Thread.Sleep(300);
                        var scanOutput = RunCommand(ssh, item.ScanCmd);
                        item.ScanDetail = DescribeScan(item, scanOutput);
                        item.Status = EvaluateScanResult(item, scanOutput);
                        item.IsApplicable = item.Status == "可优化";
                        item.IsMasked = false;
                        Dispatcher.Invoke(() => AppendResult($"    恢复后状态: {item.Status}"));
                    }
                    catch (Exception ex)
                    {
                        item.Status = "失败";
                        item.ScanDetail = ex.Message;
                        Dispatcher.Invoke(() => AppendResult($"    → 失败: {ex.Message}"));
                    }
                    Dispatcher.Invoke(() => RefreshGrid());
                }
            });

            UpdateSummary();
            SetStatus("恢复完成", true);
        }
        catch (Exception ex)
        {
            AppendResult($"恢复失败: {ex.Message}");
            SetStatus($"恢复失败: {ex.Message}", false);
        }
        finally { _restoreBtn.IsEnabled = true; _scanBtn.IsEnabled = true; DisconnectBtn.IsEnabled = Ssh != null; }
    }

    // ================== 状态列颜色转换器 ==================

    private class StatusColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var text = value?.ToString() ?? "";
            if (text.StartsWith("已优化")) return new SolidColorBrush(Color.FromRgb(0, 150, 0));
            return text switch
            {
                "未扫描" or "待扫描" => Brushes.Gray,
                "可优化" or "优化中" or "恢复中" => new SolidColorBrush(Color.FromRgb(220, 50, 50)),
                "失败" or "扫描失败" or "验证失败" => new SolidColorBrush(Color.FromRgb(200, 80, 80)),
                "不适用" => Brushes.Gray,
                _ => Brushes.Gray
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ================== 14 项优化定义 ==================
    // 命令协议：扫描/验证输出统一为 key=value 标签行（active.<unit> / enabled.<unit> / file.<path> / EXIT_CODE），
    // 由 ParseLabels 逐行解析，杜绝整串 Contains 子串误判（如 "inactive" 含 "active"）。
    // 停用 = disable --now（立即停用）+ mask（永久屏蔽，防系统更新/依赖拉起复活）；恢复 = unmask + enable。
    // 多命令序列用 sh -c '...' 包裹，确保 ; 后续命令同样以 root 执行（sudo -S 只作用于第一个命令）。

    /// <summary>生成 is-active 标签命令（stderr 丢弃，值仅 active/inactive/failed 等）</summary>
    private static string Act(string unit) =>
        $"echo \"active.{unit}=$(systemctl is-active {unit} 2>/dev/null)\"";

    /// <summary>生成 is-enabled 标签命令（stderr 合并，值含 not-found 信息）</summary>
    private static string En(string unit) =>
        $"echo \"enabled.{unit}=$(systemctl is-enabled {unit} 2>&1)\"";

    /// <summary>扫描命令：每个单元输出 active/enabled 两个标签</summary>
    private static string SysScan(params string[] units) =>
        string.Join("; ", units.SelectMany(u => new[] { Act(u), En(u) }));

    /// <summary>停用命令：disable --now 后 mask，并捕获退出码（整体以 root 执行）</summary>
    private static string MaskCmd(params string[] units) =>
        $"sh -c 'systemctl disable --now {string.Join(' ', units)} 2>&1; systemctl mask {string.Join(' ', units)} 2>&1; echo EXIT_CODE=$?'";

    /// <summary>恢复命令：unmask 后 enable，并捕获退出码（整体以 root 执行）</summary>
    private static string UnmaskCmd(params string[] units) =>
        $"sh -c 'systemctl unmask {string.Join(' ', units)} 2>&1; systemctl enable {string.Join(' ', units)} 2>&1; echo EXIT_CODE=$?'";

    /// <summary>chmod 类扫描命令：file.<path>=EXECUTABLE / NOT_EXECUTABLE / NOT_FOUND</summary>
    private static string FileScan(params string[] paths) =>
        string.Join("; ", paths.Select(p =>
            $"if [ -e \"{p}\" ]; then if [ -x \"{p}\" ]; then echo \"file.{p}=EXECUTABLE\"; else echo \"file.{p}=NOT_EXECUTABLE\"; fi; else echo \"file.{p}=NOT_FOUND\"; fi"));

    /// <summary>chmod 类变更命令：mode 为 -x（停用）或 +x（恢复），整体以 root 执行</summary>
    private static string ChmodCmd(string mode, params string[] paths) =>
        $"sh -c 'chmod {mode} {string.Join(' ', paths.Select(p => $"\"{p}\""))} 2>&1; echo EXIT_CODE=$?'";

    // ================== autostart 类（会话自启动清理）命令构造 ==================
    // 会话残留进程由 XDG 自启动(.desktop) / dbus 激活(.service) / 直接可执行位拉起，不经 systemd，mask 无法阻断。
    // 关闭手段三合一：pkill 结束残留进程 + chmod -x 去可执行位(ELF) + .desktop/.service 改名禁用(脚本类唯一有效手段)。
    // pgrep/pkill 模式用 [x]yyy 正则 + ^锚定行首，避免匹配到扫描命令自身（命令行含相同字符串）。

    /// <summary>XDG 自启动扫描：desktop.<name>=ENABLED / DISABLED / NOT_FOUND（.disabled 后缀为已禁用标记）</summary>
    private static string DesktopScan(params string[] names) =>
        string.Join("; ", names.Select(n =>
            $"if [ -e /etc/xdg/autostart/{n}.desktop ]; then echo \"desktop.{n}=ENABLED\"; elif [ -e /etc/xdg/autostart/{n}.desktop.disabled ]; then echo \"desktop.{n}=DISABLED\"; else echo \"desktop.{n}=NOT_FOUND\"; fi"));

    /// <summary>dbus 激活文件扫描：dbus.<name>=ENABLED / DISABLED / NOT_FOUND</summary>
    private static string DbusScan(params string[] names) =>
        string.Join("; ", names.Select(n =>
            $"if [ -e /usr/share/dbus-1/system-services/{n}.service ]; then echo \"dbus.{n}=ENABLED\"; elif [ -e /usr/share/dbus-1/system-services/{n}.service.disabled ]; then echo \"dbus.{n}=DISABLED\"; else echo \"dbus.{n}=NOT_FOUND\"; fi"));

    /// <summary>残留进程扫描：proc.<key>=RUNNING / STOPPED（Pattern 为 ^ 锚定正则，如 ^/usr/bin/[u]kui-bluetooth）</summary>
    private static string ProcScan(params (string Key, string Pattern)[] procs) =>
        string.Join("; ", procs.Select(p =>
            $"echo \"proc.{p.Key}=$(pgrep -f '{p.Pattern}' >/dev/null 2>&1 && echo RUNNING || echo STOPPED)\""));

    /// <summary>autostart 类停用命令：pkill 残留进程 + chmod -x + 禁用 .desktop 与 dbus .service（整体 root 执行）</summary>
    private static string AutoDisableCmd(string[] files, string[] desktops, string[] dbus, (string Key, string Pattern)[] procs)
    {
        var parts = new List<string>();
        foreach (var p in procs) parts.Add($"pkill -f \"{p.Pattern}\" 2>/dev/null");
        parts.Add($"chmod -x {string.Join(' ', files.Select(f => $"\"{f}\""))} 2>/dev/null");
        parts.Add($"for d in {string.Join(' ', desktops)}; do [ -e /etc/xdg/autostart/$d.desktop ] && mv /etc/xdg/autostart/$d.desktop /etc/xdg/autostart/$d.desktop.disabled 2>/dev/null || true; done");
        parts.Add($"for s in {string.Join(' ', dbus)}; do [ -e /usr/share/dbus-1/system-services/$s.service ] && mv /usr/share/dbus-1/system-services/$s.service /usr/share/dbus-1/system-services/$s.service.disabled 2>/dev/null || true; done");
        parts.Add("echo EXIT_CODE=$?");
        return $"sh -c '{string.Join("; ", parts)}'";
    }

    /// <summary>autostart 类恢复命令：chmod +x + 还原 .desktop 与 dbus .service（重新登录后进程自动恢复）</summary>
    private static string AutoEnableCmd(string[] files, string[] desktops, string[] dbus)
    {
        var parts = new List<string>();
        parts.Add($"chmod +x {string.Join(' ', files.Select(f => $"\"{f}\""))} 2>/dev/null");
        parts.Add($"for d in {string.Join(' ', desktops)}; do [ -e /etc/xdg/autostart/$d.desktop.disabled ] && mv /etc/xdg/autostart/$d.desktop.disabled /etc/xdg/autostart/$d.desktop 2>/dev/null || true; done");
        parts.Add($"for s in {string.Join(' ', dbus)}; do [ -e /usr/share/dbus-1/system-services/$s.service.disabled ] && mv /usr/share/dbus-1/system-services/$s.service.disabled /usr/share/dbus-1/system-services/$s.service 2>/dev/null || true; done");
        parts.Add("echo EXIT_CODE=$?");
        return $"sh -c '{string.Join("; ", parts)}'";
    }

    /// <summary>systemctl 类优化项工厂</summary>
    private static OptimizationItem SysItem(int id, string name, string risk, string riskNote, params string[] units) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "systemctl",
        ScanCmd = SysScan(units),
        OptimizeCmd = MaskCmd(units),
        VerifyCmd = SysScan(units),
        RestoreCmd = UnmaskCmd(units),
        RiskNote = riskNote
    };

    /// <summary>chmod 类优化项工厂</summary>
    private static OptimizationItem ChmodItem(int id, string name, string risk, string riskNote, params string[] paths) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "chmod",
        ScanCmd = FileScan(paths),
        OptimizeCmd = ChmodCmd("-x", paths),
        VerifyCmd = FileScan(paths),
        RestoreCmd = ChmodCmd("+x", paths),
        RiskNote = riskNote
    };

    /// <summary>autostart 类（会话自启动清理）优化项工厂：files=去可执行位的 ELF 二进制；
    /// desktops=禁用的 XDG 自启动项名（不含 .desktop 后缀）；dbus=禁用的 dbus 激活服务名（不含 .service 后缀）；
    /// procs=残留进程探测/清理的 ^ 锚定正则（Key 为标签名，Pattern 为 pgrep/pkill 模式）</summary>
    private static OptimizationItem AutoItem(int id, string name, string risk, string riskNote,
        string[] files, string[] desktops, string[] dbus, (string Key, string Pattern)[] procs) => new()
    {
        Id = id, Name = name, RiskLevel = risk, Category = "autostart",
        ScanCmd = string.Join("; ", new[] { FileScan(files), DesktopScan(desktops), DbusScan(dbus), ProcScan(procs) }),
        OptimizeCmd = AutoDisableCmd(files, desktops, dbus, procs),
        VerifyCmd = string.Join("; ", new[] { FileScan(files), DesktopScan(desktops), DbusScan(dbus), ProcScan(procs) }),
        RestoreCmd = AutoEnableCmd(files, desktops, dbus),
        RiskNote = riskNote
    };

    private static List<OptimizationItem> GetOptimizationItems() => new()
    {
        SysItem(1, "关闭蓝牙、打印机、生物识别全套服务", "低",
            "服务器场景通常不需要蓝牙/打印/生物识别",
            "bluetooth.service", "cups.service", "cups.socket", "cups.path", "cups-browsed.service", "biometric-authentication.service", "ukui-bluetooth.service"),
        ChmodItem(2, "关闭麒麟管家后台进程", "中",
            "麒麟管家是桌面环境组件，纯服务器场景可关闭",
            "/usr/bin/kylin-os-manager-daemon",
            "/usr/share/kylin-os-manager/kylin-core-dump-monitor/kylin-core-dump-monitor.sh",
            "/usr/lib/kylin-os-manager/bin/kylin-os-manager-session-service"),
        SysItem(3, "关闭麒麟管家系统服务", "中",
            "麒麟管家的 systemd 服务单元，与后台进程配套关闭；纯服务器场景建议关闭",
            "kylin-core-dump-monitor.service", "kylin-process-manager-daemon.service", "com.kylin.kysdk.SyncConfig.service", "com.kylin-os-manager.service"),
        SysItem(4, "关闭系统激活校验服务", "低",
            "关闭后跳过系统激活校验，不影响日常使用",
            "kylin-activation-check.service"),
        SysItem(5, "关闭定时更新服务", "中",
            "关闭后将不再自动下载和安装系统更新，需手动维护",
            "kylin-source-update.service", "kylin-source-update-timer.service", "kylin-source-update-timer.timer", "kylin-system-updater.service", "kylin-offline-upgrade.service", "kylin-unattended-upgrades.service"),
        SysItem(6, "关闭安全审计日志服务(auditd)", "低",
            "关闭后 /var/log/audit/ 不再增长，但失去审计追踪能力",
            "auditd.service"),
        SysItem(7, "关闭 Samba 服务", "低",
            "不需要 Windows 文件共享的场景可关闭",
            "smbd.service", "nmbd.service"),
        SysItem(8, "关闭 pppd-dns 服务", "低",
            "PPP 拨号 DNS 更新服务，不使用拨号网络即可关闭",
            "pppd-dns.service"),
        SysItem(9, "关闭局域网自动发现服务(avahi-daemon)", "低",
            "mDNS/Zeroconf 服务发现协议，服务器场景通常不需要",
            "avahi-daemon.service", "avahi-daemon.socket"),
        ChmodItem(10, "关闭天气服务(kylin-weather)", "低",
            "天气小部件为桌面附加组件，关闭后不再显示天气并释放约 0.8% 内存（进程内存排行第 3）；重启后生效",
            "/usr/bin/kylin-weather"),
        SysItem(11, "关闭磁盘、存储冗余监控服务(LVM)", "中",
            "未使用 LVM 逻辑卷管理的系统可关闭；使用 LVM 的系统建议保留",
            "lvm2-monitor.service", "lvm2-lvmpolld.service", "lvm2-lvmpolld.socket"),
        SysItem(12, "关闭多账户实时监控服务(accounts-daemon)", "中",
            "mask 后即使手动 systemctl start 也无法启动，纯服务器场景建议关闭",
            "accounts-daemon.service"),
        ChmodItem(13, "关闭系统全局搜索后台(ukui-search)", "低",
            "UKUI 桌面环境的全局搜索功能，纯命令行服务器不需要",
            "/usr/bin/ukui-search"),
        AutoItem(14, "清理会话层自启动残留(蓝牙/激活/更新/打印/管家/搜索)", "中",
            "上述服务虽已 mask，但桌面会话自启动项(XDG autostart / dbus 激活)仍会拉起残留进程；此项禁用自启动入口、去可执行位并结束残留进程，彻底阻断重启复活；恢复后重新登录生效",
            new[]
            {
                "/usr/bin/ukui-bluetooth",
                "/usr/bin/kylin-activation", "/usr/bin/kylin-activation-renewalcheck", "/usr/bin/kylin-activation-prompt", "/usr/sbin/activation-daemon",
                "/usr/bin/OfflineUpgradeNotification", "/usr/bin/kylin-background-upgrade",
                "/usr/bin/kylin-printer-applet", "/usr/bin/kpr-backend",
                "/usr/bin/kylin-process-manager",
                "/usr/bin/ukui-search-service", "/usr/bin/ukui-search-app-data-service", "/usr/bin/ukui-search-service-dir-manager", "/usr/bin/ukui-search-systemdbus",
            },
            new[]
            {
                "ukui-bluetooth",
                "kylin-activation-autostart", "kylin-activation-prompt-autostart", "kylin-activation-check-deactivate", "kylin-activation-volume",
                "print-applet", "kylin-printer-applet",
                "kylin-process-manager",
                "ukui-search", "ukui-search-service", "ukui-search-app-data-service", "ukui-search-service-dir-manager",
                "UpgradeMountFailedNotify", "kylin-background-upgrade", "kylin-updatefinish-notify1", "kylin-updateresult-notify", "kylin-updateresult-notify-2303", "kpct-updatefinish-notify", "kylin-reboot-installnotify", "kylin-stepinstall-notify",
            },
            new[]
            {
                "com.kylin.UpgradeStrategies", "org.kylin.KprBackend", "com.ukui.search.qt.systemdbus",
            },
            new (string, string)[]
            {
                ("bluetooth", "^/usr/bin/[u]kui-bluetooth"),
                ("activation", "^/usr/bin/[k]ylin-activation"),
                ("activationdaemon", "^/usr/sbin/[a]ctivation-daemon"),
                ("upgradestrategies", "^/usr/bin/python3 /usr/share/[k]ylin-system-updater"),
                ("kconf2", "^[k]conf2"),
                ("offlineupgrade", "^/usr/bin/[O]fflineUpgradeNotification"),
                ("backgroundupgrade", "^/usr/bin/[k]ylin-background-upgrade"),
                ("printapplet", "^/usr/bin/python3 /usr/share/[s]ystem-config-printer"),
                ("printerapplet", "^/usr/bin/[k]ylin-printer-applet"),
                ("kprbackend", "^/usr/bin/[k]pr-backend"),
                ("procmanager", "^/usr/bin/[k]ylin-process-manager"),
                ("ukuisearch", "^/usr/bin/[u]kui-search"),
            }),
    };
}
