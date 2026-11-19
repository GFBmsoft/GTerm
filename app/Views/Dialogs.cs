using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using GTerm.Controls;
using GTerm.Services;
using GTerm.ViewModels;

namespace GTerm.Views;

/// <summary>Base dos diálogos: moldura, título e rodapé de botões no mesmo padrão.</summary>
public abstract class DialogWindow : Window
{
    protected DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
    }

    protected static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Margin = new Thickness(0, 0, 0, 4),
        TextWrapping = TextWrapping.Wrap,
        Classes = { "faint" },
    };

    protected static StackPanel Field(string label, Control input) => new()
    {
        Margin = new Thickness(0, 0, 0, 10),
        Children = { Label(label), input },
    };

    protected static Button Btn(string text, bool primary = false)
    {
        var b = new Button { MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        Rotular(b, text);
        return b;
    }

    /// <summary>O glifo e a cor de cada ação dos diálogos, como no GRepos: a mesma ação sai igual em todos.</summary>
    private static readonly Dictionary<string, (string Glifo, string Cor)> Acoes = new()
    {
        ["Cancelar"] = (Glifos.Fechar, "cinza"),
        ["Fechar"] = (Glifos.Fechar, "cinza"),
        ["Confirmar"] = (Glifos.Confirmar, "azul"),
        ["Salvar"] = (Glifos.Salvar, "azul"),
        ["Instalar"] = (Glifos.Instalar, "verde"),
        ["Remover"] = (Glifos.Excluir, "vermelho"),
        ["Corrigir"] = (Glifos.Corrigir, "laranja"),
    };

    private static readonly string[] Cores = { "azul", "verde", "verdeAgua", "roxo", "laranja", "amarelo", "vermelho", "cinza" };

    /// <summary>
    /// Põe o texto no botão, com o glifo e a cor da ação; texto fora da tabela vai sozinho. Pode ser chamado de
    /// novo no mesmo botão (o das contas troca entre Instalar, Remover e Corrigir): a cor anterior sai.
    /// </summary>
    protected static void Rotular(Button b, string texto)
    {
        foreach (var cor in Cores) b.Classes.Remove(cor);
        if (!Acoes.TryGetValue(texto, out var acao))
        {
            b.Content = texto;
            return;
        }

        b.Classes.Add(acao.Cor);
        b.Content = new StackPanel
        {
            Classes = { "comIcone" },
            Children =
            {
                new TextBlock { Classes = { "glifo" }, Text = acao.Glifo },
                new TextBlock { Text = texto },
            },
        };
    }

    protected void Compose(string heading, IEnumerable<Control> body, IEnumerable<Control> footer,
        bool rodapeCentralizado = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 14.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        foreach (var c in body) panel.Children.Add(c);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = rodapeCentralizado ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        foreach (var c in footer) foot.Children.Add(c);
        panel.Children.Add(foot);

        Content = new Border { Padding = new Thickness(16), Child = panel };
    }
}

// ------------------------------------------------------------------ confirmar

public sealed class ConfirmWindow : DialogWindow
{
    public ConfirmWindow(string title, string message) : base(title, 420)
    {
        var no = Btn("Cancelar");
        var yes = Btn("Confirmar", true);
        no.Click += (_, _) => Close(false);
        yes.Click += (_, _) => Close(true);

        Compose(title,
            new Control[]
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) },
            },
            new[] { no, yes },
            rodapeCentralizado: true);

        // Enter não confirma: quem chega aqui por engano sai sem estrago
        Opened += (_, _) => no.Focus();
    }
}

// --------------------------------------------------------------------- texto

public sealed class PromptWindow : DialogWindow
{
    public PromptWindow(string title, string label) : base(title, 380)
    {
        var input = new TextBox();
        var cancel = Btn("Cancelar");
        var ok = Btn("Confirmar", true);

        cancel.Click += (_, _) => Close(null);
        ok.Click += (_, _) => Close(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Close(input.Text);
        };

        Compose(title, new Control[] { Field(label, input) }, new[] { cancel, ok });
        Opened += (_, _) => input.Focus();
    }
}

// ------------------------------------------------------------------- grupo

/// <summary>Nome, cor e onde o grupo fica, como no GRepos. Devolve null quando o usuário cancela.</summary>
public sealed class GrupoWindow : DialogWindow
{
    /// <param name="pais">
    /// Grupos que podem conter este, em ordem de árvore. Nulo é uma base do cia: o nome é o da
    /// pasta e ela não muda de lugar, então só a cor aparece.
    /// </param>
    public GrupoWindow(string titulo, string nome, string cor, string? paiId, IReadOnlyList<GrupoNaArvore>? pais)
        : base(titulo, 420)
    {
        var escolhida = GroupPalette.Normalizar(cor) ?? GroupPalette.Padrao;

        var nomeBox = new TextBox { Text = nome, Watermark = "Ex.: Módulos fiscais" };
        var hexBox = new TextBox { Text = escolhida, Width = 96 };

        // a amostra abre o painel com a paleta inteira; as bolinhas acima são o atalho
        // para as cores de grupo, que aparecem bem nos dois temas
        var seletor = new SeletorDeCor { Cor = escolhida, Margin = new Thickness(8, 0, 0, 0) };

        var swatches = new WrapPanel();
        var botoes = new List<Button>();

        void Selecionar(string valor, bool atualizaHex)
        {
            escolhida = valor;
            if (seletor.Cor != valor) seletor.Cor = valor;
            if (atualizaHex) hexBox.Text = valor;
            foreach (var b in botoes)
                b.BorderThickness = new Thickness(
                    string.Equals(b.Tag as string, valor, StringComparison.OrdinalIgnoreCase) ? 3 : 0);
        }

        foreach (var c in GroupPalette.Cores)
        {
            var b = new Button
            {
                Classes = { "amostra" },
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.Parse(c)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(c == escolhida ? 3 : 0),
                Padding = new Thickness(0),
                Tag = c,
            };
            b.Click += (_, _) => Selecionar(c, true);
            botoes.Add(b);
            swatches.Children.Add(b);
        }

        seletor.Escolhida += valor => Selecionar(GroupPalette.Normalizar(valor) ?? escolhida, true);

        // a cor acompanha o que se digita: assim que o código vira uma cor válida, a
        // amostra e as bolinhas já mudam, sem precisar sair do campo
        hexBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (GroupPalette.Normalizar(hexBox.Text) is { } v && v != escolhida) Selecionar(v, false);
        };
        hexBox.LostFocus += (_, _) => hexBox.Text = escolhida; // inválido volta ao que valia; válido fica por extenso

        var linhaCor = new StackPanel { Orientation = Orientation.Horizontal };
        linhaCor.Children.Add(hexBox);
        linhaCor.Children.Add(seletor);

        // "dentro de": é o que faz pasta e subpasta. O primeiro item é o nível principal
        var opcoes = pais ?? Array.Empty<GrupoNaArvore>();
        var dentroDe = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        dentroDe.ItemsSource = new[] { "(nível principal)" }.Concat(opcoes.Select(p => p.Recuado)).ToList();
        dentroDe.SelectedIndex = Math.Max(0, opcoes.ToList().FindIndex(p => p.Grupo.Id == paiId) + 1);

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nomeBox.Text)) return;
            var pai = dentroDe.SelectedIndex > 0 ? opcoes[dentroDe.SelectedIndex - 1].Grupo.Id : null;
            Close(new GrupoEscolhido(nomeBox.Text!.Trim(), escolhida, pai));
        };

        var corpo = new List<Control>();
        if (pais is not null)
        {
            corpo.Add(Field("Nome do grupo", nomeBox));
            corpo.Add(Field("Dentro de", dentroDe));
        }
        corpo.Add(Field("Cor", swatches));
        corpo.Add(Field("…ou escolha na paleta, ou informe o código", linhaCor));

        Compose(titulo, corpo, new[] { cancelar, salvar });

        Opened += (_, _) => nomeBox.Focus();
    }
}

// ------------------------------------------------------------- configurações

/// <summary>
/// As configurações, no padrão das do GNotas (a EV05 dele): as seções numa lista à esquerda e a
/// escolhida num cartão à direita. A versão em uso, a procura de atualização e as notas da versão
/// ficam no Sobre; no pé da árvore só a engrenagem. Devolve null quando o usuário cancela.
/// </summary>
public sealed class ConfiguracoesWindow : DialogWindow
{
    public static readonly string[] Secoes = { "Terminal", "Avisos", "Claude Code", "GNotas", "Sobre" };
    private static readonly string[] GlifosDasSecoes = { Glifos.Terminal, Glifos.Avisos, Glifos.Codigo, Glifos.Notas, Glifos.Sobre };

    private readonly ListBox _lista;
    private readonly ScrollViewer _conteudo;
    private readonly Dictionary<string, Control> _montadas = new();
    private readonly IReadOnlyList<string> _contas;
    private readonly MainViewModel? _vm;

    // o que o Salvar lê: criados todos de uma vez, valem mesmo sem a seção deles ter sido aberta
    private readonly AutoCompleteBox _fonte;
    private readonly NumericUpDown _tamanho;
    private readonly TextBox _painel;
    private readonly CheckBox _avisar;

    /// <param name="vm">O da janela: a versão e a atualização do Sobre, e a pasta do GNotas. Null nos testes da tela.</param>
    public ConfiguracoesWindow(Preferencias atuais, IReadOnlyList<string> contas, MainViewModel? vm = null, string? secao = null)
        : base("Configurações", 780)
    {
        _contas = contas;
        _vm = vm;
        SizeToContent = SizeToContent.Manual;
        Height = 560;
        MinWidth = 640;
        MinHeight = 440;
        CanResize = true;
        // a janela no plano escuro, como a principal, e a seção num cartão um tom acima
        this.Bind(BackgroundProperty, new DynamicResourceExtension("BgPanel"));

        // digitar filtra a lista; nome fora dela também vale (a fonte pode ser instalada depois)
        _fonte = new AutoCompleteBox
        {
            Text = atuais.Fonte,
            ItemsSource = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            Watermark = "Padrão (" + MainViewModel.FontePadrao.Split(',')[0] + ")",
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _tamanho = new NumericUpDown
        {
            Value = (decimal)atuais.Tamanho,
            Minimum = 8,
            Maximum = 32,
            Increment = 1,
            FormatString = "0",
            Width = 130,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _painel = new TextBox
        {
            Text = atuais.PainelComando,
            Watermark = @"Ex.: & 'C:\Scripts\claude-stats.ps1' -Slim -Live",
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _avisar = new CheckBox
        {
            Content = "Avisar quando um projeto fora da vista terminar ou esperar por mim",
            IsChecked = atuais.Avisar,
        };

        _lista = new ListBox { Classes = { "secoes" }, ItemsSource = Secoes, Margin = new Thickness(0, 6, 10, 0) };
        _lista.ItemTemplate = new FuncDataTemplate<string>((s, _) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Height = 30,
            Margin = new Thickness(10, 0),
            Children =
            {
                new TextBlock { Text = GlifosDasSecoes[Array.IndexOf(Secoes, s)], Classes = { "glifo", "faint" }, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = s, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        _conteudo = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _lista.SelectionChanged += (_, _) => Mostrar(_lista.SelectedItem as string);

        var cartao = new Border { Child = _conteudo, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true };
        cartao.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Bg"));
        cartao.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Border"));

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) => Close(new Preferencias(
            (_fonte.Text ?? "").Trim(),
            (double)(_tamanho.Value ?? (decimal)atuais.Tamanho),
            (_painel.Text ?? "").Trim(),
            _avisar.IsChecked == true));
        var rodape = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelar, salvar },
        };

        var grade = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(10, 10, 12, 12),
        };
        Grid.SetColumn(cartao, 1);
        Grid.SetRow(rodape, 1);
        Grid.SetColumnSpan(rodape, 2);
        grade.Children.Add(_lista);
        grade.Children.Add(cartao);
        grade.Children.Add(rodape);
        Content = grade;

        _lista.SelectedItem = Secoes.Contains(secao) ? secao : Secoes[0];
    }

    /// <summary>A seção aberta (os testes trocam por aqui).</summary>
    public string? Secao
    {
        get => _lista.SelectedItem as string;
        set => _lista.SelectedItem = value;
    }

    private void Mostrar(string? secao)
    {
        if (secao is null) return;
        if (!_montadas.TryGetValue(secao, out var c))
            _montadas[secao] = c = secao switch
            {
                "Terminal" => Pagina("Terminal",
                    Campo("Fonte", _fonte, "Para os ícones do prompt aparecerem, escolha uma Nerd Font. Em branco, usa a padrão."),
                    Campo("Tamanho", _tamanho, "Também muda com Ctrl+= e Ctrl+- (ou Ctrl+roda) em cima do terminal; Ctrl+0 volta ao padrão."),
                    Campo("Painel lateral", _painel, "Um script de PowerShell, que roda na pasta e na conta do projeto, sem carregar o perfil. " +
                                                     "Em branco, o botão do painel some.")),
                "Avisos" => Pagina("Avisos",
                    Campo("Projeto fora da vista", _avisar, "O nome do projeto fica em destaque na árvore, o título da janela diz quantos " +
                                                             "esperam e, com o GTerm atrás de outra janela, o botão dele pisca na barra de tarefas.")),
                "Claude Code" => ClaudeCode(),
                "GNotas" => DoGNotas(),
                _ => Sobre(),
            };
        _conteudo.Content = c;
        _conteudo.Offset = default;
    }

    // ------------------------------------------------------------ as peças, como as do GNotas

    private static StackPanel Pagina(string titulo, params Control[] filhos)
    {
        var p = new StackPanel { Spacing = 20, Margin = new Thickness(26, 22, 26, 26) };
        p.Children.Add(new TextBlock { Text = titulo, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var f in filhos) p.Children.Add(f);
        return p;
    }

    /// <summary>Um campo: o nome, o controle e, embaixo, a explicação (apagada).</summary>
    private static StackPanel Campo(string nome, Control controle, string? ajuda = null)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = nome, FontSize = 13, FontWeight = FontWeight.SemiBold });
        p.Children.Add(controle);
        if (ajuda is not null) p.Children.Add(Ajuda(ajuda));
        return p;
    }

    private static TextBlock Ajuda(string texto) => new()
    {
        Text = texto,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 520,
        HorizontalAlignment = HorizontalAlignment.Left,
        Classes = { "faint" },
    };

    private Control ClaudeCode()
    {
        var contas = new StackPanel { Spacing = 0 };
        foreach (var c in _contas) contas.Children.Add(LinhaDaConta(c));
        return Pagina("Claude Code",
            Campo("Contas", contas,
                "Instala hooks no settings.json da conta para o ponto das abas e da árvore mostrar o estado real do Claude: " +
                "rodando, esperando por você ou concluído. Vale para as sessões abertas depois. Uma cópia do arquivo original " +
                "fica ao lado dele (settings.json.antes-do-gterm)."));
    }

    private Control DoGNotas()
    {
        var pasta = _vm?.PastaDasNotas ?? GNotas.Pasta(null);
        var caminho = new TextBlock
        {
            Text = pasta ?? "GNotas não encontrado nesta máquina",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Classes = { pasta is null ? "faint" : "mono" },
        };
        return Pagina("GNotas",
            Campo("Pasta das notas", caminho,
                $"O que falta em cada projeto fica no caderno {GNotas.Caderno} do GNotas, numa nota com o nome da pasta dele " +
                $"({GNotas.Caderno}\\GTerm.md), em Melhorias e Bugs. O quadro do projeto mostra quantos itens faltam, e o botão Notas " +
                $"abre o Claude na /gnotas para fazê-los: ele marca {GNotas.Feito} no que ficou pronto e tira da galeria as imagens " +
                "dos itens feitos. A pasta é a das configurações do GNotas."));
    }

    private Control Sobre()
    {
        var versao = new TextBlock { Text = _vm?.Versao ?? "GTerm", FontSize = 14, FontWeight = FontWeight.SemiBold };

        // procurar e, achando versão nova, atualizar: quem atualiza é o mesmo clique do convite no pé
        // da árvore, que pergunta antes, e reiniciar encerra os terminais. A janela fecha antes
        var botao = new Button { Classes = { "pilula", "azul" }, HorizontalAlignment = HorizontalAlignment.Left };
        var situacao = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "faint" } };
        void Rotulo()
        {
            var nova = _vm?.TemAtualizacao == true;
            botao.Classes.Set("azul", !nova);
            botao.Classes.Set("verde", nova);
            botao.Content = new StackPanel
            {
                Classes = { "comIcone" },
                Children =
                {
                    new TextBlock { Classes = { "glifo" }, Text = nova ? Glifos.Instalar : Glifos.Atualizar },
                    new TextBlock { Text = _vm?.RotuloDeAtualizar ?? "Procurar atualização" },
                },
            };
            situacao.Text = _vm?.SituacaoDaVersao ?? "";
        }
        Rotulo();
        if (_vm is { } vm)
        {
            vm.PropertyChanged += Mudou;
            Closed += (_, _) => vm.PropertyChanged -= Mudou;
            void Mudou(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainViewModel.SituacaoDaVersao) or nameof(MainViewModel.RotuloDeAtualizar)) Rotulo();
            }
            botao.Click += (_, _) =>
            {
                if (!vm.TemAtualizacao)
                {
                    vm.ProcurarAtualizacaoCommand.Execute(null);
                    return;
                }
                Close(null);
                vm.AtualizacaoCommand.Execute(null);
            };
        }

        var cabecalho = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                versao,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { botao, situacao } },
                Ajuda($"As configurações ficam em {WorkspaceStore.FilePath}."),
            },
        };
        return Pagina("Sobre", cabecalho, Campo("Notas da versão", NotasDaVersaoNaTela()));
    }

    /// <summary>O que mudou em cada versão, da mais nova para a mais antiga. Vem dentro do app.</summary>
    private static Control NotasDaVersaoNaTela()
    {
        var notas = NotasDaVersao.Carregar();
        var lista = new StackPanel { Spacing = 14 };
        if (notas.Count == 0) lista.Children.Add(Ajuda("Sem notas nesta versão."));
        foreach (var n in notas)
        {
            var itens = new StackPanel { Spacing = 4 };
            itens.Children.Add(new SelectableTextBlock { Text = n.Rotulo, FontSize = 12.5, FontWeight = FontWeight.SemiBold });
            foreach (var linha in n.Notas.Replace("**", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                itens.Children.Add(new SelectableTextBlock
                {
                    Text = linha.StartsWith("- ", StringComparison.Ordinal) ? "•  " + linha[2..] : linha,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                    HorizontalAlignment = HorizontalAlignment.Left,
                });
            lista.Children.Add(itens);
        }
        return lista;
    }

    /// <summary>
    /// Uma conta: onde fica, se os avisos estão instalados e o botão que troca isso na hora.
    /// </summary>
    private static Control LinhaDaConta(string conta)
    {
        var situacao = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
        var botao = new Button { MinWidth = 84 };
        var exe = Environment.ProcessPath ?? "GTerm.exe";

        void Atualizar(string? erro = null)
        {
            var instalado = ClaudeHooks.Instalado(conta);
            // instalados por outro GTerm (outra pasta, um build de teste): o Claude chama o
            // executável errado, e o que resolve é instalar de novo a partir deste
            var deOutro = instalado && ClaudeHooks.Desatualizado(conta, exe);
            Rotular(botao, deOutro ? "Corrigir" : instalado ? "Remover" : "Instalar");
            situacao.Text = erro ?? (deOutro ? "aponta para outro GTerm" : instalado ? "instalado" : "não instalado");
            situacao.Classes.Set("faint", erro is null);
        }

        botao.Click += (_, _) =>
        {
            try
            {
                if (ClaudeHooks.Instalado(conta) && !ClaudeHooks.Desatualizado(conta, exe)) ClaudeHooks.Remover(conta);
                else ClaudeHooks.Instalar(conta, exe);
                Atualizar();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Atualizar("não foi possível: " + ex.Message);
            }
        };
        Atualizar();

        // quem está logado na conta em cima, a pasta dela embaixo: só a pasta não diz de quem é
        var quem = ClaudeHooks.Identificar(conta);
        var identificacao = string.Join("  ·  ",
            new[] { quem.Nome, quem.Email, quem.Plano }.Where(t => t is not null));

        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        var caminho = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        if (identificacao.Length > 0)
            caminho.Children.Add(new TextBlock { Text = identificacao, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        caminho.Children.Add(new TextBlock
        {
            Text = conta,
            Classes = { "mono", "faint" },
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(situacao, 1);
        Grid.SetColumn(botao, 2);
        linha.Children.Add(caminho);
        linha.Children.Add(situacao);
        linha.Children.Add(botao);
        return linha;
    }
}
