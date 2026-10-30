using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GTerm.ViewModels;

namespace GTerm.Views;

/// <summary>
/// Arrastar na árvore: um projeto ou uma conversa para um grupo ou para o meio de outros, um
/// grupo para dentro de outro, um projeto para Ocultos. Quem decide o que cabe e faz a mudança
/// é o <see cref="MainViewModel"/>; aqui ficam o gesto e a marca do destino.
///
/// Dentro dos Favoritos a linha acompanha o mouse, e a ordem nova (a do Ctrl+número e do [n]
/// do cia) é gravada ao soltar. Começa só depois de o mouse andar um pouco, para o clique
/// continuar sendo clique; na estrela e no ▶ não começa. O título do grupo é um botão, mas
/// arrasta: tomar a captura dele cancela o clique.
/// </summary>
public static class ArrasteNaArvore
{
    /// <summary>Distância antes de o clique virar arraste: um clique tremido não arrasta.</summary>
    private const double Folga = 6;

    /// <summary>Perto da beirada a lista rola, para alcançar o que está fora da vista.</summary>
    private const double Beirada = 24;
    private const double PassoDaRolagem = 14;

    public static void Habilitar(ListBox lista, MainViewModel vm)
    {
        LinhaDaArvore? pega = null;
        DestinoDoArraste? destino = null;
        var inicio = default(Point);
        var arrastando = false;

        void Terminar(bool soltou)
        {
            if (arrastando)
            {
                arrastando = false;
                lista.Cursor = null;
                vm.MarcarDestino(null);
                if (!soltou) vm.DesfazerFavorito();
                else if (destino is not null) vm.Soltar(pega!, destino);
                else vm.SoltarFavorito();
            }
            pega = null;
            destino = null;
        }

        lista.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pega = null;
            if (!e.GetCurrentPoint(lista).Properties.IsLeftButtonPressed) return;

            var botao = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
            if (botao is not null && !botao.Classes.Contains("grupo")) return;
            if ((e.Source as StyledElement)?.DataContext is not LinhaDaArvore linha || !vm.Arrastavel(linha)) return;

            pega = linha;
            inicio = e.GetPosition(lista);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        lista.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (pega is null) return;

            var aqui = e.GetPosition(lista);
            if (!arrastando)
            {
                if (Math.Abs(aqui.Y - inicio.Y) < Folga) return;
                arrastando = true;
                e.Pointer.Capture(lista);
                lista.Cursor = new Cursor(StandardCursorType.DragMove);
            }

            Rolar(lista, aqui.Y);
            var (alvo, depois) = LinhaEm(lista, aqui.Y);
            destino = vm.OndeCai(pega, alvo, depois);
            vm.MarcarDestino(destino);

            if (destino is null && pega is LinhaDoProjeto { NosFavoritos: true } favorito &&
                FavoritoEm(lista, aqui.Y) is { } outro)
                vm.ArrastarFavorito(favorito.Projeto, outro);
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        lista.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            // num clique a captura Ã© do botÃ£o: tirÃ¡-la aqui cancelaria o clique
            var arrastou = arrastando;
            Terminar(soltou: true);
            if (!arrastou) return;
            e.Handled = true;
            e.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        lista.PointerCaptureLost += (_, _) => Terminar(soltou: false);
    }

    private static void Rolar(ListBox lista, double y)
    {
        if (lista.Scroll is not { } rolagem) return;
        var passo = y < Beirada ? -PassoDaRolagem : y > lista.Bounds.Height - Beirada ? PassoDaRolagem : 0;
        if (passo != 0) rolagem.Offset = rolagem.Offset.WithY(Math.Max(0, rolagem.Offset.Y + passo));
    }

    /// <summary>A linha na altura do mouse, e se ele está na metade de baixo dela.</summary>
    private static (LinhaDaArvore? Linha, bool Depois) LinhaEm(ListBox lista, double y)
    {
        foreach (var item in lista.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (!item.IsVisible || item.DataContext is not LinhaDaArvore linha ||
                item.TranslatePoint(default, lista) is not { } topo) continue;
            if (y >= topo.Y && y < topo.Y + item.Bounds.Height) return (linha, y >= topo.Y + item.Bounds.Height / 2);
        }
        return (null, false);
    }

    /// <summary>
    /// O favorito na altura do mouse; acima do primeiro é o primeiro, abaixo do último é o
    /// último, para dar para levar ao começo e ao fim sem mirar.
    /// </summary>
    private static ProjetoViewModel? FavoritoEm(ListBox lista, double y)
    {
        var linhas = lista.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsVisible && i.DataContext is LinhaDoProjeto { NosFavoritos: true })
            .Select(i => (Projeto: ((LinhaDoProjeto)i.DataContext!).Projeto,
                Fim: (i.TranslatePoint(default, lista)?.Y ?? 0) + i.Bounds.Height))
            .OrderBy(x => x.Fim)
            .ToList();
        if (linhas.Count == 0) return null;
        return linhas.FirstOrDefault(x => y < x.Fim).Projeto ?? linhas[^1].Projeto;
    }
}
