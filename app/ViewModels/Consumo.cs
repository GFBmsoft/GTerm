using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>Uma janela do plano no cartão: verde, amarela da metade em diante, vermelha de 80% em diante.</summary>
public sealed record LinhaDeLimite(string Nome, double Valor, string Percentual, string Zera, string? Bloqueio)
{
    public bool Alerta => Valor is >= 50 and < 80;
    public bool Critico => Valor >= 80;
    public bool TemBloqueio => Bloqueio is not null;
}

/// <summary>Um tipo de token da sessão: quanto, e que parte do total.</summary>
public sealed record LinhaDeTokens(string Valor, string Percentual)
{
    public static readonly LinhaDeTokens Vazia = new("", "");
}

/// <summary>
/// O cartão do consumo, ao lado do terminal: o que o <c>ctop -Slim</c> mostrava no painel lateral do
/// cia (o contexto e os tokens da sessão, os limites do plano e o consumo do projeto por dia), já
/// em texto de tela. Quem lê os dados é a janela (<see cref="MainViewModel.LerConsumoAsync"/>).
/// </summary>
public sealed partial class ConsumoViewModel : ObservableObject
{
    // ------------------------------------------------------------ contexto

    [ObservableProperty] private bool _temContexto;

    /// <summary>De 0 a 100: quanto da janela a conversa ocupa.</summary>
    [ObservableProperty] private double _contexto;

    [ObservableProperty] private string _percentualDoContexto = "";
    [ObservableProperty] private string _textoDoContexto = "";

    /// <summary>Passou do que o cia retoma sozinho (<see cref="Cia.LimiteParaRetomar"/>): a barra fica amarela.</summary>
    [ObservableProperty] private bool _contextoAlto;

    [ObservableProperty] private bool _contextoCritico;

    public string AvisoDoContexto { get; } =
        $"Passou de {Cia.Tokens(Cia.LimiteParaRetomar)}: fechado, o Abrir o Claude começa uma sessão nova.";

    // ------------------------------------------------------------ sessão

    [ObservableProperty] private string _tituloDaSessao = "SESSÃO";
    [ObservableProperty] private bool _temSessao;
    [ObservableProperty] private string _semSessao = "lendo…";
    [ObservableProperty] private string _totalDaSessao = "";

    /// <summary>Entrada, saída, cache gravado e cache lido, na ordem das cores da barra.</summary>
    [ObservableProperty] private IReadOnlyList<double> _partes = Array.Empty<double>();

    [ObservableProperty] private LinhaDeTokens _entrada = LinhaDeTokens.Vazia;
    [ObservableProperty] private LinhaDeTokens _saida = LinhaDeTokens.Vazia;
    [ObservableProperty] private LinhaDeTokens _cacheGravado = LinhaDeTokens.Vazia;
    [ObservableProperty] private LinhaDeTokens _cacheLido = LinhaDeTokens.Vazia;
    [ObservableProperty] private string _resumoDaSessao = "";
    [ObservableProperty] private string _andamentoDaSessao = "";

    // ------------------------------------------------------------ limites do plano

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemLimites))]
    private IReadOnlyList<LinhaDeLimite> _limites = Array.Empty<LinhaDeLimite>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemPlano))]
    private string _plano = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAvisoDosLimites))]
    private string _avisoDosLimites = "lendo…";

    public bool TemLimites => Limites.Count > 0;
    public bool TemPlano => Plano.Length > 0;
    public bool TemAvisoDosLimites => AvisoDosLimites.Length > 0;

    // ------------------------------------------------------------ projeto

    [ObservableProperty] private bool _temProjeto;
    [ObservableProperty] private string _tituloDoProjeto = "PROJETO";
    [ObservableProperty] private string _totalDoProjeto = "";
    [ObservableProperty] private IReadOnlyList<long> _porDia = Array.Empty<long>();
    [ObservableProperty] private IReadOnlyList<string> _rotulosDosDias = Array.Empty<string>();
    [ObservableProperty] private string _primeiroDia = "";

    /// <summary>Outro projeto à vista: os números do anterior saem enquanto os dele são lidos. Os limites são da conta e ficam.</summary>
    public void Limpar()
    {
        TemContexto = false;
        TemSessao = false;
        SemSessao = "lendo…";
        TemProjeto = false;
    }

    /// <param name="aberta">É a conversa do Claude aberto na aba; senão, a última do projeto parado.</param>
    public void MostrarSessao(ConsumoDaSessao? s, bool aberta, DateTime agora)
    {
        TituloDaSessao = aberta ? "SESSÃO" : "ÚLTIMA SESSÃO";

        if (s is not { Uso.Respostas: > 0 })
        {
            TemContexto = false;
            TemSessao = false;
            SemSessao = aberta ? "O Claude ainda não respondeu nesta conversa." : "Nenhuma conversa neste projeto.";
            return;
        }

        if (s.Contexto is { } contexto)
        {
            Contexto = Math.Min(100, 100d * contexto / s.Janela);
            PercentualDoContexto = Porcento(contexto, s.Janela);
            TextoDoContexto = $"{ConsumoDoClaude.Numero(contexto)} de {ConsumoDoClaude.Numero(s.Janela)} tokens";
            ContextoAlto = contexto >= Cia.LimiteParaRetomar;
            ContextoCritico = Contexto >= 80;
        }
        TemContexto = s.Contexto is not null;

        var u = s.Uso;
        TotalDaSessao = ConsumoDoClaude.Numero(u.Total);
        Partes = new double[] { u.Entrada, u.Saida, u.CacheGravado, u.CacheLido };
        Entrada = Linha(u.Entrada, u.Total);
        Saida = Linha(u.Saida, u.Total);
        CacheGravado = Linha(u.CacheGravado, u.Total);
        CacheLido = Linha(u.CacheLido, u.Total);

        var cache = u.AcertoDoCache is { } acerto ? $"cache {Math.Round(acerto)}% · " : "";
        ResumoDaSessao = $"{cache}{ConsumoDoClaude.Numero(u.Total / u.Respostas)} por resposta";

        var respostas = u.Respostas == 1 ? "1 resposta" : u.Respostas + " respostas";
        AndamentoDaSessao = s.Inicio is { } inicio
            ? $"{respostas} · desde {Hora(inicio, agora)} · {Duracao((s.Ultima ?? inicio) - inicio)}"
            : respostas;

        TemSessao = true;
    }

    public void MostrarLimites(Limites l, DateTime agora)
    {
        Plano = l.Plano;
        Limites = l.Janelas
            .Select(j => new LinhaDeLimite(j.Nome, Math.Clamp(j.Uso, 0, 100), Math.Round(j.Uso) + "%",
                LimitesDoPlano.QuandoZera(j.Zera, agora), j.Bloqueio))
            .ToList();

        // o valor pode ser de minutos atrás (a espera depois de um erro): aí a idade aparece
        var idade = l.Em is { } em && agora - em >= TimeSpan.FromMinutes(5) ? "lido " + Cia.Quando(em, agora) : "";
        AvisoDosLimites = l.Erro is { } erro ? (idade.Length > 0 ? $"{erro} · {idade}" : erro)
            : l.Janelas.Count == 0 ? "A conta não informou nenhuma janela."
            : idade;
    }

    public void MostrarProjeto(ConsumoDoProjeto p, DateTime agora)
    {
        var dias = p.PorDia.Count;
        TituloDoProjeto = $"PROJETO · {dias} DIAS";
        TotalDoProjeto = ConsumoDoClaude.Numero(p.Total);
        PorDia = p.PorDia;
        RotulosDosDias = p.PorDia
            .Select((tokens, i) =>
            {
                var dia = p.PrimeiroDia.AddDays(i);
                return (dia == agora.Date ? "hoje" : dia.ToString("dd/MM", CultureInfo.InvariantCulture)) +
                       ": " + ConsumoDoClaude.Numero(tokens);
            })
            .ToList();
        PrimeiroDia = p.PrimeiroDia.ToString("dd/MM", CultureInfo.InvariantCulture);
        TemProjeto = true;
    }

    private static LinhaDeTokens Linha(long valor, long total) => new(ConsumoDoClaude.Numero(valor), Porcento(valor, total));

    // o que existe e arredonda para zero não some: "<1%"
    private static string Porcento(long valor, long total)
    {
        if (total <= 0 || valor <= 0) return "0%";
        var p = Math.Round(100d * valor / total);
        return p < 1 ? "<1%" : p + "%";
    }

    private static string Hora(DateTime quando, DateTime agora) =>
        quando.ToString(quando.Date == agora.Date ? "HH:mm" : "dd/MM HH:mm", CultureInfo.InvariantCulture);

    private static string Duracao(TimeSpan d) => d.TotalHours >= 1
        ? $"{(int)d.TotalHours}h{d.Minutes:00}"
        : $"{Math.Max(0, (int)d.TotalMinutes)} min";
}

public sealed partial class MainViewModel
{
    /// <summary>O período do consumo do projeto, como o padrão do ctop.</summary>
    public const int DiasDoConsumo = 14;

    private readonly ConsumoDoClaude _consumo = new();
    private readonly DispatcherTimer _relogioDoConsumo = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _lendoConsumo, _consumoPendente;
    private ProjetoViewModel? _consumoDe;
    private DateTime _projetoLidoEm, _limitesLidosEm;

    /// <summary>Só os testes trocam: nenhum deles consulta a conta de verdade.</summary>
    internal LimitesDoPlano LimitesDoPlano { get; set; } = new();

    /// <summary>Leitura em curso: a que for pedida agora fica para depois dela. O teste espera ela sair.</summary>
    internal bool LendoConsumo => _lendoConsumo;

    public ConsumoViewModel Consumo { get; } = new();

    /// <summary>A pílula do canto está acesa: o cartão aparece com qualquer projeto à vista.</summary>
    public bool ConsumoLigado => _workspace.Consumo;

    public bool ConsumoVisivel => _workspace.Consumo && ProjetoAVista is not null;

    private void IniciarConsumo() => _relogioDoConsumo.Tick += (_, _) => _ = LerConsumoAsync();

    /// <summary>Abre ou fecha o cartão do consumo, e lembra a escolha. É da janela, não do projeto.</summary>
    [RelayCommand]
    private void AlternarConsumo()
    {
        _workspace.Consumo = !_workspace.Consumo;
        Salvar();
        OnPropertyChanged(nameof(ConsumoLigado));
        AcompanharConsumo();
    }

    // o cartão só é lido enquanto aparece: escondido, o relógio para
    private void AcompanharConsumo()
    {
        OnPropertyChanged(nameof(ConsumoVisivel));
        if (!ConsumoVisivel)
        {
            _relogioDoConsumo.Stop();
            return;
        }
        _relogioDoConsumo.Start();
        _ = LerConsumoAsync();
    }

    /// <summary>
    /// O consumo do projeto à vista, fora da tela: a conversa da aba (ou a última do projeto parado)
    /// a cada volta, que só lê o que o Claude acrescentou; os dias do projeto a cada minuto; e os
    /// limites do plano, que o arquivo do estado segura em uma consulta a cada dois minutos.
    /// </summary>
    internal async Task LerConsumoAsync()
    {
        if (!ConsumoVisivel || ProjetoAVista is not { } p) return;
        if (_lendoConsumo)
        {
            _consumoPendente = true;
            return;
        }

        _lendoConsumo = true;
        try
        {
            var agora = Relogio();
            if (_consumoDe != p)
            {
                _consumoDe = p;
                _projetoLidoEm = default;
                Consumo.Limpar();
            }

            var conta = ClaudeHooks.PastaDaConta(_workspace.ContaDasSessoes);
            var pid = p.Sessao?.Pid;
            var pasta = p.Pasta;
            var lerProjeto = agora - _projetoLidoEm >= TimeSpan.FromMinutes(1) || agora < _projetoLidoEm;

            var (sessao, projeto) = await Task.Run(() =>
            {
                // com o Claude aberto, a conversa dele; sem ele (parado, ou saiu), a última do projeto
                var aberta = pid is { } n ? ConsumoDoClaude.SessaoDoProcesso(conta, n) : null;
                var id = pid is null ? ConsumoDoClaude.UltimaSessao(conta, pasta) : aberta?.Id;
                var onde = aberta is { Pasta.Length: > 0 } a ? a.Pasta : pasta;
                return (id is null ? null : _consumo.Sessao(conta, onde, id),
                    lerProjeto ? _consumo.Projeto(conta, pasta, DiasDoConsumo, agora) : null);
            });

            // enquanto lia, outro projeto pode ter vindo para a frente: a volta seguinte é dele
            if (ProjetoAVista == p)
            {
                Consumo.MostrarSessao(sessao, pid is not null, agora);
                if (projeto is not null)
                {
                    Consumo.MostrarProjeto(projeto, agora);
                    _projetoLidoEm = agora;
                }
            }

            // os limites são da conta, não do projeto. Quem segura as consultas é o arquivo do
            // estado; isto é só para não o abrir a cada volta
            if (agora - _limitesLidosEm >= TimeSpan.FromSeconds(30) || agora < _limitesLidosEm)
            {
                _limitesLidosEm = agora;
                Consumo.MostrarLimites(await LimitesDoPlano.LerAsync(conta, agora), agora);
            }
        }
        finally
        {
            _lendoConsumo = false;
            if (_consumoPendente)
            {
                _consumoPendente = false;
                _ = LerConsumoAsync();
            }
        }
    }
}
