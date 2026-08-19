namespace ToolHelper.ViewModels;

/// <summary>
/// 工具分类模型
/// </summary>
public class ToolCategory
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "CodeBraces";
    public List<ToolItem> Tools { get; set; } = new();

    public ToolCategory(string name, string icon = "CodeBraces")
    {
        Name = name;
        Icon = icon;
    }
}
