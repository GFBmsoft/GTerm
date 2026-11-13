using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GTerm.Services;

/// <summary>
/// Barra de título escura no Windows 10. O Avalonia só pede isso ao sistema do Windows 11
/// (build 22000) em diante; no 10 a janela ficava com o tema escuro e a barra branca.
/// </summary>
public static class BarraDeTitulo
{
    private const int PrimeiroWindows11 = 22000;

    /// <summary>
    /// Atributo do DWM que liga a barra escura, ou null quando não é o caso: o número
    /// mudou de 19 para 20 no Windows 10 2004, antes do 1809 não existe, e do Windows 11
    /// em diante o Avalonia já cuida.
    /// </summary>
    public static int? Atributo(int build) => build switch
    {
        >= PrimeiroWindows11 => null,
        >= 18985 => 20,
        >= 17763 => 19,
        _ => null,
    };

    /// <summary>Aplica agora e a cada troca de tema da janela.</summary>
    public static void Acompanhar(Window janela)
    {
        if (!OperatingSystem.IsWindows()) return;

        // do Windows 11 em diante a barra aceita uma cor: a do fundo da janela, e as duas viram um
        // plano só, sem a faixa cinza do sistema em cima do app
        if (Environment.OSVersion.Version.Build >= PrimeiroWindows11)
        {
            Pintar(janela);
            janela.ActualThemeVariantChanged += (_, _) => Pintar(janela);
            return;
        }

        if (Atributo(Environment.OSVersion.Version.Build) is not { } atributo) return;

        Aplicar(janela, atributo);
        janela.ActualThemeVariantChanged += (_, _) => Aplicar(janela, atributo);
    }

    /// <summary>A cor da barra, no formato do Windows (0x00BBGGRR); null se o fundo não é uma cor cheia.</summary>
    public static int? CorDaBarra(IBrush? fundo) =>
        fundo is ISolidColorBrush { Color: { A: 255 } c } ? c.R | c.G << 8 | c.B << 16 : null;

    private static void Pintar(Window janela)
    {
        if (janela.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        if (CorDaBarra(janela.Background) is not { } cor) return;

        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cor, sizeof(int));
        }
        catch (Exception)
        {
            // cosmético, como embaixo
        }
    }

    private const int DWMWA_CAPTION_COLOR = 35;

    private static void Aplicar(Window janela, int atributo)
    {
        if (janela.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;

        try
        {
            var escuro = janela.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, atributo, ref escuro, sizeof(int)) != 0) return;

            // No Windows 10 a barra de uma janela já aberta só é repintada quando ela
            // perde ou ganha o foco. Avisar a moldura duas vezes força a repintura na
            // hora, e a segunda mensagem a deixa no estado em que estava.
            var ativa = GetForegroundWindow() == hwnd;
            SendMessage(hwnd, WM_NCACTIVATE, ativa ? 0 : 1, 0);
            SendMessage(hwnd, WM_NCACTIVATE, ativa ? 1 : 0, 0);
        }
        catch (Exception)
        {
            // cosmético: sem o DWM a janela abre com a barra padrão do sistema
        }
    }

    private const uint WM_NCACTIVATE = 0x0086;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, nint wParam, nint lParam);
}
