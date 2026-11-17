using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Point = Avalonia.Point;

namespace AvaloniaTerminal;

// GTerm: arquivo solto no terminal e Ctrl+clique em link, como no Windows Terminal. Tudo que
// não veio do AvaloniaTerminal original para isso está aqui; no TerminalControl.cs ficam só as
// chamadas, marcadas com "GTerm:".

/// <summary>
/// O texto em volta da célula clicada. <paramref name="Line"/> é a linha inteira, com as que o
/// terminal quebrou por falta de largura juntas de novo, e <paramref name="Column"/> a posição
/// do clique nela. <paramref name="Above"/> e <paramref name="Below"/> são as linhas vizinhas:
/// um programa que quebra a linha ele mesmo (o Claude) deixa o resto de um caminho na de baixo.
/// </summary>
public sealed record TerminalLinkContext(string Line, int Column, string Above, string Below);

/// <summary>
/// Pergunta ao app se há link debaixo do mouse. O terminal só entrega o texto: o que é link, e
/// como se abre, quem sabe é o app (um caminho relativo depende da pasta do terminal).
/// </summary>
public sealed class TerminalLinkEventArgs(RoutedEvent routedEvent, TerminalLinkContext context, bool open)
    : RoutedEventArgs(routedEvent)
{
    public TerminalLinkContext Context { get; } = context;

    /// <summary>true no Ctrl+clique (abrir); false com o mouse passando (só dizer se há link).</summary>
    public bool Open { get; } = open;

    /// <summary>O que o app achou ali; null quando não é link.</summary>
    public string? Target { get; set; }
}

public partial class TerminalControl
{
    public static readonly RoutedEvent<TerminalLinkEventArgs> LinkRequestedEvent =
        RoutedEvent.Register<TerminalControl, TerminalLinkEventArgs>(nameof(LinkRequested), RoutingStrategies.Bubble);

    public event EventHandler<TerminalLinkEventArgs>? LinkRequested
    {
        add => AddHandler(LinkRequestedEvent, value);
        remove => RemoveHandler(LinkRequestedEvent, value);
    }

    private static Cursor? _linkCursor;

    // a última célula perguntada com o Ctrl apertado, para não perguntar de novo a cada pixel
    private (int Col, int Row)? _linkCell;
    private bool _overLink;

    /// <summary>
    /// Os caminhos como o Windows Terminal os cola: separados por espaço, entre aspas os que
    /// têm espaço ou algo que o shell leria como comando.
    /// </summary>
    public static string PathsToPaste(IEnumerable<string> paths) =>
        string.Join(" ", paths.Select(p => p.IndexOfAny([' ', '\t', '&', '(', ')', ';', ',', '\'', '`']) >= 0 ? "\"" + p + "\"" : p));

    private void SetUpFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnFileDragOver);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        e.DragEffects = Model != null && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
#pragma warning restore CS0618
        e.Handled = true;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
#pragma warning restore CS0618
        if (Model == null || paths is not { Count: > 0 })
        {
            return;
        }

        Focus();
        Paste(PathsToPaste(paths));
        e.Handled = true;
    }

    /// <summary>O link na célula do ponto, segundo o app; com <paramref name="open"/>, o app também o abre.</summary>
    private string? FindLink(Point surfacePosition, bool open)
    {
        if (Model == null || !TryGetCellFromPoint(surfacePosition, includeOutsideBounds: false, out var col, out var row) ||
            Model.LinkContextAt(row, col) is not { } context)
        {
            return null;
        }

        var args = new TerminalLinkEventArgs(LinkRequestedEvent, context, open);
        RaiseEvent(args);
        return args.Target;
    }

    /// <summary>Ctrl+clique num link: o app abre, e o clique não vira seleção nem vai ao programa.</summary>
    private bool TryOpenLink(PointerPressedEventArgs e)
    {
        return e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
               e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
               FindLink(e.GetPosition(_surface), open: true) != null;
    }

    /// <summary>A mãozinha com o Ctrl apertado em cima de um link, como no Windows Terminal.</summary>
    private void UpdateLinkCursor(PointerEventArgs e)
    {
        if (_selectionPointerCaptured || !e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            !TryGetCellFromPoint(e.GetPosition(_surface), includeOutsideBounds: false, out var col, out var row))
        {
            ClearLinkCursor();
            return;
        }

        if (_linkCell == (col, row))
        {
            return;
        }

        _linkCell = (col, row);
        SetLinkCursor(FindLink(e.GetPosition(_surface), open: false) != null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearLinkCursor();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            ClearLinkCursor();
        }
    }

    private void ClearLinkCursor()
    {
        _linkCell = null;
        SetLinkCursor(false);
    }

    private void SetLinkCursor(bool overLink)
    {
        if (overLink == _overLink)
        {
            return;
        }

        _overLink = overLink;
        Cursor = overLink ? _linkCursor ??= new Cursor(StandardCursorType.Hand) : null;
    }
}

public partial class TerminalControlModel
{
    /// <summary>O texto em volta de uma célula da tela, para o Ctrl+clique.</summary>
    public TerminalLinkContext? LinkContextAt(int row, int col)
    {
        var buffer = Terminal.Buffer;
        var line = buffer.YDisp + row;
        if (line < 0 || line >= buffer.Lines.Length)
        {
            return null;
        }

        // as linhas que o terminal quebrou por falta de largura voltam a ser uma só
        var first = line;
        while (first > 0 && buffer.GetLine(first)?.IsWrapped == true)
        {
            first--;
        }

        var last = line;
        while (last + 1 < buffer.Lines.Length && buffer.GetLine(last + 1)?.IsWrapped == true)
        {
            last++;
        }

        var text = new StringBuilder();
        var column = col;
        for (var i = first; i <= last; i++)
        {
            var part = buffer.GetLine(i)?.TranslateToString(trimRight: i == last) ?? string.Empty;
            if (i < line)
            {
                column += part.Length;
            }

            text.Append(part);
        }

        string Row(int i) => i >= 0 && i < buffer.Lines.Length
            ? buffer.GetLine(i)?.TranslateToString(trimRight: true) ?? string.Empty
            : string.Empty;

        return new TerminalLinkContext(text.ToString(), column, Row(first - 1), Row(last + 1));
    }
}
