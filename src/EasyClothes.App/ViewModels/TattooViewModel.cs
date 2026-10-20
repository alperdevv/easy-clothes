using CommunityToolkit.Mvvm.ComponentModel;
using EasyClothes.Core.Model;

namespace EasyClothes.App.ViewModels;

public sealed record TattooZoneOption(string Label, TattooZone Value)
{
    public static readonly IReadOnlyList<TattooZoneOption> All =
    [
        new("Torso", TattooZone.Torso),
        new("Cabeza", TattooZone.Head),
        new("Brazo izquierdo", TattooZone.LeftArm),
        new("Brazo derecho", TattooZone.RightArm),
        new("Pierna izquierda", TattooZone.LeftLeg),
        new("Pierna derecha", TattooZone.RightLeg),
    ];
}

// onChanged receives true when the change moves the tattoo to another tree group.
public sealed class TattooViewModel(Tattoo model, Action<TattooViewModel, bool> onChanged) : ObservableObject
{
    public Tattoo Model { get; } = model;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (SetProperty(Model.Name, value, Model, (t, v) => t.Name = v)) onChanged(this, false);
        }
    }

    public TattooZoneOption Zone
    {
        get => TattooZoneOption.All.First(z => z.Value == Model.Zone);
        set
        {
            if (value is not null && SetProperty(Model.Zone, value.Value, Model, (t, v) => t.Zone = v)) onChanged(this, true);
        }
    }

    public bool IsMale
    {
        get => Model.Genders.Contains(PedGender.Male);
        set => SetGender(PedGender.Male, value);
    }

    public bool IsFemale
    {
        get => Model.Genders.Contains(PedGender.Female);
        set => SetGender(PedGender.Female, value);
    }

    public decimal UvX { get => (decimal)Model.UvX; set => SetFloat(Model.UvX, value, (t, v) => t.UvX = v); }
    public decimal UvY { get => (decimal)Model.UvY; set => SetFloat(Model.UvY, value, (t, v) => t.UvY = v); }
    public decimal ScaleX { get => (decimal)Model.ScaleX; set => SetFloat(Model.ScaleX, value, (t, v) => t.ScaleX = v); }
    public decimal ScaleY { get => (decimal)Model.ScaleY; set => SetFloat(Model.ScaleY, value, (t, v) => t.ScaleY = v); }
    public decimal Rotation { get => (decimal)Model.Rotation; set => SetFloat(Model.Rotation, value, (t, v) => t.Rotation = v); }

    public string FileName => Path.GetFileName(Model.TexturePath);

    private void SetFloat(float current, decimal value, Action<Tattoo, float> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(current, (float)value, Model, apply, name)) onChanged(this, false);
    }

    private void SetGender(PedGender gender, bool enabled, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (Model.Genders.Contains(gender) == enabled) return;
        if (enabled) Model.Genders.Add(gender);
        else Model.Genders.Remove(gender);
        OnPropertyChanged(name);
        onChanged(this, false);
    }
}
