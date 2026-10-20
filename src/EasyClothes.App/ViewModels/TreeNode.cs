using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyClothes.App.ViewModels;

public sealed partial class TreeNode(string title, DrawableViewModel? drawable = null) : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = title;

    public DrawableViewModel? Drawable { get; } = drawable;
    public ObservableCollection<TreeNode> Children { get; } = [];
    public bool IsExpanded { get; set; } = true;
}
