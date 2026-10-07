using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaTerminal;
using GTerm.Models;
using GTerm.ViewModels;

namespace GTerm.Views;

public partial class MainWindow : Window, IDialogService
{
    private readonly MainViewModel _vm;
    private bool _fechamentoConfirmado;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(this);
        DataContext = _vm;

        // trocar de projeto é para digitar: o teclado vai ao terminal que apareceu
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Selecionado) or nameof(MainViewModel.Aviso))
                Dispatcher.UIThread.Post(Focar, DispatcherPriority.Background);
        };
        Opened += (_, _) =>
        {
            Focar();
            _ = _vm.VerificarAtualizacaoAsync(); // em segundo plano, no máximo uma consulta por dia
        };

        // atualização instalada: o aviso de que os terminais fecham já foi confirmado, e o
        // executável novo já está subindo. Sai direto, sem passar pelo fechamento da
        // janela: na 1.0.0.1 esse caminho deixou o app aberto, parado em "100%". Com o
        // processo morto o Windows fecha os pseudoconsoles, e os shells vão junto
        _vm.Sair = () => System.Environment.Exit(0);

        // em túnel: o terminal consome o teclado todo, então a janela olha antes dele
        AddHandler(KeyDownEvent, AoTeclar, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Focar() =>
        this.GetVisualDescendants().OfType<TerminalControl>().FirstOrDefault(t => t.IsVisible)?.Focus();

    private void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        _vm.SelecionarVizinho(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
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

    public async Task<string?> PickFolderAsync(string title)
    {
        var pastas = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return pastas.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PromptAsync(string title, string label) =>
        await new PromptWindow(title, label).ShowDialog<string?>(this);

    public async Task<Preferencias?> PreferenciasAsync(Preferencias atuais, IReadOnlyList<string> contas) =>
        await new PreferenciasWindow(atuais, contas).ShowDialog<Preferencias?>(this);

    public async Task<ProjetoEditado?> EditarProjetoAsync(Projeto projeto) =>
        await new ProjetoWindow(projeto).ShowDialog<ProjetoEditado?>(this);
}
