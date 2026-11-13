using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GTerm.Controls;

/// <summary>
/// Uma barra só, dividida entre até quatro séries na proporção de cada uma: de onde vieram os tokens
/// da sessão, no cartão do consumo. As cores vêm do tema, uma por série, na ordem das partes.
/// </summary>
public sealed class BarraEmpilhada : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PartesProperty =
        AvaloniaProperty.Register<BarraEmpilhada, IReadOnlyList<double>?>(nameof(Partes));

    public static readonly StyledProperty<IBrush?> Cor0Property = AvaloniaProperty.Register<BarraEmpilhada, IBrush?>(nameof(Cor0));
    public static readonly StyledProperty<IBrush?> Cor1Property = AvaloniaProperty.Register<BarraEmpilhada, IBrush?>(nameof(Cor1));
    public static readonly StyledProperty<IBrush?> Cor2Property = AvaloniaProperty.Register<BarraEmpilhada, IBrush?>(nameof(Cor2));
    public static readonly StyledProperty<IBrush?> Cor3Property = AvaloniaProperty.Register<BarraEmpilhada, IBrush?>(nameof(Cor3));
    public static readonly StyledProperty<IBrush?> TrilhoProperty = AvaloniaProperty.Register<BarraEmpilhada, IBrush?>(nameof(Trilho));

    static BarraEmpilhada() => AffectsRender<BarraEmpilhada>(PartesProperty, Cor0Property, Cor1Property, Cor2Property,
        Cor3Property, TrilhoProperty);

    public IReadOnlyList<double>? Partes { get => GetValue(PartesProperty); set => SetValue(PartesProperty, value); }
    public IBrush? Cor0 { get => GetValue(Cor0Property); set => SetValue(Cor0Property, value); }
    public IBrush? Cor1 { get => GetValue(Cor1Property); set => SetValue(Cor1Property, value); }
    public IBrush? Cor2 { get => GetValue(Cor2Property); set => SetValue(Cor2Property, value); }
    public IBrush? Cor3 { get => GetValue(Cor3Property); set => SetValue(Cor3Property, value); }

    /// <summary>O fundo da barra, que aparece inteiro quando não há nada a dividir.</summary>
    public IBrush? Trilho { get => GetValue(TrilhoProperty); set => SetValue(TrilhoProperty, value); }

    /// <summary>Onde cada parte começa e termina na largura dada. Parte sem valor não ocupa nada.</summary>
    public static IReadOnlyList<(double Inicio, double Largura)> Fatias(IReadOnlyList<double>? partes, double largura)
    {
        var fatias = new List<(double, double)>();
        var total = partes?.Where(p => p > 0).Sum() ?? 0;
        double x = 0;
        foreach (var parte in partes ?? Array.Empty<double>())
        {
            var l = total > 0 && parte > 0 ? largura * parte / total : 0;
            fatias.Add((x, l));
            x += l;
        }
        return fatias;
    }

    public override void Render(DrawingContext contexto)
    {
        var area = new Rect(Bounds.Size);
        if (area.Width <= 0 || area.Height <= 0) return;

        using var recorte = contexto.PushClip(new RoundedRect(area, area.Height / 2));
        if (Trilho is { } trilho) contexto.FillRectangle(trilho, area);

        var cores = new[] { Cor0, Cor1, Cor2, Cor3 };
        var fatias = Fatias(Partes, area.Width);
        for (var i = 0; i < fatias.Count && i < cores.Length; i++)
            if (fatias[i].Largura > 0 && cores[i] is { } cor)
                contexto.FillRectangle(cor, new Rect(fatias[i].Inicio, 0, fatias[i].Largura, area.Height));
    }
}

/// <summary>
/// Uma barra por dia, do mais antigo até hoje, na altura do valor contra o maior do período: o
/// consumo do projeto, no cartão do consumo. A de hoje tem cor própria, e o mouse em cima de uma
/// diz o dia e o valor (<see cref="Rotulos"/>).
/// </summary>
public sealed class BarrasPorDia : Control
{
    private const double Vao = 3;
    private const double Minima = 2;

    public static readonly StyledProperty<IReadOnlyList<long>?> ValoresProperty =
        AvaloniaProperty.Register<BarrasPorDia, IReadOnlyList<long>?>(nameof(Valores));

    public static readonly StyledProperty<IReadOnlyList<string>?> RotulosProperty =
        AvaloniaProperty.Register<BarrasPorDia, IReadOnlyList<string>?>(nameof(Rotulos));

    public static readonly StyledProperty<IBrush?> CorProperty = AvaloniaProperty.Register<BarrasPorDia, IBrush?>(nameof(Cor));
    public static readonly StyledProperty<IBrush?> CorDeHojeProperty = AvaloniaProperty.Register<BarrasPorDia, IBrush?>(nameof(CorDeHoje));
    public static readonly StyledProperty<IBrush?> TrilhoProperty = AvaloniaProperty.Register<BarrasPorDia, IBrush?>(nameof(Trilho));

    static BarrasPorDia() => AffectsRender<BarrasPorDia>(ValoresProperty, CorProperty, CorDeHojeProperty, TrilhoProperty);

    public IReadOnlyList<long>? Valores { get => GetValue(ValoresProperty); set => SetValue(ValoresProperty, value); }

    /// <summary>O que o mouse em cima de cada barra mostra, na ordem dos valores.</summary>
    public IReadOnlyList<string>? Rotulos { get => GetValue(RotulosProperty); set => SetValue(RotulosProperty, value); }

    public IBrush? Cor { get => GetValue(CorProperty); set => SetValue(CorProperty, value); }
    public IBrush? CorDeHoje { get => GetValue(CorDeHojeProperty); set => SetValue(CorDeHojeProperty, value); }

    /// <summary>A marca do dia sem consumo, rente ao chão.</summary>
    public IBrush? Trilho { get => GetValue(TrilhoProperty); set => SetValue(TrilhoProperty, value); }

    /// <summary>
    /// O retângulo de cada barra na área dada. O dia sem nada fica com a altura mínima, e o que
    /// teve algum nunca some: também tem a mínima.
    /// </summary>
    public static IReadOnlyList<Rect> Barras(IReadOnlyList<long>? valores, Size area)
    {
        var barras = new List<Rect>();
        if (valores is not { Count: > 0 } || area.Width <= 0 || area.Height <= 0) return barras;

        var largura = Math.Max(1, (area.Width - Vao * (valores.Count - 1)) / valores.Count);
        var maior = Math.Max(1, valores.Max());
        for (var i = 0; i < valores.Count; i++)
        {
            var altura = Math.Max(Minima, area.Height * valores[i] / maior);
            barras.Add(new Rect(i * (largura + Vao), area.Height - altura, largura, altura));
        }
        return barras;
    }

    public override void Render(DrawingContext contexto)
    {
        // transparente, mas pintado: é o que faz o mouse valer na área toda, e não só nas barras
        contexto.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var valores = Valores;
        var barras = Barras(valores, Bounds.Size);
        for (var i = 0; i < barras.Count; i++)
        {
            var cor = valores![i] <= 0 ? Trilho : i == barras.Count - 1 ? CorDeHoje ?? Cor : Cor;
            if (cor is not null) contexto.DrawRectangle(cor, null, barras[i], 2, 2);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Valores is not { Count: > 0 } valores || Rotulos is not { } rotulos) return;

        var passo = Bounds.Width / valores.Count;
        var i = passo > 0 ? Math.Clamp((int)(e.GetPosition(this).X / passo), 0, valores.Count - 1) : 0;
        if (i < rotulos.Count && !Equals(ToolTip.GetTip(this), rotulos[i])) ToolTip.SetTip(this, rotulos[i]);
    }
}
