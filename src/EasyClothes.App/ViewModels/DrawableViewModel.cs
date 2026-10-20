using CommunityToolkit.Mvvm.ComponentModel;
using EasyClothes.Core.Model;

namespace EasyClothes.App.ViewModels;

public sealed record TextureItem(int Index, string Letter, string FileName, string RelativePath);

// onChanged receives true when the change moves the drawable to another tree group.
public sealed partial class DrawableViewModel(Drawable model, Action<DrawableViewModel, bool> onChanged) : ObservableObject
{
    public Drawable Model { get; } = model;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (SetProperty(Model.Name, value, Model, (d, v) => d.Name = v)) onChanged(this, false);
        }
    }

    public GenderOption Gender
    {
        get => GenderOption.For(Model.Gender);
        set
        {
            if (value is null || value.Value == Model.Gender) return;
            Model.Gender = value.Value;
            OnPropertyChanged();
            onChanged(this, true);
        }
    }

    public SlotOption Slot
    {
        get => SlotOption.For(Model);
        set
        {
            if (value is null || value == Slot) return;
            Model.Component = value.Slot.Component;
            Model.Prop = value.Slot.Prop;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProp));
            onChanged(this, true);
        }
    }

    public bool IsProp => Model.IsProp;

    public bool HideHair
    {
        get => Model.Flags.HideHair;
        set
        {
            if (SetProperty(Model.Flags.HideHair, value, Model.Flags, (f, v) => f.HideHair = v)) onChanged(this, false);
        }
    }

    public bool KnockOffOnHit
    {
        get => Model.Flags.KnockOffOnHit;
        set
        {
            if (SetProperty(Model.Flags.KnockOffOnHit, value, Model.Flags, (f, v) => f.KnockOffOnHit = v)) onChanged(this, false);
        }
    }

    public IReadOnlyList<TextureItem> Textures => Model.Textures
        .Select((t, i) => new TextureItem(i, TextureVariation.LetterFor(i).ToString(), ShortName(t.TexturePath), t.TexturePath))
        .ToList();

    private static string ShortName(string path)
    {
        var name = Path.GetFileName(path);
        return name[(name.IndexOf('^') + 1)..];
    }

    public string Summary => $"{Model.Textures.Count} textura{(Model.Textures.Count == 1 ? "" : "s")}";
}
