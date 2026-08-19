using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ToolHelper.Views;

/// <summary>
/// 视图高度贴合宿主视口的辅助方法。
/// MainWindow 右侧内容区包裹了 ScrollViewer，会向视图传递无限高度约束，
/// 使视图内部 Grid 的星号行（GridLength.Star）退化为“按内容自然高度”——
/// 数据变多时该区域被撑开，排在它后面的区域被顶出可视区（例如日志区、详情区消失）。
/// 把视图高度钉在宿主 ScrollViewer 的视口高度上，星号行即可恢复按比例分配，超出的内容在各区域内部滚动。
/// </summary>
internal static class ViewportFitHelper
{
    /// <summary>
    /// 让视图高度跟随宿主 ScrollViewer 的视口高度（不低于 minHeight），并在窗口尺寸变化时同步更新。
    /// 视口高度小于 minHeight 时保持 minHeight，由宿主滚动条兜底查看。
    /// </summary>
    public static void FitToViewport(FrameworkElement view, double minHeight)
    {
        var host = FindHostScrollViewer(view);
        if (host == null) return;

        void Apply()
        {
            var h = host.ViewportHeight;
            var target = h > minHeight ? h : minHeight;
            // Height 初始为 NaN（Auto 未赋值），Math.Abs(NaN - target) 恒为 NaN、NaN > 0.5 恒 false，
            // 会漏掉首次赋值使本方法完全失效（视图无限拉长、宿主滚动条出现）；必须显式处理 NaN。
            // 已赋值后仅在实际差异 > 0.5px 时更新，避免连锁布局抖动。
            if (double.IsNaN(view.Height) || Math.Abs(view.Height - target) > 0.5)
                view.Height = target;
        }

        // 延迟到布局完全结束后执行，避免窗口最大化/恢复动画期间（Win11）读到过渡的 ViewportHeight
        void ScheduleApply() => view.Dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.Background);

        host.SizeChanged += (_, _) => ScheduleApply();
        // 自身高度变化也重新校准（幂等：目标值与当前一致时不会再次改动）
        view.SizeChanged += (_, _) => ScheduleApply();

        // 首次布局完成前 ViewportHeight 可能为 0，用一次性 LayoutUpdated 兜底重算（取到有效高度后立即退订）
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (host.ViewportHeight <= 0) return;
            host.LayoutUpdated -= OnLayoutUpdated;
            ScheduleApply();
        }
        host.LayoutUpdated += OnLayoutUpdated;

        ScheduleApply();
    }

    /// <summary>沿可视树向上查找宿主 ScrollViewer（即 MainWindow 内容区的滚动容器）</summary>
    private static ScrollViewer? FindHostScrollViewer(DependencyObject start)
    {
        var parent = VisualTreeHelper.GetParent(start);
        while (parent != null)
        {
            if (parent is ScrollViewer scroll) return scroll;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
