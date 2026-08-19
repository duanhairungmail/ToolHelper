using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolHelper.ViewModels;

/// <summary>
/// 单个工具项模型
/// </summary>
public partial class ToolItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Func<object> CreateView { get; set; } = () => null!;

    public ToolItem(string name, string description, Func<object> createView)
    {
        Name = name;
        Description = description;
        CreateView = createView;
    }

    /// <summary>
    /// 每次都创建新视图实例（WPF视觉元素不能重复附加到视觉树）
    /// </summary>
    public object GetView()
    {
        return CreateView();
    }
}
