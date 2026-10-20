using Avalonia;

namespace EasyClothes.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Linux only (development under WSLg): GLX rejects Mesa's llvmpipe, EGL accepts it,
            // which keeps the OpenGL viewport testable without a GPU. Windows uses ANGLE regardless.
            .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] })
            .LogToTrace();
}
