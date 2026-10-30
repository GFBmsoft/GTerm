using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTerm.Models;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>Uma conversa do Claude na árvore do modo Sessões: uma linha do cl.</summary>
public sealed partial class LinhaDaSessao : LinhaDaArvore
{
    private readonly MainViewModel _main;

    public LinhaDaSessao(MainViewModel main, SessaoDoClaude sessao, string grupo)
    {
        _main = main;
        _sessao = sessao;
        Grupo = grupo;
    }

    /// <summary>A mesma conversa aparece nas Favoritas e no grupo dela: uma linha em cada.</summary>
    public string Grupo { get; }

    public override Thickness Recuo => new(16 + Nivel * PassoDoRecuo, 0, 0, 0);

    /// <summary>Relida a cada pouco: o título, o tamanho e se está aberta mudam enquanto ela é usada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(Detalhe), nameof(Dica), nameof(Aberta), nameof(Quando),
        nameof(Tamanho), nameof(TemBranch))]
    private SessaoDoClaude _sessao;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlifoDaEstrela), nameof(RotuloDaEstrela))]
    private bool _favorita;

    /// <summary>O projeto da pasta de origem, ou o nome da pasta: ao lado de quando, como no cl -t.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detalhe))]
    private string _projeto = "";

    [ObservableProperty] private string _icone = "";
    [ObservableProperty] private IBrush _corDoIcone = Brushes.Gray;

    /// <summary>Aberta numa aba daqui: retomar é só ir para ela.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaAbertura), nameof(RotuloDeRetomar))]
    private bool _abertaAqui;

    public override bool Selecionavel => true;

    public string Titulo => Sessao.Titulo;
    public bool Aberta => Sessao.Aberta;
    public string Quando => SessoesDoClaude.Quando(Sessao.Quando, DateTime.Now);
    public string Tamanho => SessoesDoClaude.Tamanho(Sessao.Tamanho);
    public bool TemBranch => Sessao.Branch.Length > 0;

    /// <summary>Embaixo do título: de onde e quando, e o bg do job em segundo plano, como as colunas do cl.</summary>
    public string Detalhe =>
        string.Join(" · ", new[] { Projeto, Quando, Sessao.Bg ? "bg" : "" }.Where(t => t.Length > 0));

    public string Dica => string.Join("\n", new[]
    {
        Titulo,
        Sessao.Pasta,
        string.Join(" · ", new[] { Sessao.Branch, Tamanho, Quando }.Where(t => t.Length > 0)),
    }.Where(t => t.Length > 0));

    public string TextoDaAbertura => AbertaAqui ? "aberta numa aba do GTerm" : "aberta em outro terminal";
    public string RotuloDeRetomar => AbertaAqui ? "Ir para a aba" : "Retomar a conversa";

    /// <summary>A estrela da linha, do Segoe Fluent: cheia nas favoritas, vazada nas outras.</summary>
    public string GlifoDaEstrela => Favorita ? "" : "";
    public string RotuloDaEstrela => Favorita ? "Tirar das favoritas" : "Favoritar";

    [RelayCommand] private Task Retomar() => _main.RetomarAsync(this);
    [RelayCommand] private void Favoritar() => _main.AlternarFavorita(this);
}

/// <summary>
/// O modo Sessões da árvore: o cl (Modules\claude-sessoes.ps1) com tela. As conversas do
/// Claude de todas as pastas, as favoritas em cima e as outras por dia; a pesquisa filtra
/// pelo título e pelo projeto. Retomar abre a conversa na aba do projeto dela.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Sem pesquisa, quantas conversas aparecem além das favoritas, como o -Max do cl.</summary>
    public const int MaxSessoes = 30;

    private readonly SessoesDoClaude _leitorDeSessoes = new();
    private IReadOnlyList<SessaoDoClaude> _sessoes = Array.Empty<SessaoDoClaude>();
    private List<string> _favoritas = new();
    private readonly DispatcherTimer _releituraDasSessoes;
    private bool _lendoSessoes;
    private bool _primeiraAoLer;
    private readonly Dictionary<string, (string Glifo, IBrush Cor)> _iconesDasPastas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A árvore mostra as conversas do Claude no lugar dos projetos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarcaDoFiltro))]
    private bool _modoSessoes;

    /// <summary>A conversa escolhida no modo Sessões: o quadro dela fica no lugar do terminal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarSessao), nameof(MostrarParado))]
    private LinhaDaSessao? _sessaoAVista;

    /// <summary>As conversas já foram lidas ao menos uma vez.</summary>
    [ObservableProperty] private bool _sessoesLidas;

    public bool MostrarSessao => SessaoAVista is not null;

    public string MarcaDoFiltro => ModoSessoes ? "Sessões" : "Projetos";

    [RelayCommand] private void VerProjetos() => ModoSessoes = false;
    [RelayCommand] private void VerSessoes() => ModoSessoes = true;

    partial void OnModoSessoesChanged(bool value)
    {
        SessaoAVista = null;
        Filtro = "";
        Remontar();
        if (value)
        {
            // entrar nas Sessões mostra a primeira favorita; na primeira vez, quando a leitura terminar
            _primeiraAoLer = !SessoesLidas;
            if (SessoesLidas) VerPrimeiraSessao();
            _ = CarregarSessoesAsync();
            _releituraDasSessoes.Start();
        }
        else
        {
            _releituraDasSessoes.Stop();
        }
    }

    partial void OnSessaoAVistaChanged(LinhaDaSessao? value)
    {
        MarcarLinha();
        AtualizarAviso();
        Mostrar();
        Visto();
    }

    /// <summary>
    /// Relê as conversas da conta, fora da tela: ao entrar no modo Sessões, a cada 30 s
    /// enquanto ele está à vista e quando um Claude daqui é encerrado. Só o arquivo que mudou
    /// é lido de novo.
    /// </summary>
    internal async Task CarregarSessoesAsync()
    {
        if (_lendoSessoes) return;
        _lendoSessoes = true;
        try
        {
            var conta = ClaudeHooks.PastaDaConta(_workspace.ContaDasSessoes);
            _sessoes = await Task.Run(() => _leitorDeSessoes.Listar(conta));
        }
        finally
        {
            _lendoSessoes = false;
        }

        SessoesLidas = true;
        if (ModoSessoes) Remontar();

        // escolher uma conversa enquanto a leitura não terminava vale mais que a favorita
        if (_primeiraAoLer && ModoSessoes && SessaoAVista is null) VerPrimeiraSessao();
        _primeiraAoLer = false;
    }

    /// <summary>
    /// A primeira das favoritas, como aparece no topo da árvore; sem favorita, a primeira
    /// conversa. Oculta não conta, nem a favorita com as Favoritas fora da árvore. Vale também
    /// com o grupo recolhido: o quadro aparece, só a linha não.
    /// </summary>
    private void VerPrimeiraSessao()
    {
        var visiveis = _sessoes.Where(x => !_workspace.SessoesOcultas.Contains(x.Id, Chaves)).ToList();
        var favorita = Escondido(GrupoFavoritas) ? null : visiveis.FirstOrDefault(x => _favoritas.Contains(x.Id, Chaves));
        if ((favorita ?? visiveis.FirstOrDefault()) is { } s) SessaoAVista = LinhaDe(s, PidsDasAbas(), "");
    }

    private HashSet<int> PidsDasAbas() => Abertos.Select(p => p.Sessao?.Pid).OfType<int>().ToHashSet();

    /// <summary>As favoritas do cl. Arquivo ilegível no momento: fica o que já havia.</summary>
    private void CarregarFavoritas()
    {
        if (SessoesDoClaude.LerFavoritas(_workspace.EstadoDoCl) is { } lidas) _favoritas = lidas.ToList();
    }

    private static readonly (string Titulo, string Cor)[] Dias =
    {
        ("Hoje", "#2F7BE8"), ("Ontem", "#8B5CF6"), ("Esta semana", "#E07B00"), ("Mais antigas", "#6B7280"),
    };

    /// <summary>
    /// A árvore do cl: as favoritas em cima, de qualquer pasta, os grupos do usuário e depois
    /// as outras por dia, as mais novas primeiro. Ocultas ficam num grupo só delas, no fim.
    /// Grupo fixo vazio não aparece; o do usuário sim, para receber conversas. Com pesquisa,
    /// só o que bate, com os grupos abertos, também os que estão fora da árvore.
    /// </summary>
    private List<LinhaDaArvore> LinhasDasSessoes()
    {
        var novas = new List<LinhaDaArvore>();
        var agora = DateTime.Now;
        var termo = Filtro.Trim();
        var buscando = termo.Length > 0;
        var pidsDasAbas = PidsDasAbas();
        var ocultas = _workspace.SessoesOcultas.ToHashSet(Chaves);

        var achadas = _sessoes.Where(s => !buscando ||
                s.Titulo.Contains(termo, StringComparison.CurrentCultureIgnoreCase) ||
                NomeDoProjeto(s, ProjetoDaConversa(s)).Contains(termo, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var visiveis = achadas.Where(s => !ocultas.Contains(s.Id)).ToList();
        bool Fora(string grupo) => !buscando && Escondido(grupo);

        void Acrescentar(string id, IEnumerable<SessaoDoClaude> itens, int nivel)
        {
            foreach (var s in itens)
            {
                var l = LinhaDe(s, pidsDasAbas, id);
                l.Nivel = nivel;
                novas.Add(l);
            }
        }

        void Fixo(string id, string titulo, string cor, List<SessaoDoClaude> itens)
        {
            if (itens.Count == 0 || Fora(id)) return;
            var g = TituloDoGrupo(id, titulo, cor, itens.Count, sempreAberto: buscando);
            novas.Add(g);
            if (!g.Recolhido) Acrescentar(id, itens, 0);
        }

        var todos = _workspace.GruposDasSessoes;
        var porId = new Dictionary<string, SessaoDoClaude>(Chaves);
        foreach (var s in visiveis) porId[s.Id] = s;

        var porGrupo = new Dictionary<string, List<SessaoDoClaude>>();
        var colocadas = new HashSet<string>(Chaves);
        foreach (var g in todos)
        {
            porGrupo[g.Id] = new();
            foreach (var id in g.Itens)
                if (porId.TryGetValue(id, out var s) && colocadas.Add(id)) porGrupo[g.Id].Add(s);
        }

        void Desenhar(Grupo g, int nivel)
        {
            if (Fora(g.Id)) return;
            var abaixo = buscando ? GrupoArvore.ComDescendentes(todos, g.Id) : AVistaAbaixo(todos, g.Id);
            var total = abaixo.Sum(id => porGrupo.GetValueOrDefault(id)?.Count ?? 0);
            if (buscando && total == 0) return;

            var titulo = TituloDoGrupo(g.Id, g.Nome, g.Cor, total, sempreAberto: buscando, nivel: nivel);
            novas.Add(titulo);
            if (titulo.Recolhido) return;

            foreach (var filho in GrupoArvore.Filhos(todos, g.Id)) Desenhar(filho, nivel + 1);
            Acrescentar(g.Id, porGrupo[g.Id], nivel);
        }

        bool Favorita(SessaoDoClaude s) => _favoritas.Contains(s.Id, Chaves);

        Fixo(GrupoFavoritas, "Favoritas", "#B08500", visiveis.Where(Favorita).ToList());
        foreach (var g in GrupoArvore.Filhos(todos, null)) Desenhar(g, 0);

        // o dia fora da árvore não gasta o limite do cl; com as Favoritas fora, a favorita
        // volta para o dia dela, para não sumir
        var soltas = visiveis
            .Where(s => (!Favorita(s) || Fora(GrupoFavoritas)) && !colocadas.Contains(s.Id) &&
                        !Fora("s:" + SessoesDoClaude.Dia(s.Quando, agora)))
            .Take(buscando ? int.MaxValue : MaxSessoes).ToList();
        foreach (var (titulo, cor) in Dias)
            Fixo("s:" + titulo, titulo, cor, soltas.Where(s => SessoesDoClaude.Dia(s.Quando, agora) == titulo).ToList());

        Fixo(GrupoOcultas, "Ocultas", "#6B7280", achadas.Where(s => ocultas.Contains(s.Id)).ToList());

        // a conversa à vista pode estar num grupo recolhido: o quadro dela segue em dia
        if (SessaoAVista is { } vista && _sessoes.FirstOrDefault(s => s.Id == vista.Sessao.Id) is { } atual)
            LinhaDe(atual, pidsDasAbas, vista.Grupo);
        return novas;
    }

    private ProjetoViewModel? ProjetoDaConversa(SessaoDoClaude s) =>
        s.Pasta.Length > 0 ? Projetos.FirstOrDefault(p => Mesma(p.Pasta, s.Pasta)) : null;

    private static string NomeDoProjeto(SessaoDoClaude s, ProjetoViewModel? projeto) =>
        projeto?.Nome ?? (s.Pasta.Length > 0 ? NomeDaPasta(s.Pasta) : s.Chave);

    // a mesma linha para a mesma conversa no mesmo grupo: relida, ela só troca os dados, e a
    // lista não perde a seleção
    private LinhaDaSessao LinhaDe(SessaoDoClaude s, HashSet<int> pidsDasAbas, string grupo)
    {
        var chave = "s|" + grupo + "|" + s.Id;
        var l = (LinhaDaSessao)(_cache.TryGetValue(chave, out var achada)
            ? achada
            : _cache[chave] = new LinhaDaSessao(this, s, grupo));
        l.Sessao = s;
        l.Favorita = _favoritas.Contains(s.Id, Chaves);
        l.AbertaAqui = s.Pid is { } pid && pidsDasAbas.Contains(pid);

        var projeto = ProjetoDaConversa(s);
        l.Projeto = NomeDoProjeto(s, projeto);
        (l.Icone, l.CorDoIcone) = projeto is not null ? (projeto.Icone, projeto.CorDoIcone) : IconeDaPasta(s.Pasta);
        return l;
    }

    // fora das bases o tipo da pasta é lido uma vez só
    private (string Glifo, IBrush Cor) IconeDaPasta(string pasta)
    {
        if (_iconesDasPastas.TryGetValue(pasta, out var icone)) return icone;
        var (glifo, cor) = Cia.Icone(pasta.Length > 0 && Directory.Exists(pasta) ? Cia.Tipo(pasta) : "pasta");
        return _iconesDasPastas[pasta] = (glifo, new SolidColorBrush(Color.Parse(cor)));
    }

    /// <summary>A estrela da conversa: favorita na frente das outras, como o cl -f, ou tira.</summary>
    public void AlternarFavorita(LinhaDaSessao l)
    {
        try
        {
            SessoesDoClaude.TrocarFavorita(_workspace.EstadoDoCl, l.Sessao.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Aviso = "Não foi possível gravar as favoritas: " + ex.Message;
            return;
        }
        CarregarFavoritas();
        Remontar();
    }

    /// <summary>
    /// O ▶ da conversa, o Enter e o duplo clique nela: o Claude na pasta de origem, na aba do
    /// projeto, com o --resume dela, como o cl &lt;n&gt;. Aberta numa aba daqui, só vai para a
    /// aba; aberta em outro terminal, confirma antes, como o cl.
    /// </summary>
    public async Task RetomarAsync(LinhaDaSessao l)
    {
        var s = l.Sessao;
        if (s.Pid is { } pid && Abertos.FirstOrDefault(p => p.Sessao?.Pid == pid) is { } aba)
        {
            Ver(aba);
            return;
        }

        if (s.Pasta.Length == 0 || !Directory.Exists(s.Pasta))
        {
            Aviso = "A pasta de origem da conversa não existe mais: " + (s.Pasta.Length > 0 ? s.Pasta : s.Chave);
            return;
        }

        if (s.Aberta && !await _dialogos.ConfirmAsync("Conversa aberta em outro terminal",
                $"“{s.Titulo}” está aberta em outro terminal. Retomar aqui mesmo assim?"))
            return;

        await AbrirAsync(ProjetoDaPasta(s.Pasta), Cia.ModoConversa + s.Id);
    }

    /// <summary>
    /// O Claude sem nenhuma das conversas: limpo, sem retomar e sem skill, na pasta pessoal,
    /// numa aba "Claude". É o claude digitado num terminal recém-aberto.
    /// </summary>
    [RelayCommand]
    private Task NovaConversa() => AbrirAsync(ProjetoDaPasta(PastaPessoal), "-Limpo");

    private static string PastaPessoal => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// O projeto da pasta. Fora das bases do cia (a pasta pessoal, um script solto) vira um
    /// avulso: tem aba, mas não linha na árvore, e some na releitura das bases depois de
    /// encerrado.
    /// </summary>
    private ProjetoViewModel ProjetoDaPasta(string pasta)
    {
        if (Projetos.FirstOrDefault(p => Mesma(p.Pasta, pasta)) is { } existente) return existente;

        var limpa = pasta.Trim().TrimEnd('\\', '/');
        var info = new ProjetoDoCia(NomeDaPasta(limpa), limpa, Path.GetDirectoryName(limpa) ?? limpa, Cia.Tipo(limpa));
        var avulso = new ProjetoViewModel(info, PreferenciasDe(info), daBmsoft: false, this);
        Projetos.Add(avulso);
        return avulso;
    }

    // a pasta pessoal é a do Claude sem projeto: a aba dela se chama Claude
    private static string NomeDaPasta(string pasta) =>
        Mesma(pasta, PastaPessoal) ? "Claude" : Path.GetFileName(pasta.Trim().TrimEnd('\\', '/'));
}
