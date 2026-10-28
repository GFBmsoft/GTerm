using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTerm.Models;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>Diálogos, implementados pela MainWindow.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> PromptAsync(string title, string label);

    /// <summary>Devolve as preferências escolhidas, ou null quando o usuário cancela.</summary>
    /// <param name="contas">Pastas de configuração do Claude onde os avisos podem ser instalados.</param>
    Task<Preferencias?> PreferenciasAsync(Preferencias atuais, IReadOnlyList<string> contas);
}

/// <param name="Fonte">Vazia usa a lista padrão.</param>
/// <param name="PainelComando">Script do painel lateral; vazio, sem painel.</param>
/// <param name="Avisar">Chamar a atenção quando um projeto fora da vista termina ou espera por você.</param>
public sealed record Preferencias(string Fonte, double Tamanho, string PainelComando, bool Avisar = true);

/// <summary>Uma linha da árvore: o título de um grupo ou um projeto dentro dele.</summary>
public abstract class LinhaDaArvore : ObservableObject
{
    /// <summary>Só a linha de projeto se escolhe: o título abre e fecha no clique, e as setas passam por ele.</summary>
    public abstract bool Selecionavel { get; }
}

/// <summary>Título de grupo, minimalista como no GTest: seta, ponto e nome na cor dele, e a contagem.</summary>
public sealed partial class GrupoDaArvore : LinhaDaArvore
{
    private readonly MainViewModel _main;

    public GrupoDaArvore(MainViewModel main, string id, string titulo, string cor)
    {
        _main = main;
        Id = id;
        Titulo = titulo;
        Cor = new SolidColorBrush(Color.Parse(cor));
    }

    public string Id { get; }
    public string Titulo { get; }
    public IBrush Cor { get; }

    [ObservableProperty] private int _contagem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Seta))]
    private bool _recolhido;

    /// <summary>Seta do Segoe Fluent: para a direita recolhido, para baixo aberto.</summary>
    public string Seta => Recolhido ? "" : "";

    public override bool Selecionavel => false;

    [RelayCommand] private void Alternar() => _main.AlternarGrupo(this);
}

/// <summary>Um projeto num grupo. O mesmo projeto aparece em mais de um (favorito, recente e na base).</summary>
public sealed partial class LinhaDoProjeto : LinhaDaArvore
{
    private readonly MainViewModel _main;

    public LinhaDoProjeto(MainViewModel main, ProjetoViewModel projeto, string grupo)
    {
        _main = main;
        Projeto = projeto;
        Grupo = grupo;
    }

    public ProjetoViewModel Projeto { get; }
    public string Grupo { get; }

    /// <summary>Só nos favoritos a ordem é do usuário: lá a linha sobe e desce.</summary>
    public bool NosFavoritos => Grupo == MainViewModel.GrupoFavoritos;

    /// <summary>À direita do nome, apagado: o número do favorito, quando foi aberto, a base.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemExtra))]
    private string _extra = "";

    public bool TemExtra => Extra.Length > 0;

    public override bool Selecionavel => true;

    [RelayCommand] private void Subir() => _main.MoverFavorito(Projeto, -1);
    [RelayCommand] private void Descer() => _main.MoverFavorito(Projeto, 1);
}

/// <summary>Um projeto das bases do cia e, se já foi aberto, o terminal dele.</summary>
public sealed partial class ProjetoViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ProjetoViewModel(ProjetoDoCia info, Projeto projeto, bool daBmsoft, MainViewModel main)
    {
        Info = info;
        Projeto = projeto;
        EhDaBmsoft = daBmsoft;
        _main = main;
        var (glifo, cor) = Cia.Icone(info.Tipo);
        Icone = glifo;
        CorDoIcone = new SolidColorBrush(Color.Parse(cor));
        _corHex = GroupPalette.Normalizar(projeto.Cor) ?? GroupPalette.DoNome(info.Nome);
    }

    public ProjetoDoCia Info { get; }

    /// <summary>O que o projeto lembra (painel, segundo terminal) e de onde sai o arranque do shell.</summary>
    public Projeto Projeto { get; }

    public string Nome => Info.Nome;
    public string Pasta => Info.Pasta;

    /// <summary>O tipo (Delphi, C#, Go...) em Nerd Font, na cor do Terminal-Icons, como no cia.</summary>
    public string Icone { get; }
    public IBrush CorDoIcone { get; }

    /// <summary>
    /// Da primeira base, a da BMSoft: lá o cia abre na delphi-writer, e pedido do Mantis e
    /// pull request fazem sentido. Nas outras (Private) ele abre no /retomar.
    /// </summary>
    public bool EhDaBmsoft { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDoFavorito))]
    private bool _favorito;

    public string RotuloDoFavorito => Favorito ? "Tirar dos favoritos" : "Favoritar";

    /// <summary>
    /// A cor do projeto, a da aba dele. Sem escolha, uma da paleta tirada do nome, pela
    /// mesma conta do GTest: o projeto tem a mesma cor nos dois.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cor), nameof(Fundo), nameof(FundoDaAba), nameof(BordaDaAba), nameof(FundoDaBarra), nameof(BordaDaBarra))]
    private string _corHex;

    private Color CorBase => Color.Parse(CorHex);
    public IBrush Cor => new SolidColorBrush(CorBase);

    // fundo da aba da frente: a cor esmaecida
    public IBrush Fundo => new SolidColorBrush(CorBase, 0.22);

    // a barra dos botões, embaixo das abas: o tom da aba, mais leve
    public IBrush FundoDaBarra => new SolidColorBrush(CorBase, 0.09);
    public IBrush BordaDaBarra => new SolidColorBrush(CorBase, 0.35);

    /// <summary>A aba dele é a da frente: ganha o fundo e o contorno na cor do projeto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FundoDaAba), nameof(BordaDaAba))]
    private bool _aVista;

    public IBrush FundoDaAba => AVista ? Fundo : Brushes.Transparent;
    public IBrush BordaDaAba => AVista ? Cor : Brushes.Transparent;

    /// <summary>A sessão que o cia retoma; null sem sessão com conversa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeAbrir), nameof(TextoDaSessao))]
    private UltimaSessao? _ultimaSessao;

    /// <summary>A sessão já foi procurada: até lá, o texto embaixo do botão fica vazio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaSessao))]
    private bool _sessaoLida;

    public string RotuloDeAbrir => UltimaSessao is null ? "Abrir o Claude" : "Retomar a sessão";

    /// <summary>Embaixo do botão de abrir: o que o cia vai fazer.</summary>
    public string TextoDaSessao => !SessaoLida ? ""
        : UltimaSessao is { } s
            ? $"“{s.Titulo}” · {Cia.Quando(s.Quando, DateTime.Now)} · {Cia.Tamanho(s.KB)}" +
              (s.KB >= Cia.CompactarKB ? " · abre com /compact" : "")
            : "Nenhuma sessão com conversa: abre " +
              (EhDaBmsoft ? "na /delphi-writer" : "no /retomar, com a memória do projeto");

    /// <summary>Null até o terminal do projeto ser iniciado, e depois de encerrado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Parado), nameof(TemTerminal), nameof(Vivo), nameof(Rodando), nameof(Aguardando), nameof(Concluido), nameof(Erro))]
    private TerminalSessao? _sessao;

    /// <summary>Sem terminal: a linha mostra o botão de abrir no lugar do ponto de estado.</summary>
    public bool Parado => Sessao is null;

    /// <summary>Tem terminal, vivo ou encerrado: o menu oferece encerrar.</summary>
    public bool TemTerminal => Sessao is not null;

    /// <summary>O painel lateral deste projeto, quando aberto.</summary>
    public TerminalSessao? PainelSessao { get; set; }

    /// <summary>O segundo terminal deste projeto, quando aberto.</summary>
    public TerminalSessao? AuxiliarSessao { get; set; }

    /// <summary>
    /// Terminou ou ficou esperando por você enquanto outro projeto (ou outra janela) estava
    /// na frente: o nome fica em destaque até o projeto ser visto.
    /// </summary>
    [ObservableProperty] private bool _pedeAtencao;

    /// <summary>Há um shell aberto: é o que pede confirmação antes de encerrar.</summary>
    public bool Vivo => Sessao is { Encerrada: false };

    // um por cor do ponto na árvore; nenhum ligado = terminal parado no prompt
    public bool Rodando => Sessao?.Estado == EstadoDoTerminal.Rodando;
    public bool Aguardando => Sessao?.Estado == EstadoDoTerminal.Aguardando;
    public bool Concluido => Sessao?.Estado == EstadoDoTerminal.Concluido;
    public bool Erro => Sessao?.Estado == EstadoDoTerminal.Erro;

    partial void OnSessaoChanged(TerminalSessao? oldValue, TerminalSessao? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= SessaoMudou;
        if (newValue is not null) newValue.PropertyChanged += SessaoMudou;
    }

    private void SessaoMudou(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TerminalSessao.Encerrada) or nameof(TerminalSessao.Estado))) return;
        OnPropertyChanged(nameof(Vivo));
        OnPropertyChanged(nameof(Rodando));
        OnPropertyChanged(nameof(Aguardando));
        OnPropertyChanged(nameof(Concluido));
        OnPropertyChanged(nameof(Erro));
        _main.EstadoMudou(this);
    }

    // os comandos ficam aqui porque o menu de contexto é um popup: de dentro dele não se
    // alcança o DataContext da janela por $parent
    [RelayCommand] private Task Iniciar() => _main.IniciarAsync(this);
    [RelayCommand] private Task AbrirClaude() => _main.AbrirAsync(this);
    [RelayCommand] private Task SessaoNova() => _main.AbrirAsync(this, "-Novo");
    [RelayCommand] private Task SemSkill() => _main.AbrirAsync(this, "-Limpo");
    [RelayCommand] private Task PullRequest() => _main.AbrirAsync(this, "-Pr");
    [RelayCommand] private Task Pedido() => _main.AbrirPedidoAsync(this);
    [RelayCommand] private void Favoritar() => _main.AlternarFavorito(this);
    [RelayCommand] private Task Encerrar() => _main.EncerrarAsync(this);

    /// <summary>O clique na aba: traz o projeto para a frente.</summary>
    [RelayCommand] private void Mostrar() => _main.Selecionado = this;

    /// <summary>A cor da aba, escolhida no menu dela.</summary>
    [RelayCommand] private void EscolherCor(string hex) => _main.MudarCor(this, hex);
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string GrupoFavoritos = "favoritos";
    public const string GrupoRecentes = "recentes";
    public const string GrupoResultados = "resultados";

    private readonly IDialogService _dialogos;
    private readonly Workspace _workspace;

    // os vigias de pasta avisam de outra thread
    private readonly Dispatcher _ui = Dispatcher.UIThread;

    /// <summary>Todos os projetos das bases, na ordem delas.</summary>
    public ObservableCollection<ProjetoViewModel> Projetos { get; } = new();

    /// <summary>A árvore achatada: títulos de grupo e, dos grupos abertos, os projetos.</summary>
    public ObservableCollection<LinhaDaArvore> Linhas { get; } = new();

    /// <summary>A linha escolhida na árvore. Mudar de linha muda o projeto à vista.</summary>
    [ObservableProperty] private LinhaDaArvore? _linhaSelecionada;

    /// <summary>O que se digita em cima da árvore: com texto, ela vira a lista dos que batem.</summary>
    [ObservableProperty] private string _filtro = "";

    /// <summary>Terminais abertos, todos vivos; só o do projeto selecionado aparece.</summary>
    public ObservableCollection<TerminalSessao> Sessoes { get; } = new();

    /// <summary>Painéis laterais abertos, um por projeto; também só aparece o do selecionado.</summary>
    public ObservableCollection<TerminalSessao> Paineis { get; } = new();

    [ObservableProperty] private ProjetoViewModel? _selecionado;

    /// <summary>O que aparece no lugar do terminal quando não há um para mostrar.</summary>
    [ObservableProperty] private string? _aviso;

    /// <summary>O projeto à vista não tem terminal: no lugar dele, os jeitos de abrir o Claude.</summary>
    public bool MostrarParado => Selecionado is { Sessao: null };

    /// <summary>A escolhida na frente e, atrás, a lista padrão: é o que cobre fonte desinstalada.</summary>
    public string Fonte => _workspace.Fonte is { Length: > 0 } f ? f + "," + FontePadrao : FontePadrao;
    public double TamanhoDaFonte => _workspace.TamanhoDaFonte;

    // Nerd Font na frente: os ícones do prompt (oh-my-posh, starship) e de TUIs vivem na
    // área privada do Unicode, que a Cascadia Mono pura não tem
    public const string FontePadrao = "CaskaydiaCove NFM,CaskaydiaMono Nerd Font,JetBrainsMono NFM,Cascadia Mono,Consolas";

    /// <summary>Segundos terminais abertos, um por projeto; também só aparece o do selecionado.</summary>
    public ObservableCollection<TerminalSessao> Auxiliares { get; } = new();

    public bool HaTerminalRodando => Projetos.Any(p => p.Vivo || p.AuxiliarSessao is { Encerrada: false });

    public bool AuxiliarVisivel => Selecionado?.AuxiliarSessao is not null;

    /// <summary>O segundo terminal acompanha o do projeto: só existe com ele iniciado.</summary>
    public bool PodeAuxiliar => Selecionado?.Sessao is not null;

    /// <summary>Há um script de painel nas preferências: o botão do painel aparece.</summary>
    public bool TemPainel => !string.IsNullOrWhiteSpace(_workspace.PainelComando);

    public bool PainelVisivel => Selecionado?.PainelSessao is not null;

    /// <summary>Os projetos com o Claude aberto, na ordem em que foram abertos: as abas e o Ctrl+Tab.</summary>
    public ObservableCollection<ProjetoViewModel> Abertos { get; } = new();

    /// <summary>O projeto da aba da frente: a barra embaixo das abas tem os botões dele.</summary>
    public ProjetoViewModel? AbaAtiva => Selecionado is { Sessao: not null } p ? p : null;

    public bool TemAbaAtiva => AbaAtiva is not null;

    private EstadoDoCia _estado = EstadoDoCia.Vazio;
    private List<ProjetoViewModel> _favoritos = new();

    public MainViewModel(IDialogService dialogos)
    {
        _dialogos = dialogos;
        _workspace = WorkspaceStore.Load();
        Atualizador.LimparAntigo();
        _rodape = RodapePadrao;
        _releitura = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _releitura.Tick += (_, _) => Reler();

        CarregarProjetos();
        CarregarEstado();
        Remontar();
        Vigiar();

        Selecionado = Projetos.FirstOrDefault(p => Mesma(p.Pasta, _workspace.UltimaPasta));
        if (Selecionado is null) AtualizarAviso();
    }

    partial void OnSelecionadoChanged(ProjetoViewModel? value)
    {
        // selecionar não sobe o shell: o Claude do projeto só abre quando o usuário manda
        if (value is not null && !Mesma(_workspace.UltimaPasta, value.Pasta))
        {
            _workspace.UltimaPasta = value.Pasta;
            Salvar();
        }

        MarcarLinha();
        AtualizarAviso();
        Mostrar();
        Visto();
        OnPropertyChanged(nameof(MostrarParado));
        if (value is { Sessao: null }) _ = CarregarSessaoAsync(value);
    }

    partial void OnLinhaSelecionadaChanged(LinhaDaArvore? value)
    {
        // a lista solta a seleção quando a linha some (grupo recolhido): o projeto continua à vista
        if (value is LinhaDoProjeto l && l.Projeto != Selecionado) Selecionado = l.Projeto;
    }

    partial void OnFiltroChanged(string value) => Remontar();

    /// <summary>
    /// A sessão que o cia vai retomar, para o botão de abrir dizer o que acontece. Lida fora
    /// da tela: os .jsonl das sessões passam de megabytes.
    /// </summary>
    internal async Task CarregarSessaoAsync(ProjetoViewModel p)
    {
        var conta = ClaudeHooks.PastaDaConta(p.Projeto.Conta);
        p.UltimaSessao = await Task.Run(() => Cia.UltimaSessao(p.Pasta, conta));
        p.SessaoLida = true;
    }

    // ------------------------------------------------------------ projetos e árvore

    private static bool Mesma(string? a, string? b) =>
        string.Equals(a?.Trim().TrimEnd('\\', '/'), b?.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Os projetos das bases. Quem já existia fica com o mesmo view model (e o terminal
    /// dele); a pasta que sumiu com o terminal aberto continua até o terminal ser encerrado.
    /// </summary>
    private void CarregarProjetos()
    {
        var primeira = _workspace.Bases.FirstOrDefault();

        var novos = Cia.Projetos(_workspace.Bases)
            .Select(info => Projetos.FirstOrDefault(p => Mesma(p.Pasta, info.Pasta)) ??
                            new ProjetoViewModel(info, PreferenciasDe(info), Mesma(info.Base, primeira), this))
            .ToList();
        novos.AddRange(Abertos.Where(p => !novos.Contains(p)));

        Projetos.Clear();
        foreach (var p in novos) Projetos.Add(p);
    }

    private Projeto PreferenciasDe(ProjetoDoCia info)
    {
        var p = _workspace.Projetos.FirstOrDefault(x => Mesma(x.Pasta, info.Pasta)) ?? new Projeto { Pasta = info.Pasta };

        // o cia vale para todos: o shell é o das preferências, sem comando próprio nem outra conta
        p.Nome = info.Nome;
        p.Shell = _workspace.Shell;
        p.Comando = "";
        p.Conta = "";
        return p;
    }

    // só entra no workspace quem tem algo a lembrar
    private void Guardar(ProjetoViewModel p)
    {
        if (!_workspace.Projetos.Contains(p.Projeto)) _workspace.Projetos.Add(p.Projeto);
    }

    /// <summary>Favoritos e recentes do cia. Arquivo ilegível no momento: fica o que já havia.</summary>
    private void CarregarEstado()
    {
        if (Cia.LerEstado(_workspace.EstadoDoCia) is { } lido) _estado = lido;

        _favoritos = _estado.Favoritos.Select(Achar).OfType<ProjetoViewModel>().Distinct().ToList();
        foreach (var p in Projetos) p.Favorito = _favoritos.Contains(p);
    }

    /// <summary>Pelo caminho; no formato antigo do estado do cia, pelo nome (vale a primeira base).</summary>
    private ProjetoViewModel? Achar(string referencia) =>
        Projetos.FirstOrDefault(p => Mesma(p.Pasta, referencia)) ??
        Projetos.FirstOrDefault(p => p.Nome.Equals(referencia, StringComparison.OrdinalIgnoreCase));

    // linhas já criadas, por grupo e pasta: a mesma linha volta igual e a lista não perde a seleção
    private readonly Dictionary<string, LinhaDaArvore> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CoresDasBases = { "#2F7BE8", "#8B5CF6", "#E07B00" };

    /// <summary>
    /// Monta a árvore como o dashboard do cia: Favoritos, Recentes e uma pasta por base. Os
    /// abertos não entram: o lugar deles são as abas. Com filtro, só os que batem.
    /// </summary>
    private void Remontar()
    {
        var novas = new List<LinhaDaArvore>();
        var agora = DateTime.Now;

        void Grupo(string id, string titulo, string cor, IEnumerable<(ProjetoViewModel P, string Extra)> itens,
            bool sempreAberto = false)
        {
            var lista = itens.ToList();
            var g = (GrupoDaArvore)(_cache.TryGetValue("g|" + id, out var achado)
                ? achado
                : _cache["g|" + id] = new GrupoDaArvore(this, id, titulo, cor));
            g.Contagem = lista.Count;
            g.Recolhido = !sempreAberto && EstaRecolhido(id);
            novas.Add(g);
            if (g.Recolhido) return;

            foreach (var (p, extra) in lista)
            {
                var chave = "p|" + id + "|" + p.Pasta;
                var l = (LinhaDoProjeto)(_cache.TryGetValue(chave, out var existente)
                    ? existente
                    : _cache[chave] = new LinhaDoProjeto(this, p, id));
                l.Extra = extra;
                novas.Add(l);
            }
        }

        if (Filtro.Trim() is { Length: > 0 } termo)
        {
            Grupo(GrupoResultados, "Resultados", "#2F7BE8", Projetos
                    .Where(p => p.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(p => !p.Nome.StartsWith(termo, StringComparison.CurrentCultureIgnoreCase)) // estável: o resto fica na ordem das bases
                    .Select(p => (p, Path.GetFileName(p.Info.Base))),
                sempreAberto: true);
        }
        else
        {
            Grupo(GrupoFavoritos, "Favoritos", "#B08500",
                _favoritos.Select((p, i) => (p, i < 9 ? (i + 1).ToString() : "")));

            Grupo(GrupoRecentes, "Recentes", "#0E9384", _estado.Recentes
                .Select(r => (P: Achar(r.Referencia), R: r))
                .Where(x => x.P is not null)
                .DistinctBy(x => x.P)
                .Take(8)
                .Select(x => (x.P!, string.Join(" ", new[]
                {
                    x.R.Quando is { } q ? Cia.Quando(q, agora) : "",
                    x.R.Pedido.Length > 0 ? "#" + x.R.Pedido : "",
                }.Where(t => t.Length > 0)))));

            for (var i = 0; i < _workspace.Bases.Count; i++)
            {
                var b = _workspace.Bases[i];
                if (string.IsNullOrWhiteSpace(b)) continue;
                Grupo(IdDaBase(b), Path.GetFileName(b.Trim().TrimEnd('\\', '/')), CoresDasBases[i % CoresDasBases.Length],
                    Projetos.Where(p => Mesma(p.Info.Base, b)).Select(p => (p, "")));
            }
        }

        // troca só o que mudou: a lista mantém a rolagem e a linha escolhida
        for (var i = 0; i < novas.Count; i++)
        {
            if (i < Linhas.Count && ReferenceEquals(Linhas[i], novas[i])) continue;
            var j = Linhas.IndexOf(novas[i]);
            if (j > i) Linhas.Move(j, i);
            else Linhas.Insert(i, novas[i]);
        }
        while (Linhas.Count > novas.Count) Linhas.RemoveAt(Linhas.Count - 1);

        MarcarLinha();
    }

    private static string IdDaBase(string b) => "base:" + b.Trim().TrimEnd('\\', '/');

    private bool EstaRecolhido(string id) =>
        (_workspace.Recolhidos ?? RecolhidosPadrao()).Contains(id, StringComparer.OrdinalIgnoreCase);

    // como no cia: Recentes e as bases abrem recolhidos
    private List<string> RecolhidosPadrao() =>
        new[] { GrupoRecentes }.Concat(_workspace.Bases.Where(b => !string.IsNullOrWhiteSpace(b)).Select(IdDaBase)).ToList();

    /// <summary>O clique no título do grupo: recolhe ou abre, e lembra.</summary>
    public void AlternarGrupo(GrupoDaArvore g)
    {
        if (g.Id == GrupoResultados) return;

        var lista = _workspace.Recolhidos ??= RecolhidosPadrao();
        if (lista.RemoveAll(i => i.Equals(g.Id, StringComparison.OrdinalIgnoreCase)) == 0) lista.Add(g.Id);
        Salvar();
        Remontar();
    }

    /// <summary>
    /// A linha do projeto à vista fica marcada na árvore: a primeira em que ele aparece. Em
    /// grupo recolhido não há linha, e a árvore fica sem marca.
    /// </summary>
    private void MarcarLinha()
    {
        if (LinhaSelecionada is LinhaDoProjeto atual && atual.Projeto == Selecionado && Linhas.Contains(atual)) return;
        LinhaSelecionada = Selecionado is null
            ? null
            : Linhas.OfType<LinhaDoProjeto>().FirstOrDefault(l => l.Projeto == Selecionado);
    }

    /// <summary>O Enter no filtro: vai para o primeiro projeto da lista.</summary>
    public void EscolherPrimeiro()
    {
        if (Linhas.OfType<LinhaDoProjeto>().FirstOrDefault() is { } l) Selecionado = l.Projeto;
    }

    public void AlternarFavorito(ProjetoViewModel p)
    {
        var lista = _favoritos.ToList();
        if (!lista.Remove(p)) lista.Add(p);
        GravarFavoritos(lista);
    }

    /// <summary>Sobe ou desce o favorito uma posição: é a ordem do Ctrl+número e do dashboard do cia.</summary>
    public void MoverFavorito(ProjetoViewModel p, int passo)
    {
        var lista = _favoritos.ToList();
        var de = lista.IndexOf(p);
        var para = de + passo;
        if (de < 0 || para < 0 || para >= lista.Count) return;
        (lista[de], lista[para]) = (lista[para], lista[de]);
        GravarFavoritos(lista);
    }

    private void GravarFavoritos(List<ProjetoViewModel> lista)
    {
        // favorito que não é projeto daqui (base que esta máquina não tem) continua no arquivo
        var soltos = _estado.Favoritos.Where(f => Achar(f) is null);
        try
        {
            Cia.GravarFavoritos(_workspace.EstadoDoCia, lista.Select(p => p.Pasta).Concat(soltos));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Aviso = "Não foi possível gravar os favoritos: " + ex.Message;
            return;
        }
        CarregarEstado();
        Remontar();
    }

    // ------------------------------------------------------------ vigias

    private readonly DispatcherTimer _releitura;
    private bool _relerProjetos;
    private readonly List<FileSystemWatcher> _vigias = new();

    /// <summary>
    /// O cia grava os recentes ao abrir um projeto (aqui ou no Windows Terminal), e pasta
    /// nova aparece na base: a árvore acompanha sem reabrir o app.
    /// </summary>
    private void Vigiar()
    {
        void Novo(string pasta, string filtro, NotifyFilters mudancas, bool projetos)
        {
            try
            {
                var vigia = new FileSystemWatcher(pasta, filtro) { NotifyFilter = mudancas };
                vigia.Changed += (_, _) => _ui.Post(() => Agendar(projetos));
                vigia.Created += (_, _) => _ui.Post(() => Agendar(projetos));
                vigia.Deleted += (_, _) => _ui.Post(() => Agendar(projetos));
                vigia.Renamed += (_, _) => _ui.Post(() => Agendar(projetos));
                vigia.EnableRaisingEvents = true;
                _vigias.Add(vigia);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // pasta que não existe nesta máquina: nada a vigiar
            }
        }

        if (Path.GetDirectoryName(_workspace.EstadoDoCia) is { Length: > 0 } dir)
            Novo(dir, Path.GetFileName(_workspace.EstadoDoCia),
                NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, projetos: false);
        foreach (var b in _workspace.Bases.Where(b => !string.IsNullOrWhiteSpace(b)))
            Novo(b.Trim(), "*", NotifyFilters.DirectoryName, projetos: true);
    }

    // o cia grava o arquivo em mais de um passo: espera o último antes de ler
    private void Agendar(bool projetos)
    {
        _relerProjetos |= projetos;
        _releitura.Stop();
        _releitura.Start();
    }

    internal void Reler()
    {
        _releitura.Stop();
        if (_relerProjetos) CarregarProjetos();
        _relerProjetos = false;
        CarregarEstado();
        Remontar();
        if (Projetos.Count == 0 || Selecionado is null) AtualizarAviso();
    }

    // ------------------------------------------------------------ atenção

    /// <summary>A janela está na frente, com o teclado. Quem informa é a própria janela.</summary>
    public bool JanelaAtiva
    {
        get => _janelaAtiva;
        set
        {
            _janelaAtiva = value;
            Visto();
        }
    }
    private bool _janelaAtiva = true;

    /// <summary>Um projeto fora da vista passou a pedir atenção: a janela pisca na barra de tarefas.</summary>
    public event Action? PedirAtencao;

    /// <summary>O título da janela conta os projetos à espera: aparece na barra de tarefas e no Alt+Tab.</summary>
    public string Titulo => Projetos.Count(p => p.PedeAtencao) switch
    {
        0 => "GTerm",
        1 => "GTerm — " + Projetos.First(p => p.PedeAtencao).Nome + " espera por você",
        var n => $"GTerm — {n} projetos esperam por você",
    };

    /// <summary>
    /// O estado do terminal de um projeto mudou. Terminou ou parou para perguntar sem
    /// ninguém olhando (outro projeto selecionado, ou a janela atrás de outra): marca o
    /// projeto e chama. Voltou a rodar: não há mais o que ver.
    /// </summary>
    public void EstadoMudou(ProjetoViewModel p)
    {
        if (p.Sessao?.ChamaAtencao != true)
            p.PedeAtencao = false;
        else if (_workspace.AvisarAtencao && !p.PedeAtencao && !(JanelaAtiva && Selecionado == p))
        {
            p.PedeAtencao = true;
            PedirAtencao?.Invoke();
        }

        OnPropertyChanged(nameof(Titulo));
    }

    // o projeto selecionado, com a janela na frente, está sendo visto
    private void Visto()
    {
        if (!JanelaAtiva || Selecionado is not { PedeAtencao: true } p) return;
        p.PedeAtencao = false;
        OnPropertyChanged(nameof(Titulo));
    }

    /// <summary>A cor escolhida no menu da aba: vale na hora e fica gravada.</summary>
    public void MudarCor(ProjetoViewModel p, string hex)
    {
        if (GroupPalette.Normalizar(hex) is not { } cor) return;
        p.CorHex = cor;
        p.Projeto.Cor = cor;
        Guardar(p);
        Salvar();
    }

    /// <summary>
    /// Os hooks do Claude guardam o caminho do executável que os instalou. Na abertura de
    /// uma versão publicada, as contas onde eles apontam para outro lugar (o app mudou de
    /// pasta, ou foram instalados por um build de teste) passam a apontar para este. Build
    /// local não corrige sozinho: tomaria os hooks do GTerm instalado a cada teste.
    /// </summary>
    public IReadOnlyList<string> CorrigirHooks()
    {
        if (Atualizador.VersaoEmUso.Length == 0 || Environment.ProcessPath is not { Length: > 0 } exe)
            return Array.Empty<string>();

        return ClaudeHooks.Corrigir(ClaudeHooks.Contas(Array.Empty<string>()), exe);
    }

    // ------------------------------------------------------------ terminais

    /// <summary>Só o terminal e o painel do projeto selecionado ficam à vista, e a aba dele na frente.</summary>
    private void Mostrar()
    {
        foreach (var p in Abertos) p.AVista = p == Selecionado;
        foreach (var s in Sessoes) s.Ativa = s == Selecionado?.Sessao;
        foreach (var s in Paineis) s.Ativa = s == Selecionado?.PainelSessao;
        foreach (var s in Auxiliares) s.Ativa = s == Selecionado?.AuxiliarSessao;
        OnPropertyChanged(nameof(PainelVisivel));
        OnPropertyChanged(nameof(AuxiliarVisivel));
        OnPropertyChanged(nameof(PodeAuxiliar));
        OnPropertyChanged(nameof(AbaAtiva));
        OnPropertyChanged(nameof(TemAbaAtiva));
    }

    /// <summary>Abre o Claude no terminal do projeto se ainda não houver um.</summary>
    /// <param name="modo">-Novo, -Limpo, -Pr ou o número do pedido; null retoma a última sessão.</param>
    private void Abrir(ProjetoViewModel p, string? modo = null)
    {
        if (p.Sessao is null)
        {
            // o Enter depois de sair do Claude retoma a sessão: o modo pedido aqui vale uma vez
            var daVez = modo;
            var sessao = new TerminalSessao(() =>
            {
                var arranque = ArranqueDoClaude(p, daVez);
                daVez = null;
                return arranque;
            });
            try
            {
                sessao.Iniciar(esperarTela: true);
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                sessao.Dispose();
                Aviso = ex.Message;
                return;
            }

            p.Sessao = sessao;
            Sessoes.Add(sessao);
            Abertos.Add(p);

            // um terminal que não está à vista não se mede, e o shell dele espera a medida
            // para subir: pega a de quem já foi medido, e passa adiante a sua quando tiver
            sessao.Mediu += (colunas, linhas) =>
            {
                foreach (var s in Sessoes) s.Medir(colunas, linhas);
            };
            if (_medida is { } m) sessao.Medir(m.Colunas, m.Linhas);
            sessao.Mediu += (colunas, linhas) => _medida = (colunas, linhas);
        }

        if (p.Projeto.Painel) AbrirPainel(p);
        if (p.Projeto.Auxiliar) AbrirAuxiliar(p);
        if (Selecionado == p) Aviso = null;
    }

    // colunas e linhas da área dos terminais dos projetos, da última vez que um foi medido
    private (int Colunas, int Linhas)? _medida;

    /// <summary>
    /// O Claude como o cia o abriria (<see cref="Cia.Decidir"/>), direto, sem shell, e o
    /// recente gravado no estado do cia, como ele grava. Roda a cada subida: o Enter depois
    /// de sair do Claude procura de novo a sessão a retomar.
    /// </summary>
    private Arranque ArranqueDoClaude(ProjetoViewModel p, string? modo)
    {
        var claude = _workspace.Claude is { Length: > 0 } proprio ? proprio
            : Cia.Executavel() is { } exe ? "\"" + exe + "\""
            : throw new FileNotFoundException(
                @"Claude Code não encontrado: o claude.exe não está no PATH nem em %USERPROFILE%\.local\bin.");

        var abertura = Cia.Decidir(modo, p.EhDaBmsoft,
            () => Cia.UltimaSessao(p.Pasta, ClaudeHooks.PastaDaConta(p.Projeto.Conta)));

        if (Directory.Exists(p.Pasta))
        {
            try
            {
                Cia.RegistrarRecente(_workspace.EstadoDoCia, p.Pasta, abertura.Pedido, abertura.Modo, DateTime.Now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // o recente é conveniência: o Claude abre do mesmo jeito
            }
        }

        return Arranque.DoClaude(p.Projeto, claude, abertura.Argumentos);
    }

    private void AbrirAuxiliar(ProjetoViewModel p)
    {
        if (p.AuxiliarSessao is not null) return;

        var auxiliar = new TerminalSessao(() => Arranque.DoAuxiliar(p.Projeto));
        try
        {
            auxiliar.Iniciar(esperarTela: true);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            auxiliar.Dispose(); // sem a pasta não há terminal; o aviso do principal já diz o porquê
            return;
        }

        p.AuxiliarSessao = auxiliar;
        Auxiliares.Add(auxiliar);
    }

    private void FecharAuxiliar(ProjetoViewModel p)
    {
        if (p.AuxiliarSessao is not { } auxiliar) return;
        Auxiliares.Remove(auxiliar);
        p.AuxiliarSessao = null;
        auxiliar.Dispose();
    }

    private void AbrirPainel(ProjetoViewModel p)
    {
        if (!TemPainel || p.PainelSessao is not null) return;

        var painel = new TerminalSessao(() => Arranque.DoPainel(p.Projeto, _workspace.PainelComando));
        try
        {
            painel.Iniciar(esperarTela: true);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            painel.Dispose(); // sem a pasta não há painel; o aviso do terminal já diz o porquê
            return;
        }

        p.PainelSessao = painel;
        Paineis.Add(painel);
    }

    private void FecharPainel(ProjetoViewModel p)
    {
        if (p.PainelSessao is not { } painel) return;
        Paineis.Remove(painel);
        p.PainelSessao = null;
        painel.Dispose();
    }

    private void Fechar(ProjetoViewModel p)
    {
        FecharPainel(p);
        FecharAuxiliar(p);
        if (p.Sessao is not { } sessao) return;
        Sessoes.Remove(sessao);
        p.Sessao = null;
        sessao.Dispose();
        p.PedeAtencao = false;
        p.AVista = false;
        Abertos.Remove(p);
        OnPropertyChanged(nameof(Titulo));
        OnPropertyChanged(nameof(MostrarParado));
    }

    private void AtualizarAviso() => Aviso =
        Projetos.Count == 0
            ? (_workspace.Bases.Count == 0
                ? "Nenhuma pasta de projetos configurada."
                : "Nenhum projeto nas pastas do cia: " + string.Join(", ", _workspace.Bases) + ".")
        : Selecionado is null ? "Escolha um projeto na árvore."
        : null;

    private void Salvar() => WorkspaceStore.Save(_workspace);

    [RelayCommand]
    private async Task AbrirPreferencias()
    {
        var contas = ClaudeHooks.Contas(Array.Empty<string>());
        var novas = await _dialogos.PreferenciasAsync(
            new Preferencias(_workspace.Fonte ?? "", _workspace.TamanhoDaFonte, _workspace.PainelComando ?? "",
                _workspace.AvisarAtencao),
            contas);
        if (novas is null) return;

        _workspace.Fonte = novas.Fonte.Trim();
        _workspace.TamanhoDaFonte = Math.Clamp(novas.Tamanho, 8, 32);
        _workspace.PainelComando = novas.PainelComando.Trim();
        _workspace.AvisarAtencao = novas.Avisar;
        Salvar();
        OnPropertyChanged(nameof(Fonte));
        OnPropertyChanged(nameof(TamanhoDaFonte));
        OnPropertyChanged(nameof(TemPainel));
    }

    /// <summary>Abre ou fecha o painel lateral do projeto selecionado, e lembra a escolha.</summary>
    [RelayCommand]
    private void AlternarPainel()
    {
        if (Selecionado is not { } p) return;

        p.Projeto.Painel = p.PainelSessao is null;
        Guardar(p);
        Salvar();
        if (p.Projeto.Painel) AbrirPainel(p);
        else FecharPainel(p);
        Mostrar();
    }

    /// <summary>
    /// Abre ou esconde o segundo terminal do projeto selecionado, e lembra a escolha. Ele
    /// só aparece com o terminal do projeto iniciado, e é encerrado junto com ele.
    /// </summary>
    [RelayCommand]
    private async Task AlternarAuxiliar()
    {
        if (Selecionado is not { Sessao: not null } p) return;

        if (p.AuxiliarSessao is { Encerrada: false } &&
            !await _dialogos.ConfirmAsync("Fechar o segundo terminal",
                $"Fechar o segundo terminal de “{p.Nome}”? O que estiver rodando nele será interrompido."))
            return;

        p.Projeto.Auxiliar = p.AuxiliarSessao is null;
        Guardar(p);
        Salvar();
        if (p.Projeto.Auxiliar) AbrirAuxiliar(p);
        else FecharAuxiliar(p);
        Mostrar();
    }

    /// <summary>Ctrl+1 a Ctrl+9: o favorito naquela posição, como o [n] do cia.</summary>
    public void SelecionarPorNumero(int numero)
    {
        if (_favoritos.ElementAtOrDefault(numero - 1) is { } p) Selecionado = p;
    }

    /// <summary>Ctrl+Tab e Ctrl+Shift+Tab: dá a volta nas abas.</summary>
    public void SelecionarVizinho(int passo)
    {
        if (Abertos.Count == 0) return;

        var atual = Selecionado is null ? -1 : Abertos.IndexOf(Selecionado);
        Selecionado = atual < 0
            ? (passo > 0 ? Abertos[0] : Abertos[^1])
            : Abertos[((atual + passo) % Abertos.Count + Abertos.Count) % Abertos.Count];
    }

    /// <summary>
    /// O ▶ da linha, o Enter e o duplo clique na árvore: abre o Claude num projeto sem
    /// terminal; no que já tem, só o traz para a frente.
    /// </summary>
    public Task IniciarAsync(ProjetoViewModel p)
    {
        if (p.Sessao is null) return AbrirAsync(p);
        Selecionado = p;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Abre o Claude no projeto, direto no terminal, como o cia abriria. Com um Claude já
    /// aberto ali, só depois de confirmar: ele é encerrado e outro sobe no lugar.
    /// </summary>
    /// <param name="modo">-Novo, -Limpo, -Pr ou o número do pedido; null retoma a última sessão.</param>
    public async Task AbrirAsync(ProjetoViewModel p, string? modo = null)
    {
        if (p.Vivo && !await _dialogos.ConfirmAsync("Abrir o Claude de outro jeito",
                $"O Claude de “{p.Nome}” está aberto. Ele sai pelo /exit e é aberto de novo{Descrever(modo)}, " +
                "e o que estiver rodando nele será interrompido."))
            return;

        if (!await SairDoClaudeAsync(p)) return;
        Fechar(p);
        Iniciar(p, modo);
    }

    /// <summary>O tempo que o Claude tem para sair pelo /exit antes de ser encerrado à força.</summary>
    internal TimeSpan PrazoDaSaida { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Antes de fechar o terminal, o Claude sai pelo /exit, como no terminal: termina a
    /// sessão do jeito dele (os hooks de fim de sessão rodam). Não saindo no prazo, o
    /// Fechar encerra o processo. Falso quando, enquanto ele saía, outro comando já cuidou
    /// do terminal: aí não há mais o que fechar.
    /// </summary>
    private async Task<bool> SairDoClaudeAsync(ProjetoViewModel p)
    {
        if (p.Sessao is not { Encerrada: false } sessao) return true;
        await sessao.SairAsync("/exit", PrazoDaSaida);
        return p.Sessao == sessao;
    }

    private static string Descrever(string? modo) => modo switch
    {
        null => ", retomando a última sessão",
        "-Novo" => " numa sessão nova",
        "-Limpo" => " sem skill",
        "-Pr" => " no pull request",
        _ => " no pedido " + modo,
    };

    public async Task AbrirPedidoAsync(ProjetoViewModel p)
    {
        var pedido = (await _dialogos.PromptAsync("Pedido do Mantis", "Número do pedido"))?.Trim();
        if (string.IsNullOrEmpty(pedido)) return;

        // vai para a linha de comando do shell: só número, nada que ele possa interpretar
        if (!pedido.All(char.IsAsciiDigit))
        {
            Aviso = "Número de pedido inválido: " + pedido;
            return;
        }

        await AbrirAsync(p, pedido);
    }

    private void Iniciar(ProjetoViewModel p, string? modo)
    {
        Selecionado = p;
        Abrir(p, modo);
        Mostrar();
        OnPropertyChanged(nameof(MostrarParado));
    }

    public async Task EncerrarAsync(ProjetoViewModel p)
    {
        if (p.Sessao is null) return;
        if (p.Vivo && !await _dialogos.ConfirmAsync("Encerrar o Claude",
                $"Encerrar o Claude de “{p.Nome}”? Ele sai pelo /exit, e o que estiver rodando nele será interrompido."))
            return;

        if (!await SairDoClaudeAsync(p)) return;
        Fechar(p);
        Mostrar();
        if (Selecionado == p)
        {
            AtualizarAviso();
            _ = CarregarSessaoAsync(p);
        }
    }

    /// <summary>Ao fechar a janela: nenhum shell fica órfão, e os vigias de pasta param.</summary>
    public void EncerrarTudo()
    {
        foreach (var p in Abertos.ToList()) Fechar(p);
        foreach (var v in _vigias) v.Dispose();
        _vigias.Clear();
        _releitura.Stop();
    }

    // ------------------------------------------------------------ atualização

    private Release? _release;

    /// <summary>Encerra o aplicativo sem perguntar pelos terminais, depois de uma atualização.</summary>
    public Action? Sair { get; set; }

    /// <summary>Tag da release mais nova que a versão em uso; vazia quando não há.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAtualizacao))]
    private string _atualizacaoTag = "";

    public bool TemAtualizacao => AtualizacaoTag.Length > 0;

    /// <summary>A linha do pé da árvore: a versão, o convite para atualizar ou o andamento.</summary>
    [ObservableProperty] private string _rodape;

    private static string RodapePadrao =>
        Atualizador.VersaoEmUso is { Length: > 0 } versao ? "GTerm " + versao : "GTerm (build local)";

    partial void OnAtualizacaoTagChanged(string value)
    {
        if (value.Length > 0) Rodape = $"Atualizar para {value}";
    }

    /// <summary>
    /// Procura release nova. Build local não é avisado (não tem versão para comparar), e a
    /// API só é consultada uma vez por dia — o resultado anterior fica no workspace.
    /// </summary>
    public async Task VerificarAtualizacaoAsync(bool forcar = false)
    {
        var atual = Atualizador.VersaoEmUso;
        if (atual.Length == 0) return;

        try
        {
            var recente =
                DateTime.TryParse(_workspace.UltimaChecagem, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var quando) &&
                DateTime.UtcNow - quando < Atualizador.IntervaloDeChecagem;

            var tag = _workspace.UltimaTagVista ?? "";

            if (forcar || !recente)
            {
                _release = await Atualizador.UltimaReleaseAsync();
                tag = _release?.Tag ?? "";

                _workspace.UltimaChecagem = DateTime.UtcNow.ToString("o");
                _workspace.UltimaTagVista = tag;
                Salvar();
            }

            if (tag.Length > 0 && Atualizador.TemNovidade(atual, tag)) AtualizacaoTag = tag;
        }
        catch (Exception) when (!forcar)
        {
            // atualização é conveniência: sem rede ou fora da cota, o app segue igual.
            // Pedida pelo usuário, a falha sobe para quem chamou dizer o que houve.
        }
    }

    /// <summary>O clique no pé da árvore: atualiza se há versão nova, senão procura uma.</summary>
    [RelayCommand]
    private async Task Atualizacao()
    {
        if (TemAtualizacao)
        {
            await AtualizarAgoraAsync();
            return;
        }

        if (Atualizador.VersaoEmUso.Length == 0)
        {
            Rodape = "Build local, sem versão";
            return;
        }

        try
        {
            Rodape = "Procurando atualização…";
            await VerificarAtualizacaoAsync(forcar: true);
            // o rodapé tem a largura da árvore: frase inteira ali sai cortada
            if (!TemAtualizacao) Rodape = RodapePadrao + "  ✓";
        }
        catch (Exception e)
        {
            Rodape = "Não foi possível procurar: " + e.Message;
        }
    }

    /// <summary>
    /// Baixa a release e troca o executável. Só o de arquivo único pode ser trocado por
    /// aqui; no build de pasta resta abrir a página, onde o usuário escolhe o que baixar.
    /// </summary>
    private async Task AtualizarAgoraAsync()
    {
        var trocado = false;
        try
        {
            _release ??= await Atualizador.UltimaReleaseAsync();

            var exe = Atualizador.CaminhoDoExe();
            var arquivo = _release?.Standalone;

            if (arquivo is null || !Atualizador.PodeTrocarSozinho(exe))
            {
                Atualizador.Reabrir(_release?.Url is { Length: > 0 } u
                    ? u
                    : $"https://github.com/{Atualizador.Slug}/releases/latest");
                return;
            }

            // achada pelo site (API sem cota), a release não traz o tamanho
            var tamanho = arquivo.Tamanho > 0 ? $" ({arquivo.Tamanho / 1024d / 1024d:N0} MB)" : "";
            if (!await _dialogos.ConfirmAsync("Atualizar o GTerm",
                    $"Baixar a versão {AtualizacaoTag}{tamanho} e reiniciar o aplicativo?\n\n" +
                    "Todos os terminais abertos serão encerrados, com o que estiver rodando neles. " +
                    "O executável atual é guardado como cópia e volta sozinho se algo falhar."))
                return;

            // os avisos de progresso chegam pela fila da tela, e os últimos chegam depois
            // de o download terminar: sem a trava, um "100%" atrasado cobria o que viesse
            // em seguida, inclusive a mensagem de erro
            var baixando = true;
            var progresso = new Progress<double>(p =>
            {
                if (baixando) Rodape = $"Baixando… {p:P0}";
            });
            var baixado = await Atualizador.BaixarAsync(arquivo, Path.GetDirectoryName(exe!)!, progresso);
            baixando = false;

            Rodape = "Instalando…";
            await Task.Delay(50); // dá à tela a vez de mostrar o texto antes da troca
            Atualizador.Trocar(exe!, baixado);
            trocado = true;
            await Atualizador.ReabrirAsync(exe!);

            // o novo processo já está subindo; este sai para liberar o arquivo
            Sair?.Invoke();
        }
        catch (Exception e)
        {
            // com o executável já trocado só faltou reabrir: não é para tentar de novo
            Rodape = trocado ? "Atualizado: feche e abra de novo" : "Não foi possível atualizar: " + e.Message;
        }
    }
}
