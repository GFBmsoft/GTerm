using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaTerminal;
using GTerm.Controls;
using GTerm.Services;
using GTerm.ViewModels;

namespace GTerm.Views;

public partial class MainWindow : Window, IDialogService
{
    private readonly MainViewModel _vm;
    private bool _fechamentoConfirmado;

    // andando pela árvore com as setas: o teclado fica nela, não pula para o terminal
    private bool _pelasSetas;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(this);
        DataContext = _vm;

        // trocar de projeto é para digitar: o teclado vai ao terminal que apareceu
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Selecionado) or nameof(MainViewModel.Aviso)
                    or nameof(MainViewModel.SessaoAVista) && !_pelasSetas)
                Dispatcher.UIThread.Post(Focar, DispatcherPriority.Background);
        };

        // abrir ou retomar não muda de projeto, e o botão clicado some: sem isto o teclado
        // ficava sem dono até um clique no terminal
        _vm.ClaudeAberto += () => Dispatcher.UIThread.Post(Focar, DispatcherPriority.Background);

        Opened += (_, _) =>
        {
            Focar();
            _vm.CorrigirHooks();
            _ = _vm.VerificarAtualizacaoAsync(); // em segundo plano, no máximo uma consulta por dia
            _ = _vm.CarregarSessoesAsync(); // fora da tela: as Sessões abrem sem esperar pelos .jsonl
        };

        // atualização instalada: o aviso de que os terminais fecham já foi confirmado, e o
        // executável novo já está subindo. Sai direto, sem passar pelo fechamento da
        // janela: na 1.0.0.1 esse caminho deixou o app aberto, parado em "100%". Com o
        // processo morto o Windows fecha os pseudoconsoles, e os shells vão junto
        _vm.Sair = () => System.Environment.Exit(0);

        // um projeto que não está à vista terminou ou espera por você: com a janela atrás
        // de outra, o botão dela pisca na barra de tarefas até ela vir para a frente
        Activated += (_, _) => _vm.JanelaAtiva = true;
        Deactivated += (_, _) => _vm.JanelaAtiva = false;
        _vm.PedirAtencao += () =>
        {
            if (!IsActive && TryGetPlatformHandle()?.Handle is { } janela)
                Services.BarraDeTarefas.Piscar(janela);
        };

        // em túnel: o terminal consome o teclado todo, então a janela olha antes dele
        AddHandler(KeyDownEvent, AoTeclar, RoutingStrategies.Tunnel);

        var lista = this.FindControl<ListBox>("Lista")!;
        lista.AddHandler(KeyDownEvent, (_, e) =>
        {
            _pelasSetas = e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
            if (e.Key == Key.Enter && _vm.LinhaSelecionada is LinhaDoProjeto l)
            {
                l.Projeto.IniciarCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _vm.LinhaSelecionada is LinhaDaSessao s)
            {
                s.RetomarCommand.Execute(null);
                e.Handled = true;
            }
            // Alt+setas sobe e desce o favorito, como o arrastar
            else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down &&
                     _vm.LinhaSelecionada is LinhaDoProjeto { NosFavoritos: true } favorito)
            {
                _vm.MoverFavorito(favorito.Projeto, e.Key == Key.Up ? -1 : 1);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        ArrasteNaArvore.Habilitar(lista, _vm);
        lista.ContextRequested += AbrirMenuDaArvore;
        lista.AddHandler(KeyUpEvent, (_, _) => _pelasSetas = false, RoutingStrategies.Tunnel);
        lista.AddHandler(PointerPressedEvent, (_, _) => _pelasSetas = false, RoutingStrategies.Tunnel);
        lista.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is LinhaDoProjeto l) l.Projeto.IniciarCommand.Execute(null);
            else if ((e.Source as Control)?.DataContext is LinhaDaSessao s) s.RetomarCommand.Execute(null);
        };

        // o filtro em cima da árvore: Enter vai ao primeiro que bate, Esc limpa, seta para
        // baixo desce para a árvore
        var filtro = this.FindControl<TextBox>("CaixaDoFiltro")!;
        filtro.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    _vm.EscolherPrimeiro();
                    lista.Focus();
                    break;
                case Key.Escape:
                    _vm.Filtro = "";
                    break;
                case Key.Down:
                    lista.Focus();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        // a cor da aba, na paleta do GRepos: a mesma dos grupos
        var botaoDaCor = this.FindControl<Button>("CorDaAba")!;
        var paleta = new PaletaDeCores("A cor da aba deste projeto, que vale também no GTest quando é a tirada do nome.");
        var painelDaCor = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = paleta };
        painelDaCor.Opening += (_, _) => paleta.Marcar(_vm.AbaAtiva?.CorHex);
        paleta.Escolhida += hex =>
        {
            painelDaCor.Hide();
            if (_vm.AbaAtiva is { } aba) _vm.MudarCor(aba, hex);
        };
        botaoDaCor.Flyout = painelDaCor;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void AbrirMenuDaArvore(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control onde) return;
        MenuDe(onde.DataContext as LinhaDaArvore).ShowAt(onde, showAtPointer: true);
        e.Handled = true;
    }

    /// <summary>
    /// O menu do clique direito, montado na hora: os grupos para onde a linha pode ir mudam a
    /// cada momento, e um menu pronto em cada linha não os acompanharia. Fora das
    /// linhas, só criar um grupo.
    /// </summary>
    internal MenuFlyout MenuDe(LinhaDaArvore? linha)
    {
        var menu = new MenuFlyout();

        void Item(string rotulo, Action acao)
        {
            var item = new MenuItem { Header = rotulo };
            item.Click += (_, _) => acao();
            menu.Items.Add(item);
        }

        void Comando(string rotulo, System.Windows.Input.ICommand comando) =>
            menu.Items.Add(new MenuItem { Header = rotulo, Command = comando });

        void MoverPara(LinhaDaArvore l)
        {
            var mover = new MenuItem { Header = "Mover para" };

            var novo = new MenuItem { Header = "Novo grupo…" };
            novo.Click += async (_, _) =>
            {
                if (await _vm.NovoGrupoAsync(null) is { } id) _vm.MoverPara(l, id);
            };
            mover.Items.Add(novo);

            var destinos = _vm.DestinosDe(l);
            if (destinos.Count > 0) mover.Items.Add(new Separator());
            foreach (var d in destinos)
            {
                var item = new MenuItem { Header = d.Recuado };
                item.Click += (_, _) => _vm.MoverPara(l, d.Grupo.Id);
                mover.Items.Add(item);
            }
            menu.Items.Add(mover);
        }

        // num título ou fora das linhas: é por aqui que um grupo escondido volta, inclusive
        // com a árvore vazia
        void GruposAVista(GrupoDaArvore? titulo)
        {
            menu.Items.Add(new Separator());
            if (titulo is not null && titulo.Id != MainViewModel.GrupoResultados)
                Item("Ocultar este grupo", () => _vm.MostrarOuEsconderGrupo(titulo.Id));

            var lista = new MenuItem { Header = "Grupos à vista" };
            foreach (var g in _vm.GruposAVista())
            {
                var item = new MenuItem { Header = g.Nome, ToggleType = MenuItemToggleType.CheckBox, IsChecked = g.Marcado };
                item.Click += (_, _) => _vm.MostrarOuEsconderGrupo(g.Id);
                lista.Items.Add(item);
            }
            menu.Items.Add(lista);
        }

        switch (linha)
        {
            case GrupoDaArvore g when _vm.EhEditavel(g.Id):
                Item($"Novo subgrupo em {g.Titulo}…", () => _ = _vm.NovoGrupoAsync(g.Id));
                if (_vm.EhDoUsuario(g.Id))
                {
                    Item("Editar grupo… (nome, cor e onde fica)", () => _ = _vm.EditarGrupoAsync(g.Id));
                    menu.Items.Add(new Separator());
                    Item("Excluir grupo…", () => _ = _vm.ExcluirGrupoAsync(g.Id));
                }
                else
                    Item("Cor…", () => _ = _vm.EditarGrupoAsync(g.Id));
                GruposAVista(g);
                break;

            case LinhaDoProjeto { Projeto: var p } l:
                Comando("Abrir o Claude", p.AbrirClaudeCommand);
                Comando("Sessão nova", p.SessaoNovaCommand);
                if (p.EhDaBmsoft)
                {
                    Comando("Pedido do Mantis…", p.PedidoCommand);
                    Comando("Pull request", p.PullRequestCommand);
                }
                Comando("Sem skill", p.SemSkillCommand);
                menu.Items.Add(new Separator());
                Comando(p.RotuloDoFavorito, p.FavoritarCommand);
                if (l.NosFavoritos)
                {
                    menu.Items.Add(new MenuItem { Header = "Mover para cima", Command = l.SubirCommand, InputGesture = new KeyGesture(Key.Up, KeyModifiers.Alt) });
                    menu.Items.Add(new MenuItem { Header = "Mover para baixo", Command = l.DescerCommand, InputGesture = new KeyGesture(Key.Down, KeyModifiers.Alt) });
                }
                menu.Items.Add(new Separator());
                MoverPara(l);
                Item(_vm.EstaOculto(p) ? "Mostrar na árvore" : "Ocultar da árvore", () => _vm.AlternarOculto(p));
                if (p.TemTerminal)
                {
                    menu.Items.Add(new Separator());
                    Comando("Encerrar o Claude", p.EncerrarCommand);
                }
                break;

            case LinhaDaSessao s:
                Comando(s.RotuloDeRetomar, s.RetomarCommand);
                Comando(s.RotuloDaEstrela, s.FavoritarCommand);
                menu.Items.Add(new Separator());
                MoverPara(s);
                if (_vm.ConversaEmGrupo(s)) Item("Tirar do grupo", () => _vm.TirarDoGrupo(s));
                Item(_vm.EstaOculta(s) ? "Mostrar na árvore" : "Ocultar da árvore", () => _vm.AlternarOculta(s));
                break;

            default:
                Item("Novo grupo…", () => _ = _vm.NovoGrupoAsync(null));
                GruposAVista(linha as GrupoDaArvore);
                break;
        }
        return menu;
    }

    private void Focar() =>
        this.GetVisualDescendants().OfType<TerminalControl>().FirstOrDefault(t => t.IsVisible)?.Focus();

    private void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.Tab)
            _vm.SelecionarVizinho(shift ? -1 : 1);
        else if (!shift && e.Key is >= Key.D1 and <= Key.D9)
            _vm.SelecionarPorNumero(e.Key - Key.D1 + 1);
        else if (!shift && e.Key is >= Key.NumPad1 and <= Key.NumPad9)
            _vm.SelecionarPorNumero(e.Key - Key.NumPad1 + 1);
        else if (shift && e.Key == Key.P) // com Shift: Ctrl+P sozinho é do shell e do Claude
        {
            var filtro = this.FindControl<TextBox>("CaixaDoFiltro")!;
            filtro.Focus();
            filtro.SelectAll();
        }
        else
            return;

        e.Handled = true;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_fechamentoConfirmado || !_vm.HaTerminalRodando)
        {
            _vm.EncerrarTudo();
            return;
        }

        // fechar a janela mata todos os shells de uma vez: é o engano que o app existe para evitar
        e.Cancel = true;
        if (!await ConfirmAsync("Fechar o GTerm",
                "Há terminais abertos. Fechar encerra todos eles e o que estiver rodando.")) return;
        _fechamentoConfirmado = true;
        Close();
    }

    // ------------------------------------------------------------ IDialogService

    public async Task<bool> ConfirmAsync(string title, string message) =>
        await new ConfirmWindow(title, message).ShowDialog<bool>(this);

    public async Task<string?> PromptAsync(string title, string label) =>
        await new PromptWindow(title, label).ShowDialog<string?>(this);

    public async Task<Preferencias?> PreferenciasAsync(Preferencias atuais, IReadOnlyList<string> contas) =>
        await new PreferenciasWindow(atuais, contas).ShowDialog<Preferencias?>(this);

    public async Task<GrupoEscolhido?> GrupoAsync(string titulo, string nome, string cor, string? paiId,
        IReadOnlyList<GrupoNaArvore>? pais) =>
        await new GrupoWindow(titulo, nome, cor, paiId, pais).ShowDialog<GrupoEscolhido?>(this);
}
