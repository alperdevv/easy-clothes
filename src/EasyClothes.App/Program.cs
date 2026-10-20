using Avalonia;
using Avalonia.Logging;

namespace EasyClothes.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Linux/WSLg only: GLX rejects Mesa's llvmpipe and EGL doesn't, so the viewport
            // stays testable without a GPU. Windows always uses ANGLE.
            .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] });

        return Environment.GetEnvironmentVariable("EASYCLOTHES_LOG") is { Length: > 0 } logPath
            ? builder.LogToTextWriter(new StreamWriter(logPath) { AutoFlush = true }, LogEventLevel.Information)
            : builder.LogToTrace();
    }
}
