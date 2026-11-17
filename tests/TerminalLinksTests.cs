using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaTerminal;
using GTerm.Services;
using Xunit;

namespace GTerm.Tests;

/// <summary>
/// Arquivo solto no terminal, colar avisando o programa e Ctrl+clique em endereço ou caminho,
/// como no Windows Terminal.
/// </summary>
public class TerminalLinksTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "gterm-links-" + Guid.NewGuid().ToString("n"));

    public TerminalLinksTests()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "app", "Views"));
        Directory.CreateDirectory(Path.Combine(_pasta, "com espaço"));
        File.WriteAllText(Path.Combine(_pasta, "app", "Views", "MainWindow.axaml.cs"), "");
        File.WriteAllText(Path.Combine(_pasta, "com espaço", "depois.png"), "");
        File.WriteAllText(Path.Combine(_pasta, "antes-todas.png"), "");
        File.WriteAllText(Path.Combine(_pasta, "CLAUDE.md"), "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    private string? Clicar(string linha, string ondeClica, string acima = "", string abaixo = "") =>
        LinksDoTerminal.Resolver(new TerminalLinkContext(linha, linha.IndexOf(ondeClica, StringComparison.Ordinal) + 1, acima, abaixo), _pasta);

    [Fact]
    public void CaminhoCompletoNoMeioDaFrase()
    {
        var antes = Path.Combine(_pasta, "antes-todas.png");

        // a pontuação da frase não entra, nem o desenho do Claude em volta
        Assert.Equal(antes, Clicar($"  ⎿  Gravei a imagem em {antes}.", "antes-todas"));
        Assert.Equal(antes, Clicar($"● Antes: {antes}, depois: x", "antes-todas"));
        Assert.Null(Clicar($"● Antes: {antes}, depois: x", "Antes:"));
        Assert.Equal(antes, Clicar($"veja `{antes}`", "todas"));
    }

    [Fact]
    public void RelativoParteDaPastaDoTerminalECortaALinha()
    {
        var arquivo = Path.Combine(_pasta, "app", "Views", "MainWindow.axaml.cs");

        Assert.Equal(arquivo, Clicar("em app/Views/MainWindow.axaml.cs:42 o botão", "Views"));
        Assert.Equal(arquivo, Clicar(@"em app\Views\MainWindow.axaml.cs:42:7.", "MainWindow"));
        Assert.Equal(Path.Combine(_pasta, "CLAUDE.md"), Clicar("leia o CLAUDE.md antes", "CLAUDE"));

        // uma palavra solta que por acaso é uma pasta do projeto não é link, e o que não existe também não
        Assert.Null(Clicar("o app abre", "app"));
        Assert.Null(Clicar("em app/Views/Nada.cs:3", "Nada"));
        Assert.Null(Clicar("uma frase qualquer", "frase"));
    }

    [Fact]
    public void NomeComEspacoSemAspas()
    {
        var depois = Path.Combine(_pasta, "com espaço", "depois.png");
        Assert.Equal(depois, Clicar($"Depois: {depois} (novo)", "com"));
        Assert.Equal(depois, Clicar($"Depois: \"{depois}\"", "depois.png"));
    }

    /// <summary>O Claude quebra a linha ele mesmo: o resto do caminho fica na de baixo, depois do recuo.</summary>
    [Fact]
    public void CaminhoQuebradoPeloProgramaJuntaComAVizinha()
    {
        var antes = Path.Combine(_pasta, "antes-todas.png");
        var corte = antes.Length - 6;
        var cima = "  Antes: " + antes[..corte];
        var baixo = "  " + antes[corte..] + " e depois";

        Assert.Equal(antes, LinksDoTerminal.Resolver(new TerminalLinkContext(cima, cima.Length - 2, "", baixo), _pasta));
        Assert.Equal(antes, LinksDoTerminal.Resolver(new TerminalLinkContext(baixo, 3, cima, ""), _pasta));
    }

    [Fact]
    public void EnderecoSemOParenteseDoMarkdown()
    {
        Assert.Equal("https://github.com/onurozcan/omux",
            Clicar("veja [o repo](https://github.com/onurozcan/omux).", "github"));
        Assert.Equal("https://pt.wikipedia.org/wiki/Teste_(software)",
            Clicar("em https://pt.wikipedia.org/wiki/Teste_(software), por exemplo", "wiki"));
        Assert.Null(Clicar("https://", "https"));
    }

    [Fact]
    public void CaminhosColadosComoNoWindowsTerminal()
    {
        Assert.Equal(@"C:\a\b.png ""C:\com espaço\c.png"" ""C:\x&y\d.txt""",
            TerminalControl.PathsToPaste([@"C:\a\b.png", @"C:\com espaço\c.png", @"C:\x&y\d.txt"]));
    }

    /// <summary>
    /// Colar avisa o programa que é colagem quando ele pediu (modo 2004): é assim que o Claude
    /// reconhece o caminho de uma imagem. Sem o pedido, vai como antes.
    /// </summary>
    [AvaloniaFact]
    public void ColarVaiEntreColchetesQuandoOProgramaPede()
    {
        var modelo = new TerminalControlModel(new TerminalOptions { Cols = 40, Rows = 5 });
        var terminal = new TerminalControl { Model = modelo };
        var enviado = new StringBuilder();
        modelo.UserInput += dados => enviado.Append(Encoding.UTF8.GetString(dados));

        terminal.Paste("um\ndois");
        Assert.Equal("um\ndois", enviado.ToString());

        enviado.Clear();
        modelo.Feed("\x1b[?2004h");
        terminal.Paste("um\x1b[201~\ndois");
        Assert.Equal("\x1b[200~um[201~\ndois\x1b[201~", enviado.ToString());
    }

    /// <summary>Arquivo solto no terminal vira o caminho colado, entre aspas se tiver espaço; texto solto não.</summary>
    [AvaloniaFact]
    public void SoltarArquivoColaOCaminho()
    {
        var modelo = new TerminalControlModel(new TerminalOptions { Cols = 60, Rows = 5 });
        var terminal = new TerminalControl { Model = modelo };
        var janela = new Window { Width = 600, Height = 200, Content = terminal };
        var enviado = new StringBuilder();
        modelo.UserInput += dados => enviado.Append(Encoding.UTF8.GetString(dados));
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        modelo.Feed("\x1b[?2004h");

        var antes = Path.Combine(_pasta, "antes-todas.png");
        var depois = Path.Combine(_pasta, "com espaço", "depois.png");
#pragma warning disable CS0618
        var arquivos = new DataObject();
        arquivos.Set(DataFormats.Files, new[] { Arquivo(antes), Arquivo(depois) });
        var texto = new DataObject();
        texto.Set(DataFormats.Text, "só texto");

        // o que o terminal respondeu a cada passo: é o que o Windows mostra no cursor
        var respostas = new List<DragDropEffects>();
        janela.AddHandler(DragDrop.DragEnterEvent, (_, e) => respostas.Add(e.DragEffects), handledEventsToo: true);
        janela.AddHandler(DragDrop.DragOverEvent, (_, e) => respostas.Add(e.DragEffects), handledEventsToo: true);

        var meio = new Point(300, 100);
        janela.DragDrop(meio, RawDragEventType.DragEnter, texto, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.DragLeave, texto, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.DragEnter, arquivos, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.DragOver, arquivos, DragDropEffects.Copy);
        janela.DragDrop(meio, RawDragEventType.Drop, arquivos, DragDropEffects.Copy);
#pragma warning restore CS0618

        Assert.Equal(new[] { DragDropEffects.None, DragDropEffects.Copy, DragDropEffects.Copy }, respostas);
        Assert.Equal($"\x1b[200~{antes} \"{depois}\"\x1b[201~", enviado.ToString());
        Assert.Same(terminal, janela.FocusManager!.GetFocusedElement());
        janela.Close();
    }

    // o arquivo do Explorer como o Avalonia o entrega; a classe dele é interna, e a interface não se implementa fora
    private static IStorageItem Arquivo(string caminho) =>
        (IStorageItem)Activator.CreateInstance(
            typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!,
            new FileInfo(caminho))!;

    /// <summary>O texto em volta do clique junta as linhas que o terminal quebrou por falta de largura.</summary>
    [AvaloniaFact]
    public void LinhaQuebradaPeloTerminalVoltaInteira()
    {
        var modelo = new TerminalControlModel(new TerminalOptions { Cols = 10, Rows = 5 });
        modelo.Feed("antes\r\nC:\\pasta\\comprida\\x.png\r\ndepois");

        var contexto = modelo.LinkContextAt(row: 2, col: 3)!;
        Assert.Equal("C:\\pasta\\comprida\\x.png", contexto.Line);
        Assert.Equal(13, contexto.Column);
        Assert.Equal("antes", contexto.Above);
        Assert.Equal("depois", contexto.Below);
        Assert.Null(modelo.LinkContextAt(row: 99, col: 0));
    }

    /// <summary>Ctrl+clique pergunta ao app o que há ali e não vira seleção; sem o Ctrl, é um clique comum.</summary>
    [AvaloniaFact]
    public void CtrlCliquePedeOLinkAoApp()
    {
        var modelo = new TerminalControlModel(new TerminalOptions { Cols = 60, Rows = 5 });
        var terminal = new TerminalControl { Model = modelo };
        var janela = new Window { Width = 600, Height = 200, Content = terminal };
        var pedidos = new List<TerminalLinkEventArgs>();
        janela.AddHandler(TerminalControl.LinkRequestedEvent, (_, e) =>
        {
            pedidos.Add(e);
            e.Target = LinksDoTerminal.Resolver(e.Context, _pasta);
        });
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        var antes = Path.Combine(_pasta, "antes-todas.png");
        modelo.Feed(antes);
        Dispatcher.UIThread.RunJobs();

        janela.MouseDown(new Point(3, 3), MouseButton.Left);
        janela.MouseUp(new Point(3, 3), MouseButton.Left);
        Assert.Empty(pedidos);

        janela.MouseDown(new Point(3, 3), MouseButton.Left, RawInputModifiers.Control);
        janela.MouseUp(new Point(3, 3), MouseButton.Left, RawInputModifiers.Control);
        var pedido = Assert.Single(pedidos, p => p.Open);
        Assert.Equal(antes, pedido.Target);
        Assert.False(modelo.HasSelection);
        janela.Close();
    }
}
