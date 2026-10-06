using Avalonia;
using GTerm.Services;

namespace GTerm;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        // chamado pelos hooks do Claude Code a cada evento: grava o estado e sai, sem janela
        if (ClaudeHooks.Avisar(args)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
