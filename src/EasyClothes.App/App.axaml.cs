using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyClothes.App.Viewer;
using EasyClothes.Core.Textures;

namespace EasyClothes.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            window.Opened += (_, _) => ApplyCommandLine(window, desktop.Args ?? []);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Opens files given on the command line. Debug aid: <c>--wireframe</c> switches shading and
    /// <c>--snapshot out.png</c> saves the first rendered frame and exits.
    /// </summary>
    private static void ApplyCommandLine(MainWindow window, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--wireframe")
                window.Viewport.Shading = ShadingMode.SolidWithWireframe;
            else if (arg == "--snapshot" && i + 1 < args.Length)
            {
                var target = args[++i];
                window.Viewport.SnapshotRequested = image =>
                {
                    using (var file = File.Create(target)) PngWriter.Write(image, file);
                    window.Close();
                };
            }
            else if (arg.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                window.LoadModel(arg);
            else if (arg.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                window.LoadTexture(arg);
        }
    }
}
