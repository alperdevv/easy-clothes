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
            OnPropertyChanged(nameof(IsShoes));
            OnPropertyChanged(nameof(IsHat));
            onChanged(this, true);
        }
    }

    public bool IsProp => Model.IsProp;
    public bool IsShoes => Model.Component == ComponentSlot.Shoes;
    public bool IsHat => Model.Prop == PropSlot.Hat;

    public string FirstPersonLabel => Model.FirstPersonModelPath is { } path ? ShortName(path) : "Ninguno";
    public bool HasFirstPerson => Model.FirstPersonModelPath is not null;
    public string ClothPhysicsLabel => Model.ClothPhysicsPath is { } path ? ShortName(path) : "Ninguna";
    public bool HasClothPhysics => Model.ClothPhysicsPath is not null;

    public bool HasHeels
    {
        get => Model.Flags.HeelHeight is not null;
        set
        {
            if (value == HasHeels) return;
            Model.Flags.HeelHeight = value ? 1f : null;
            RefreshExtras();
            onChanged(this, false);
        }
    }

    public decimal HeelHeight
    {
        get => (decimal)(Model.Flags.HeelHeight ?? 1f);
        set
        {
            var height = (float)Math.Clamp(value, 0.1m, 3m);
            if (Model.Flags.HeelHeight == height) return;
            Model.Flags.HeelHeight = height;
            OnPropertyChanged();
            onChanged(this, false);
        }
    }

    public static IReadOnlyList<string> HatHairOptions { get; } = ["Se ve entero", "Recortado (como los gorros del juego)", "Oculto"];

    public int HatHairIndex
    {
        get => (int)Model.Flags.HatHair;
        set
        {
            if (value < 0 || value == HatHairIndex) return;
            Model.Flags.HatHair = (HatHair)value;
            OnPropertyChanged();
            onChanged(this, false);
        }
    }

    public void RefreshExtras()
    {
        OnPropertyChanged(nameof(FirstPersonLabel));
        OnPropertyChanged(nameof(HasFirstPerson));
        OnPropertyChanged(nameof(ClothPhysicsLabel));
        OnPropertyChanged(nameof(HasClothPhysics));
        OnPropertyChanged(nameof(HasHeels));
        OnPropertyChanged(nameof(HeelHeight));
    }

    public IReadOnlyList<TextureItem> Textures => Model.Textures
        .Select((t, i) => new TextureItem(i, TextureVariation.LetterFor(i).ToString(), ShortName(t.TexturePath), t.TexturePath))
        .ToList();

    private static string ShortName(string path)
    {
        var name = Path.GetFileName(path);
        return name[(name.IndexOf('^') + 1)..];
    }

    public void RefreshTextures()
    {
        OnPropertyChanged(nameof(Textures));
        OnPropertyChanged(nameof(Summary));
    }

    public string Summary => $"{Model.Textures.Count} textura{(Model.Textures.Count == 1 ? "" : "s")}";
}
