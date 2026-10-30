using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace GTerm.Views;

/// <summary>
/// Item de menu com o glifo na cor da ação, como as pílulas (retomar verde, encerrar
/// vermelho). <paramref name="cor"/> é uma das classes de cor do tema: azul, verde, roxo,
/// laranja, amarelo, vermelho ou cinza.
/// </summary>
public static class ItemDeMenu
{
    public static MenuItem Novo(string texto, string glifo, string cor) => new()
    {
        Header = texto,
        Icon = new TextBlock { Text = glifo, Classes = { "glifo", cor } },
    };

    /// <summary>Item com a bolinha da cor no lugar do glifo: um grupo, como as cores do GRemote.</summary>
    public static MenuItem ComBolinha(string texto, IBrush cor) => new()
    {
        Header = texto,
        Icon = new Ellipse { Width = 10, Height = 10, Fill = cor },
    };
}
