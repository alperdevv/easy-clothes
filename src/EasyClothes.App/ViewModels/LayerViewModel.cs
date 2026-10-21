using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EasyClothes.Core.Textures.Editing;

namespace EasyClothes.App.ViewModels;

// Wraps one layer of the document; every change is reported to the editor so it lands in the history.
public sealed partial class LayerViewModel(Layer model, Action<string, string> changed) : ObservableObject
{
    public static IReadOnlyList<string> BlendNames { get; } =
        ["Normal", "Multiplicar", "Trama", "Superponer", "Luz suave", "Oscurecer", "Aclarar", "Sumar", "Diferencia", "Color"];

    public Layer Model { get; } = model;
    public Guid Id => Model.Id;

    public string Kind => Model switch
    {
        PixelLayer => "Píxeles",
        ImageLayer => "Imagen",
        FillLayer => "Relleno",
        _ => "Ajuste",
    };

    public bool IsPixels => Model is PixelLayer;
    public bool IsImage => Model is ImageLayer;
    public bool IsFill => Model is FillLayer;
    public bool IsAdjustment => Model is AdjustmentLayer;
    public bool CanRasterize => Model is ImageLayer or FillLayer;

    public string Name
    {
        get => Model.Name;
        set => Set(() => Model.Name = string.IsNullOrWhiteSpace(value) ? Model.Name : value.Trim(), "Renombrar capa");
    }

    public bool Visible
    {
        get => Model.Visible;
        set => Set(() => Model.Visible = value, value ? "Mostrar capa" : "Ocultar capa", merge: false);
    }

    public bool Locked
    {
        get => Model.Locked;
        set => Set(() => Model.Locked = value, value ? "Bloquear capa" : "Desbloquear capa", merge: false);
    }

    public double Opacity
    {
        get => Model.Opacity * 100;
        set => Set(() => Model.Opacity = (float)Math.Clamp(value, 0, 100) / 100, "Opacidad");
    }

    public int BlendIndex
    {
        get => (int)Model.Blend;
        set => Set(() => Model.Blend = value < 0 ? Model.Blend : (BlendMode)value, "Modo de fusión");
    }

    public double Size
    {
        get => Model is ImageLayer i ? i.Scale * 100 : 0;
        set => Set(() => ((ImageLayer)Model).Scale = (float)Math.Clamp(value, 1, 400) / 100, "Escalar capa");
    }

    public double Rotation
    {
        get => Model is ImageLayer i ? i.Rotation : 0;
        set => Set(() => ((ImageLayer)Model).Rotation = (float)value, "Rotar capa");
    }

    public Color FillColor
    {
        get => Rgb(Model is FillLayer f ? f.Rgb : 0);
        set => Set(() => ((FillLayer)Model).Rgb = value.ToUInt32() & 0xFFFFFF, "Color de relleno");
    }

    public double Hue
    {
        get => Adjustment?.Hue ?? 0;
        set => Set(() => Adjustment!.Hue = (float)value, "Tono");
    }

    public double Saturation
    {
        get => (Adjustment?.Saturation ?? 0) * 100;
        set => Set(() => Adjustment!.Saturation = (float)value / 100, "Saturación");
    }

    public double Brightness
    {
        get => (Adjustment?.Brightness ?? 0) * 100;
        set => Set(() => Adjustment!.Brightness = (float)value / 100, "Brillo");
    }

    public double Contrast
    {
        get => (Adjustment?.Contrast ?? 0) * 100;
        set => Set(() => Adjustment!.Contrast = (float)value / 100, "Contraste");
    }

    public double TintStrength
    {
        get => (Adjustment?.TintStrength ?? 0) * 100;
        set => Set(() => Adjustment!.TintStrength = (float)value / 100, "Tinte");
    }

    public Color TintColor
    {
        get => Rgb(Adjustment?.TintRgb ?? 0xFFFFFF);
        set => Set(() => Adjustment!.TintRgb = value.ToUInt32() & 0xFFFFFF, "Color del tinte");
    }

    private AdjustmentLayer? Adjustment => Model as AdjustmentLayer;

    private static Color Rgb(uint rgb) => Color.FromUInt32(0xFF000000 | rgb);

    // Drags on the same slider of the same layer merge into one history step.
    private void Set(Action apply, string label, bool merge = true, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        apply();
        OnPropertyChanged(property);
        changed(label, merge ? $"{property}:{Id}" : "");
    }
}

public sealed record HistoryItemViewModel(string Label, bool IsUndone);
