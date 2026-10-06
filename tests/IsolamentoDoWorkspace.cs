using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using GTerm.Services;

[assembly: AvaloniaTestApplication(typeof(GTerm.Tests.TestAppBuilder))]

namespace GTerm.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Trava o workspace dos testes numa pasta temporária **antes de qualquer teste rodar**:
/// não existe caminho de código que alcance o %APPDATA%\GTerm real. Mesma lição do GRepos,
/// onde um teste já apagou o workspace do usuário.
/// </summary>
internal static class IsolamentoDoWorkspace
{
    [ModuleInitializer]
    internal static void Ativar()
    {
        // GTERM_HOME venceria o desvio: um teste rodado de um shell com ela definida
        // gravaria na pasta que ela aponta
        Environment.SetEnvironmentVariable("GTERM_HOME", null);

        var raiz = Path.Combine(Path.GetTempPath(), "gterm-testes-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(raiz);
        WorkspaceStore.PastaPadraoDeTeste = raiz;
    }
}
