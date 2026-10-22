using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyClothes.App.ViewModels;

public sealed partial class TreeNode(string title, DrawableViewModel? drawable = null, TattooViewModel? tattoo = null, string? number = null) : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = title;

    public DrawableViewModel? Drawable { get; } = drawable;

    // In-game number of a garment, shown before its name.
    public string? Number { get; } = number;
    public bool HasNumber => Number is not null;
    public TattooViewModel? Tattoo { get; } = tattoo;
    public ObservableCollection<TreeNode> Children { get; } = [];
    public bool IsExpanded { get; set; } = true;
}
