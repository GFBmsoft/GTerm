using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GTerm.Models;
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
        var b = new Button { Content = text, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        return b;
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

// -------------------------------------------------------------------- projeto

/// <summary>Dados do projeto. Devolve null quando o usuário cancela.</summary>
public sealed class ProjetoWindow : DialogWindow
{
    public ProjetoWindow(Projeto projeto) : base("Editar projeto", 480)
    {
        var nomeBox = new TextBox { Text = projeto.Nome };
        var pastaBox = new TextBox { Text = projeto.Pasta };
        var comandoBox = new TextBox { Text = projeto.Comando, Watermark = "Ex.: cia Financeiro -SemMenu -SemPainel" };
        var contaBox = new TextBox { Text = projeto.Conta, Watermark = @"Em branco = conta normal. Ex.: C:\Users\voce\.claude-bm" };
        var shellBox = new ComboBox
        {
            ItemsSource = Shells.Todos,
            SelectedItem = Shells.Achar(projeto.Shell),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nomeBox.Text) || string.IsNullOrWhiteSpace(pastaBox.Text)) return;
            Close(new ProjetoEditado(nomeBox.Text!.Trim(), pastaBox.Text!.Trim().Trim('"'),
                (shellBox.SelectedItem as Shell)?.Id ?? Shells.Padrao,
                (comandoBox.Text ?? "").Trim(), (contaBox.Text ?? "").Trim().Trim('"')));
        };

        Compose("Editar projeto",
            new Control[]
            {
                Field("Nome", nomeBox),
                Field("Pasta", pastaBox),
                Field("Shell", shellBox),
                Field("Comando ao abrir", comandoBox),
                Field("Outra conta do Claude (pasta de configuração)", contaBox),
                Label("Só para projetos que usam uma conta diferente da sua conta normal. Para a conta normal, deixe em branco."),
                Label("Pasta, shell, comando e conta passam a valer quando o terminal for reiniciado."),
            },
            new[] { cancelar, salvar });

        Opened += (_, _) => nomeBox.Focus();
    }
}

// ---------------------------------------------------------------------- busca

/// <summary>
/// Vai a um projeto pelo nome: digitar filtra, as setas andam na lista e o Enter escolhe.
/// Devolve null quando o usuário desiste.
/// </summary>
public sealed class BuscaWindow : DialogWindow
{
    public BuscaWindow(IReadOnlyList<Projeto> projetos) : base("Ir para o projeto", 420)
    {
        var caixa = new TextBox { Watermark = "Nome ou pasta do projeto" };
        var lista = new ListBox
        {
            MaxHeight = 300,
            Margin = new Thickness(0, 8, 0, 0),
            ItemsSource = projetos,
            SelectedIndex = 0,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Projeto>((p, _) => new StackPanel
            {
                Margin = new Thickness(2, 3),
                Children =
                {
                    new TextBlock { Text = p?.Nome },
                    new TextBlock { Text = p?.Pasta, FontSize = 11, Classes = { "faint" }, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            }),
        };

        caixa.TextChanged += (_, _) =>
        {
            var termo = (caixa.Text ?? "").Trim();
            lista.ItemsSource = projetos
                .Where(p => p.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase) ||
                            p.Pasta.Contains(termo, StringComparison.CurrentCultureIgnoreCase))
                // o que começa pelo que foi digitado vem na frente
                .OrderBy(p => !p.Nome.StartsWith(termo, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            lista.SelectedIndex = 0;
        };

        // o teclado fica na caixa: as setas e o Enter chegam aqui e mexem na lista
        caixa.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Down:
                    lista.SelectedIndex = Math.Min(lista.SelectedIndex + 1, lista.ItemCount - 1);
                    break;
                case Avalonia.Input.Key.Up:
                    lista.SelectedIndex = Math.Max(lista.SelectedIndex - 1, 0);
                    break;
                case Avalonia.Input.Key.Enter:
                    Close(lista.SelectedItem as Projeto);
                    break;
                case Avalonia.Input.Key.Escape:
                    Close(null);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        lista.DoubleTapped += (_, _) =>
        {
            if (lista.SelectedItem is Projeto p) Close(p);
        };

        var cancelar = Btn("Cancelar");
        cancelar.Click += (_, _) => Close(null);

        Compose("Ir para o projeto", new Control[] { caixa, lista }, new[] { cancelar });
        Opened += (_, _) => caixa.Focus();
    }
}

// --------------------------------------------------------------- preferências

/// <summary>Preferências do aplicativo. Devolve null quando o usuário cancela.</summary>
public sealed class PreferenciasWindow : DialogWindow
{
    public PreferenciasWindow(Preferencias atuais, IReadOnlyList<string> contas) : base("Preferências", 520)
    {
        // digitar filtra a lista; nome fora dela também vale (a fonte pode ser instalada depois)
        var fonteBox = new AutoCompleteBox
        {
            Text = atuais.Fonte,
            ItemsSource = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            Watermark = "Padrão (" + MainViewModel.FontePadrao.Split(',')[0] + ")",
        };
        var tamanhoBox = new NumericUpDown
        {
            Value = (decimal)atuais.Tamanho,
            Minimum = 8,
            Maximum = 32,
            Increment = 1,
            FormatString = "0",
            Width = 130,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var cores = new Dictionary<string, string>(
            atuais.Cores ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var painelBox = new TextBox
        {
            Text = atuais.PainelComando,
            Watermark = @"Ex.: & 'C:\Scripts\claude-stats.ps1' -Slim -Live",
        };

        var avisarBox = new CheckBox
        {
            Content = "Avisar quando um projeto fora da vista terminar ou esperar por mim",
            IsChecked = atuais.Avisar,
        };

        var notas = Btn("Notas da versão");
        notas.Click += (_, _) => new NotasWindow().ShowDialog(this);

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) => Close(new Preferencias(
            (fonteBox.Text ?? "").Trim(),
            (double)(tamanhoBox.Value ?? (decimal)atuais.Tamanho),
            (painelBox.Text ?? "").Trim(),
            cores,
            avisarBox.IsChecked == true));

        var corpo = new List<Control>
        {
            Field("Fonte do terminal", fonteBox),
            Label("Para os ícones do prompt aparecerem, escolha uma Nerd Font. Em branco, usa a padrão."),
            new Border { Height = 10 },
            Field("Tamanho", tamanhoBox),
            Field("Painel lateral (script de PowerShell)", painelBox),
            Label("Roda na pasta e na conta do projeto, sem carregar o perfil. Em branco, o botão do painel some."),
            avisarBox,
            Label("O nome do projeto fica em destaque na barra lateral, o título da janela diz quantos esperam e, " +
                  "com o GTerm atrás de outra janela, o botão dele pisca na barra de tarefas."),
            new Border { Height = 14 },
            new TextBlock { Text = "CONTAS DO CLAUDE CODE", Classes = { "sectionTitle" }, Margin = new Thickness(0, 0, 0, 6) },
            Label("A cor ao lado de cada conta pinta o grupo dela na barra lateral e os projetos que a usam. " +
                  "Clique nela para trocar."),
            Label("Instala hooks no settings.json da conta para o ponto da sidebar mostrar o estado real " +
                  "do Claude: rodando, esperando por você ou concluído. Vale para as sessões abertas depois. " +
                  "Uma cópia do arquivo original fica ao lado dele (settings.json.antes-do-gterm)."),
        };
        corpo.AddRange(contas.Select(c => LinhaDaConta(c, cores)));

        Compose("Preferências", corpo, new[] { notas, cancelar, salvar });
    }

    /// <summary>
    /// Uma conta: a cor dela, onde fica, se os avisos estão instalados e o botão que troca
    /// isso na hora. A cor só vale ao salvar as preferências.
    /// </summary>
    private static Control LinhaDaConta(string conta, Dictionary<string, string> cores)
    {
        var cor = new Button
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(cor, "Cor da conta na barra lateral");

        var paleta = new WrapPanel { MaxWidth = 170 };
        var flyout = new Flyout { Content = paleta };
        cor.Flyout = flyout;

        void Pintar()
        {
            var atual = GroupPalette.Normalizar(cores.GetValueOrDefault(conta)) ?? GroupPalette.Padrao;
            cor.Background = new SolidColorBrush(Color.Parse(atual));
            foreach (var b in paleta.Children.OfType<Button>())
                b.BorderThickness = new Thickness(b.Tag as string == atual ? 3 : 0);
        }

        foreach (var c in GroupPalette.Cores)
        {
            var b = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.Parse(c)),
                BorderBrush = Brushes.White,
                Padding = new Thickness(0),
                Tag = c,
            };
            b.Click += (_, _) =>
            {
                cores[conta] = c;
                Pintar();
                flyout.Hide();
            };
            paleta.Children.Add(b);
        }
        Pintar();

        var situacao = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
        var botao = new Button { MinWidth = 84 };
        var exe = Environment.ProcessPath ?? "GTerm.exe";

        void Atualizar(string? erro = null)
        {
            var instalado = ClaudeHooks.Instalado(conta);
            // instalados por outro GTerm (outra pasta, um build de teste): o Claude chama o
            // executável errado, e o que resolve é instalar de novo a partir deste
            var deOutro = instalado && ClaudeHooks.Desatualizado(conta, exe);
            botao.Content = deOutro ? "Corrigir" : instalado ? "Remover" : "Instalar";
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

        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 8) };
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
        Grid.SetColumn(caminho, 1);
        Grid.SetColumn(situacao, 2);
        Grid.SetColumn(botao, 3);
        linha.Children.Add(cor);
        linha.Children.Add(caminho);
        linha.Children.Add(situacao);
        linha.Children.Add(botao);
        return linha;
    }
}
// ----------------------------------------------------------- notas da versão

/// <summary>O que mudou em cada versão, da mais nova para a mais antiga. Vem dentro do app.</summary>
public sealed class NotasWindow : DialogWindow
{
    public NotasWindow() : base("Notas da versão", 560)
    {
        var notas = NotasDaVersao.Carregar();
        var texto = notas.Count == 0
            ? "Sem notas nesta versão."
            : string.Join("\n\n", notas.Select(n => n.Rotulo + "\n" + n.Notas.Replace("**", "")));

        var fechar = Btn("Fechar", true);
        fechar.Click += (_, _) => Close();

        Compose("Notas da versão",
            new Control[]
            {
                new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new SelectableTextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) },
                },
            },
            new[] { fechar });
    }
}