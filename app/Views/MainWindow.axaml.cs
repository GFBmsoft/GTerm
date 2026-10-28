using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaTerminal;
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
            if (e.PropertyName is nameof(MainViewModel.Selecionado) or nameof(MainViewModel.Aviso) && !_pelasSetas)
                Dispatcher.UIThread.Post(Focar, DispatcherPriority.Background);
        };
        Opened += (_, _) =>
        {
            Focar();
            _vm.CorrigirHooks();
            _ = _vm.VerificarAtualizacaoAsync(); // em segundo plano, no máximo uma consulta por dia
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
        }, RoutingStrategies.Tunnel);
        lista.AddHandler(KeyUpEvent, (_, _) => _pelasSetas = false, RoutingStrategies.Tunnel);
        lista.AddHandler(PointerPressedEvent, (_, _) => _pelasSetas = false, RoutingStrategies.Tunnel);
        lista.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is LinhaDoProjeto l) l.Projeto.IniciarCommand.Execute(null);
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
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
}
