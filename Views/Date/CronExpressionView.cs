using System.Text;
using MaterialDesignThemes.Wpf;

namespace ToolHelper.Views.Date;

public class CronExpressionView : StandardToolView
{
    protected override PackIconKind TitleIcon => PackIconKind.CalendarClock;

    public CronExpressionView() : base("Cron 表达式",
        "Cron 定时表达式生成与解析（秒 分 时 日 月 周）")
    {
        InputPlaceholder = "输入 Cron 表达式（如 0 0 12 * * ?）...";
        AddHandler("解析表达式", Parse, PackIconKind.Magnify);
        AddHandler("常用模板", ShowTemplates, PackIconKind.FormatListBulleted);
        AddHandler("清空", Clear, PackIconKind.Eraser);
    }

    private void Parse()
    {
        try
        {
            var parts = InputBox.Text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts.Length > 7)
            {
                SetStatus("Cron 表达式应为 5-7 个字段", false); return;
            }

            var fieldNames = new[] { "秒", "分", "时", "日", "月", "周", "年" };
            var sb = new StringBuilder();
            sb.AppendLine($"原始表达式: {InputBox.Text.Trim()}");
            sb.AppendLine(new string('-', 40));
            for (int i = 0; i < parts.Length; i++)
            {
                var name = i < fieldNames.Length ? fieldNames[i] : $"字段{i + 1}";
                sb.AppendLine($"{name}: {parts[i]} → {DescribeCronField(parts[i], i)}");
            }
            OutputBox.Text = sb.ToString();
            SetStatus("解析完成", true);
        }
        catch (Exception ex) { SetStatus($"错误: {ex.Message}", false); }
    }

    private string DescribeCronField(string field, int index)
    {
        if (field == "*") return "每个值（所有）";
        if (field == "?") return "不指定";
        if (field.Contains('/'))
        {
            var p = field.Split('/');
            return $"从 {p[0]} 开始，每隔 {p[1]}";
        }
        if (field.Contains('-'))
        {
            var p = field.Split('-');
            return $"从 {p[0]} 到 {p[1]}";
        }
        if (field.Contains(','))
            return $"指定值: {field}";
        return $"值: {field}";
    }

    private void ShowTemplates()
    {
        var sb = new StringBuilder();
        sb.AppendLine("常用 Cron 表达式模板:");
        sb.AppendLine("========================================");
        sb.AppendLine("每秒钟:          * * * * * ?");
        sb.AppendLine("每分钟:          0 * * * * ?");
        sb.AppendLine("每5分钟:         0 0/5 * * * ?");
        sb.AppendLine("每小时:          0 0 * * * ?");
        sb.AppendLine("每天中午12点:    0 0 12 * * ?");
        sb.AppendLine("每天凌晨2点:     0 0 2 * * ?");
        sb.AppendLine("每周一上午9点:   0 0 9 ? * MON");
        sb.AppendLine("每月1号凌晨:     0 0 0 1 * ?");
        sb.AppendLine("每年1月1号:      0 0 0 1 1 ?");
        sb.AppendLine("工作日早9晚6:    0 0 9,18 ? * MON-FRI");
        OutputBox.Text = sb.ToString();
        SetStatus("模板已加载", true);
    }

    private void Clear() { InputBox.Text = ""; OutputBox.Text = ""; StatusText.Text = ""; }
}
