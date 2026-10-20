using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyClothes.Core.Projects;
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

    // --snapshot out.png is a debug aid: saves the first rendered frame and exits.
    private static void ApplyCommandLine(MainWindow window, string[] args)
    {
        if (args.FirstOrDefault(a => a.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) is { } project)
            _ = window.ViewModel.OpenProjectFileAsync(project);

        var snapshot = Array.IndexOf(args, "--snapshot");
        if (snapshot < 0 || snapshot + 1 >= args.Length) return;
        var target = args[snapshot + 1];
        window.Viewport.SnapshotRequested = image =>
        {
            using (var file = File.Create(target)) PngWriter.Write(image, file);
            window.Close();
        };
    }
}
