using System;
using System.Runtime.InteropServices;

namespace GTerm.Services;

/// <summary>
/// O botão do app na barra de tarefas piscando: é como o Windows chama a atenção para uma
/// janela que não está na frente, sem tomar o foco de onde o usuário está digitando.
/// </summary>
public static class BarraDeTarefas
{
    private const uint FLASHW_TRAY = 0x2;
    private const uint FLASHW_TIMERNOFG = 0xC; // pisca até a janela vir para a frente

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    public static void Piscar(IntPtr janela)
    {
        if (janela == IntPtr.Zero || !OperatingSystem.IsWindows()) return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = janela,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
        };
        FlashWindowEx(ref info);
    }
}
