using Avalonia;
using Avalonia.Headless;
using EasyClothes.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace EasyClothes.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
