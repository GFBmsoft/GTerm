using Avalonia;
using Avalonia.Media;

namespace AvaloniaTerminal;

/// <summary>
/// GTerm: os "Block Elements" do Unicode (U+2580 a U+259F) desenhados como retângulos, do
/// jeito que o Windows Terminal faz. Pelo glifo da fonte o meio-bloco não tem exatamente
/// meia célula nem encosta no vizinho, e arte em pixels (o pokémon do perfil, barras de
/// progresso) sai com frestas e degraus.
/// </summary>
internal static class BlockElements
{
    // quadrantes de U+2596 a U+259F: 1 em cima à esquerda, 2 em cima à direita,
    // 4 embaixo à esquerda, 8 embaixo à direita
    private static readonly int[] Quadrantes = [4, 8, 1, 13, 9, 7, 11, 2, 6, 14];

    public static bool Eh(int codePoint) => codePoint is >= 0x2580 and <= 0x259F;

    /// <param name="celula">A célula já alinhada a pixels inteiros.</param>
    /// <param name="alinhar">Leva uma coordenada ao pixel inteiro mais próximo.</param>
    public static void Desenhar(DrawingContext context, IBrush pincel, int codePoint, Rect celula, Func<double, double> alinhar)
    {
        // a célula em oitavos: é a unidade em que esses caracteres são definidos
        Rect R(int x0, int y0, int x1, int y1)
        {
            var esquerda = alinhar(celula.X + (celula.Width * x0 / 8));
            var topo = alinhar(celula.Y + (celula.Height * y0 / 8));
            var direita = alinhar(celula.X + (celula.Width * x1 / 8));
            var base_ = alinhar(celula.Y + (celula.Height * y1 / 8));
            return new Rect(esquerda, topo, Math.Max(direita - esquerda, 0), Math.Max(base_ - topo, 0));
        }

        switch (codePoint)
        {
            case 0x2580: // metade de cima
                context.FillRectangle(pincel, R(0, 0, 8, 4));
                break;
            case >= 0x2581 and <= 0x2588: // de 1/8 embaixo até o bloco cheio
                context.FillRectangle(pincel, R(0, 8 - (codePoint - 0x2580), 8, 8));
                break;
            case >= 0x2589 and <= 0x258F: // de 7/8 à esquerda até 1/8
                context.FillRectangle(pincel, R(0, 0, 0x2590 - codePoint, 8));
                break;
            case 0x2590: // metade da direita
                context.FillRectangle(pincel, R(4, 0, 8, 8));
                break;
            case >= 0x2591 and <= 0x2593: // sombreados: 25, 50 e 75%
                using (context.PushOpacity((codePoint - 0x2590) * 0.25))
                {
                    context.FillRectangle(pincel, R(0, 0, 8, 8));
                }
                break;
            case 0x2594: // 1/8 em cima
                context.FillRectangle(pincel, R(0, 0, 8, 1));
                break;
            case 0x2595: // 1/8 à direita
                context.FillRectangle(pincel, R(7, 0, 8, 8));
                break;
            default:
                var q = Quadrantes[codePoint - 0x2596];
                if ((q & 1) != 0) context.FillRectangle(pincel, R(0, 0, 4, 4));
                if ((q & 2) != 0) context.FillRectangle(pincel, R(4, 0, 8, 4));
                if ((q & 4) != 0) context.FillRectangle(pincel, R(0, 4, 4, 8));
                if ((q & 8) != 0) context.FillRectangle(pincel, R(4, 4, 8, 8));
                break;
        }
    }
}
