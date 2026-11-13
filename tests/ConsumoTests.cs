using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using GTerm.Controls;
using GTerm.Services;
using GTerm.ViewModels;
using Xunit;

namespace GTerm.Tests;

/// <summary>Linhas de transcript como o Claude Code grava, para os testes do consumo.</summary>
internal static class Transcript
{
    public const string Pergunta = """{"type":"user","message":{"role":"user","content":"oi"},"timestamp":"2026-01-01T00:00:00.000Z"}""";

    /// <summary>Uma linha de uma resposta: a mesma resposta sai em várias, com o mesmo id.</summary>
    public static string Resposta(string id, DateTime quando, long entrada, long saida, long cacheGravado, long cacheLido,
        bool lateral = false) =>
        "{\"parentUuid\":\"p\",\"isSidechain\":" + (lateral ? "true" : "false") +
        ",\"message\":{\"model\":\"claude-opus-5-5\",\"id\":\"" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":" + entrada +
        ",\"cache_creation_input_tokens\":" + cacheGravado + ",\"cache_read_input_tokens\":" + cacheLido +
        ",\"output_tokens\":" + saida + ",\"service_tier\":\"standard\"}},\"type\":\"assistant\",\"uuid\":\"u\"," +
        "\"timestamp\":\"" + quando.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "\"}";

    /// <summary>O login que o Claude Code grava na conta.</summary>
    public static void Login(string conta, DateTime vence, string plano = "max")
    {
        Directory.CreateDirectory(conta);
        File.WriteAllText(Path.Combine(conta, ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"segredo\",\"refreshToken\":\"outro\",\"expiresAt\":" +
            new DateTimeOffset(vence).ToUnixTimeMilliseconds() + ",\"subscriptionType\":\"" + plano + "\"}}");
    }

    /// <summary>O que a conta responde no /usage: 64% da sessão, que zera em 3h12, e 23% da semana.</summary>
    public static string Limites(DateTime agora) =>
        "{\"five_hour\":{\"utilization\":64.0,\"resets_at\":\"" + Iso(agora.AddHours(3).AddMinutes(12)) + "\",\"locked_reason\":null}," +
        "\"seven_day\":{\"utilization\":23.0,\"resets_at\":\"" + Iso(agora.AddDays(2)) + "\",\"locked_reason\":null}," +
        "\"seven_day_opus\":null,\"seven_day_oauth_apps\":null," +
        "\"seven_day_sonnet\":{\"utilization\":81.5,\"resets_at\":null,\"locked_reason\":\"limite semanal\"}}";

    public static string Iso(DateTime local) => new DateTimeOffset(local).ToString("o", CultureInfo.InvariantCulture);
}

/// <summary>
/// O consumo lido dos transcripts, como o ctop -Slim: a soma por resposta sem contar duas vezes,
/// só o que foi acrescentado a cada volta, e sem travar o arquivo em que o Claude grava.
/// </summary>
public class ConsumoTests : IDisposable
{
    private const string Pasta = @"D:\Projetos\DBISAM\Notas";
    private static readonly DateTime Hoje = new(2026, 11, 13, 9, 0, 0);

    private readonly string _raiz = Directory.CreateTempSubdirectory("gterm-consumo-").FullName;
    private string Conta => Path.Combine(_raiz, ".claude");
    private string Conversas => Path.Combine(Conta, "projects", SessoesDoClaude.Chave(Pasta));

    public ConsumoTests() => Directory.CreateDirectory(Conversas);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private string Conversa(string id, params string[] linhas)
    {
        var arquivo = Path.Combine(Conversas, id + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, string.Concat(linhas.Select(l => l + "\n")));
        File.SetLastWriteTime(arquivo, Hoje); // o dia dos testes é fixo: o do relógio da máquina não entra
        return arquivo;
    }

    [Fact]
    public void ASessaoSomaCadaRespostaUmaVezEMedeOContexto()
    {
        Conversa("s1",
            Transcript.Pergunta,
            Transcript.Resposta("msg_1", Hoje, 10, 5, 1000, 0),    // a primeira linha da resposta, com a saída pela metade
            Transcript.Resposta("msg_1", Hoje, 10, 200, 1000, 0),  // a última traz o uso completo: é ela que vale
            Transcript.Resposta("msg_2", Hoje.AddMinutes(30), 20, 300, 500, 1000));

        var leitor = new ConsumoDoClaude();
        var s = leitor.Sessao(Conta, Pasta, "s1")!;
        Assert.Equal(new Uso(30, 500, 1500, 1000, 2), s.Uso);
        Assert.Equal(3030, s.Uso.Total);
        Assert.Equal(1520, s.Contexto); // o que a última resposta leu: entrada e cache
        Assert.Equal(200_000, s.Janela);
        Assert.Equal(Hoje, s.Inicio);
        Assert.Equal(Hoje.AddMinutes(30), s.Ultima);
        Assert.Equal(39.5, s.Uso.AcertoDoCache!.Value, 1); // 1000 lidos do cache em 2530 lidos

        // ler de novo não soma de novo
        Assert.Equal(s.Uso, leitor.Sessao(Conta, Pasta, "s1")!.Uso);

        // conversa que o Claude ainda não gravou, e projeto sem conversa nenhuma
        Assert.Null(leitor.Sessao(Conta, Pasta, "nova"));
        Assert.Null(ConsumoDoClaude.UltimaSessao(Conta, @"D:\Projetos\Outro"));
        Assert.Equal("s1", ConsumoDoClaude.UltimaSessao(Conta, Pasta));
    }

    /// <summary>
    /// Reler a conversa inteira a cada volta foi o que tirou o painel lateral do cia do ar. Aqui o
    /// que já foi contado não é lido de novo (o começo do arquivo é estragado de propósito), a
    /// linha pela metade espera o resto, e a leitura acontece com o arquivo aberto para gravação.
    /// </summary>
    [Fact]
    public void SoLeOQueFoiAcrescentado()
    {
        var arquivo = Conversa("s1",
            Transcript.Resposta("msg_1", Hoje, 10, 200, 1000, 0),
            Transcript.Resposta("msg_2", Hoje.AddMinutes(30), 20, 300, 500, 1000));
        var leitor = new ConsumoDoClaude();
        Assert.Equal(2, leitor.Sessao(Conta, Pasta, "s1")!.Uso.Respostas);

        // o começo vira espaço, no mesmo tamanho: quem relesse o arquivo perderia as duas respostas
        var tamanho = new FileInfo(arquivo).Length;
        using (var estraga = new FileStream(arquivo, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            estraga.Write(Encoding.UTF8.GetBytes(new string(' ', (int)tamanho - 1) + "\n"));

        // o Claude grava a resposta seguinte em dois pedaços, com o arquivo aberto
        var linha = Encoding.UTF8.GetBytes(Transcript.Resposta("msg_3", Hoje.AddHours(1), 5, 50, 0, 2000) + "\n");
        using (var claude = new FileStream(arquivo, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            claude.Write(linha, 0, linha.Length - 20);
            claude.Flush();
            Assert.Equal(2, leitor.Sessao(Conta, Pasta, "s1")!.Uso.Respostas); // pela metade: ainda não conta

            claude.Write(linha, linha.Length - 20, 20);
            claude.Flush();
            var s = leitor.Sessao(Conta, Pasta, "s1")!;
            Assert.Equal(new Uso(35, 550, 1500, 3000, 3), s.Uso);
            Assert.Equal(2005, s.Contexto);
        }
    }

    [Fact]
    public void OsSubagentesEntramNaSessaoMasNaoNoContexto()
    {
        Conversa("s1", Transcript.Resposta("msg_1", Hoje, 10, 200, 1000, 0));
        Conversa(Path.Combine("s1", "subagents", "agent-a1"),
            Transcript.Resposta("msg_a", Hoje.AddMinutes(5), 1, 400, 90_000, 0, lateral: true));

        var s = new ConsumoDoClaude().Sessao(Conta, Pasta, "s1")!;
        Assert.Equal(new Uso(11, 600, 91_000, 0, 2), s.Uso);
        Assert.Equal(1010, s.Contexto); // o da conversa: o subagente tem o dele
    }

    /// <summary>
    /// A sessão bifurcada copia o histórico para um .jsonl novo. A resposta copiada é de quem a teve
    /// primeiro, seja qual for a ordem em que os arquivos são lidos.
    /// </summary>
    [Fact]
    public void ARespostaCopiadaEDeQuemATevePrimeiro()
    {
        var antiga = Conversa("s0", Transcript.Resposta("msg_1", Hoje.AddDays(-1), 10, 200, 1000, 0));
        var nova = Conversa("s1",
            Transcript.Resposta("msg_1", Hoje.AddDays(-1), 10, 200, 1000, 0),
            Transcript.Resposta("msg_2", Hoje, 20, 300, 500, 1000));
        File.SetCreationTimeUtc(antiga, DateTime.UtcNow.AddDays(-2));
        File.SetCreationTimeUtc(nova, DateTime.UtcNow.AddDays(-1));

        // a nova é lida primeiro, e conta as duas
        var leitor = new ConsumoDoClaude();
        Assert.Equal(2, leitor.Sessao(Conta, Pasta, "s1")!.Uso.Respostas);

        // o projeto lê a antiga: a copiada volta para ela, e os dias não contam nada duas vezes
        var projeto = leitor.Projeto(Conta, Pasta, 14, Hoje);
        Assert.Equal(1210, projeto.PorDia[12]);
        Assert.Equal(1820, projeto.PorDia[13]);
        Assert.Equal(3030, projeto.Total);
        Assert.Equal(new Uso(20, 300, 500, 1000, 1), leitor.Sessao(Conta, Pasta, "s1")!.Uso);
        Assert.Equal(new Uso(10, 200, 1000, 0, 1), leitor.Sessao(Conta, Pasta, "s0")!.Uso);
    }

    [Fact]
    public void OProjetoSomaPorDiaSemAbrirConversaAntiga()
    {
        var recente = Conversa("s1",
            Transcript.Resposta("msg_1", Hoje.AddDays(-20), 1, 1, 1, 1),         // antes do período: fora
            Transcript.Resposta("msg_2", Hoje.AddDays(-3), 100, 0, 0, 0),
            Transcript.Resposta("msg_3", Hoje.AddHours(2), 0, 50, 0, 0));
        Conversa(Path.Combine("s1", "subagents", "agent-a1"), Transcript.Resposta("msg_a", Hoje, 0, 7, 0, 0, lateral: true));

        // gravada pela última vez antes do período: não pode ter nada dele, e não é aberta (se
        // fosse, a resposta de hoje dentro dela apareceria)
        var antiga = Conversa("s0", Transcript.Resposta("msg_0", Hoje, 9000, 0, 0, 0));
        File.SetLastWriteTime(antiga, Hoje.AddDays(-30));
        File.SetLastWriteTime(recente, Hoje);

        var p = new ConsumoDoClaude().Projeto(Conta, Pasta, 14, Hoje.AddHours(5));
        Assert.Equal(14, p.PorDia.Count);
        Assert.Equal(Hoje.Date.AddDays(-13), p.PrimeiroDia);
        Assert.Equal(100, p.PorDia[10]);
        Assert.Equal(57, p.PorDia[13]);
        Assert.Equal(157, p.Total);

        // projeto sem pasta de conversas: tudo zero
        Assert.Equal(0, new ConsumoDoClaude().Projeto(Conta, @"D:\Projetos\Outro", 14, Hoje).Total);
    }

    [Fact]
    public void AConversaDoProcessoVemDoArquivoDeSessoes()
    {
        Assert.Null(ConsumoDoClaude.SessaoDoProcesso(Conta, 4242));

        Directory.CreateDirectory(Path.Combine(Conta, "sessions"));
        File.WriteAllText(Path.Combine(Conta, "sessions", "4242.json"), """{"pid":4242,"sessionId":"abc","cwd":"D:\\Projetos\\DBISAM\\Notas"}""");
        Assert.Equal(("abc", Pasta), ConsumoDoClaude.SessaoDoProcesso(Conta, 4242)!.Value);

        File.WriteAllText(Path.Combine(Conta, "sessions", "4242.json"), "{ quebrado");
        Assert.Null(ConsumoDoClaude.SessaoDoProcesso(Conta, 4242));
    }

    [Theory]
    [InlineData(850, "850")]
    [InlineData(8_400, "8,4 mil")]
    [InlineData(84_200, "84 mil")]
    [InlineData(200_000, "200 mil")]
    [InlineData(999_499, "999 mil")]
    [InlineData(999_600, "1 mi")]
    [InlineData(1_000_000, "1 mi")]
    [InlineData(1_240_000, "1,2 mi")]
    [InlineData(117_300_000, "117 mi")]
    [InlineData(1_180_000_000, "1,18 bi")]
    public void NumerosCurtosEmPortugues(long n, string texto) => Assert.Equal(texto, ConsumoDoClaude.Numero(n));

    [Theory]
    [InlineData(0, 200_000)]
    [InlineData(200_000, 200_000)]
    [InlineData(200_037, 1_000_000)] // passou de 200 mil: só pode ser a de 1 milhão (não existe a de 500 mil)
    [InlineData(486_000, 1_000_000)]
    [InlineData(1_200_000, 1_500_000)]
    public void AJanelaEAMenorEmQueOContextoCabe(long maior, long janela) => Assert.Equal(janela, ConsumoDoClaude.Janela(maior));

    [Fact]
    public void OCartaoDizOConsumoEmTextoDeTela()
    {
        var vm = new ConsumoViewModel();
        var agora = Hoje.AddHours(3);

        // sem nada lido ainda, e depois sem conversa
        Assert.False(vm.TemSessao);
        Assert.Equal("lendo…", vm.SemSessao);
        vm.MostrarSessao(null, aberta: true, agora);
        Assert.Equal("SESSÃO", vm.TituloDaSessao);
        Assert.Equal("O Claude ainda não respondeu nesta conversa.", vm.SemSessao);
        vm.MostrarSessao(null, aberta: false, agora);
        Assert.Equal("ÚLTIMA SESSÃO", vm.TituloDaSessao);
        Assert.Equal("Nenhuma conversa neste projeto.", vm.SemSessao);

        vm.MostrarSessao(new ConsumoDaSessao("s1", new Uso(12_300, 45_100, 210_000, 950_000, 124), Hoje, Hoje.AddHours(2).AddMinutes(31),
            84_200, 200_000), aberta: true, agora);
        Assert.True(vm.TemSessao);
        Assert.True(vm.TemContexto);
        Assert.Equal("42%", vm.PercentualDoContexto);
        Assert.Equal(42.1, vm.Contexto, 1);
        Assert.Equal("84 mil de 200 mil tokens", vm.TextoDoContexto);
        Assert.False(vm.ContextoAlto);
        Assert.False(vm.ContextoCritico);
        Assert.Equal("1,2 mi", vm.TotalDaSessao);
        Assert.Equal(new double[] { 12_300, 45_100, 210_000, 950_000 }, vm.Partes);
        Assert.Equal(new LinhaDeTokens("12 mil", "1%"), vm.Entrada);
        Assert.Equal(new LinhaDeTokens("45 mil", "4%"), vm.Saida);
        Assert.Equal(new LinhaDeTokens("210 mil", "17%"), vm.CacheGravado);
        Assert.Equal(new LinhaDeTokens("950 mil", "78%"), vm.CacheLido);
        Assert.Equal("cache 81% · 9,8 mil por resposta", vm.ResumoDaSessao);
        Assert.Equal("124 respostas · desde 09:00 · 2h31", vm.AndamentoDaSessao);

        // acima do que o cia retoma sozinho fica amarelo e diz o que isso muda; perto do fim, vermelho
        vm.MostrarSessao(new ConsumoDaSessao("s1", new Uso(1, 1, 1, 180_000, 1), Hoje.AddDays(-1), Hoje.AddDays(-1), 180_000, 200_000),
            aberta: false, agora);
        Assert.True(vm.ContextoAlto);
        Assert.True(vm.ContextoCritico);
        Assert.Equal("90%", vm.PercentualDoContexto);
        Assert.Contains("150 mil tokens", vm.AvisoDoContexto);
        Assert.Equal("1 resposta · desde 12/11 09:00 · 0 min", vm.AndamentoDaSessao);
        Assert.Equal(new LinhaDeTokens("1", "<1%"), vm.Entrada); // o que existe não vira zero

        // o projeto, dia a dia
        vm.MostrarProjeto(new ConsumoDoProjeto(new long[] { 0, 1_200_000, 84_200 }, Hoje.Date.AddDays(-2)), agora);
        Assert.True(vm.TemProjeto);
        Assert.Equal("PROJETO · 3 DIAS", vm.TituloDoProjeto);
        Assert.Equal("1,3 mi", vm.TotalDoProjeto);
        Assert.Equal("11/11", vm.PrimeiroDia);
        Assert.Equal(new[] { "11/11: 0", "12/11: 1,2 mi", "hoje: 84 mil" }, vm.RotulosDosDias);

        // os limites: verde, amarelo da metade em diante, vermelho de 80% em diante
        Assert.Equal("lendo…", vm.AvisoDosLimites);
        vm.MostrarLimites(new Limites(new[]
        {
            new JanelaDoPlano("Sessão (5h)", 64, agora.AddHours(3).AddMinutes(12), null),
            new JanelaDoPlano("Semana (7d)", 23.4, agora.AddDays(2), null),
            new JanelaDoPlano("Semana Sonnet", 81.5, null, "limite semanal"),
        }, agora, null, "Max"), agora);
        Assert.True(vm.TemPlano);
        Assert.Equal("Max", vm.Plano);
        Assert.Equal("", vm.AvisoDosLimites);
        Assert.False(vm.TemAvisoDosLimites);
        Assert.Equal(new[] { "64%", "23%", "82%" }, vm.Limites.Select(l => l.Percentual));
        Assert.Equal(new[] { "zera em 3h12", "zera 15/11 12:00", "" }, vm.Limites.Select(l => l.Zera));
        Assert.Equal(new[] { true, false, false }, vm.Limites.Select(l => l.Alerta));
        Assert.Equal(new[] { false, false, true }, vm.Limites.Select(l => l.Critico));
        Assert.Equal(new[] { false, false, true }, vm.Limites.Select(l => l.TemBloqueio));

        // a consulta de agora não saiu: fica a última resposta, com a idade e o porquê
        vm.MostrarLimites(new Limites(new[] { new JanelaDoPlano("Sessão (5h)", 64, null, null) }, agora.AddMinutes(-12),
            "a conta limitou as consultas", "Max"), agora);
        Assert.Equal("a conta limitou as consultas · lido há 12 min", vm.AvisoDosLimites);
        vm.MostrarLimites(new Limites(Array.Empty<JanelaDoPlano>(), null, "sem login do Claude nesta conta", ""), agora);
        Assert.False(vm.TemLimites);
        Assert.False(vm.TemPlano);
        Assert.Equal("sem login do Claude nesta conta", vm.AvisoDosLimites);
    }

    [Fact]
    public void AsBarrasDividemALarguraEAAltura()
    {
        // a barra dos tokens: cada parte na proporção dela, e a que não tem nada não ocupa lugar
        var fatias = BarraEmpilhada.Fatias(new double[] { 10, 0, 30, 60 }, 200);
        Assert.Equal(new[] { (0d, 20d), (20d, 0d), (20d, 60d), (80d, 120d) }, fatias);
        Assert.All(BarraEmpilhada.Fatias(new double[] { 0, 0 }, 200), f => Assert.Equal(0, f.Largura));
        Assert.Empty(BarraEmpilhada.Fatias(null, 200));

        // as barras dos dias: a maior ocupa a altura toda, e o dia vazio fica com a marca mínima
        var barras = BarrasPorDia.Barras(new long[] { 0, 50, 100 }, new Size(96, 40));
        Assert.Equal(3, barras.Count);
        Assert.Equal(new Rect(0, 38, 30, 2), barras[0]);
        Assert.Equal(new Rect(33, 20, 30, 20), barras[1]);
        Assert.Equal(new Rect(66, 0, 30, 40), barras[2]);
        Assert.Empty(BarrasPorDia.Barras(Array.Empty<long>(), new Size(96, 40)));
    }
}

/// <summary>
/// Os limites do plano, que vêm da conta: nenhum teste chega a ela. O que importa aqui é quantas
/// vezes ela seria consultada, porque o endpoint responde 429 a quem insiste.
/// </summary>
public class LimitesTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 13, 12, 0, 0);

    private readonly string _raiz = Directory.CreateTempSubdirectory("gterm-limites-").FullName;
    private readonly List<string> _consultas = new();
    private LimitesDoPlano.Resposta _resposta = new(200, Transcript.Limites(Agora));

    private string Conta => Path.Combine(_raiz, ".claude");
    private string Estado => Path.Combine(_raiz, "claude-stats");

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private LimitesDoPlano Novo() => new(login =>
    {
        _consultas.Add(login);
        return Task.FromResult(_resposta);
    }, Estado);

    [Fact]
    public async Task ConsultaAContaEGuardaARespostaEmDisco()
    {
        Transcript.Login(Conta, Agora.AddHours(1));
        var limites = Novo();

        var l = await limites.LerAsync(Conta, Agora);
        Assert.Equal(new[] { "segredo" }, _consultas); // o login que o Claude gravou, e só ele
        Assert.Null(l.Erro);
        Assert.Equal("Max", l.Plano);
        Assert.Equal(Agora, l.Em);
        Assert.Equal(new[] { "Sessão (5h)", "Semana (7d)", "Semana Sonnet" }, l.Janelas.Select(j => j.Nome));
        Assert.Equal(new[] { 64, 23, 81.5 }, l.Janelas.Select(j => j.Uso));
        Assert.Equal(Agora.AddHours(3).AddMinutes(12), l.Janelas[0].Zera);
        Assert.Null(l.Janelas[2].Zera);
        Assert.Equal("limite semanal", l.Janelas[2].Bloqueio);
        Assert.Null(l.Janelas[0].Bloqueio);

        // dentro da validade ninguém consulta de novo: nem este, nem outro processo (o ctop, outra
        // janela), porque a resposta está em disco
        Assert.Equal(64, (await limites.LerAsync(Conta, Agora.AddSeconds(90))).Janelas[0].Uso);
        var outro = new LimitesDoPlano(_ => throw new InvalidOperationException("consultou a conta"), Estado);
        var doDisco = await outro.LerAsync(Conta, Agora.AddSeconds(100));
        Assert.Equal(64, doDisco.Janelas[0].Uso);
        Assert.Equal(Agora, doDisco.Em);
        Assert.Single(_consultas);

        // o arquivo é o do ctop, no formato dele: at, data e nextTry
        var arquivo = JsonNode.Parse(File.ReadAllText(limites.Arquivo(Conta)))!;
        Assert.Equal("limits-cache.json", Path.GetFileName(limites.Arquivo(Conta)));
        Assert.Equal(64, arquivo["data"]!["five_hour"]!["utilization"]!.GetValue<double>());
        Assert.Equal(Agora, DateTimeOffset.Parse(arquivo["at"]!.GetValue<string>(), CultureInfo.InvariantCulture).LocalDateTime);
        Assert.NotNull(arquivo["nextTry"]);
        Assert.DoesNotContain("segredo", File.ReadAllText(limites.Arquivo(Conta))); // o login não vai para o arquivo

        // passada a validade, consulta de novo
        await limites.LerAsync(Conta, Agora.AddMinutes(3));
        Assert.Equal(2, _consultas.Count);

        // outra conta tem o arquivo dela
        Assert.Equal("limits-cache-.claude-bm.json", Path.GetFileName(limites.Arquivo(Path.Combine(_raiz, ".claude-bm"))));
    }

    [Fact]
    public async Task DepoisDeUm429EsperaEMostraAUltimaResposta()
    {
        Transcript.Login(Conta, Agora.AddHours(4));
        var limites = Novo();
        await limites.LerAsync(Conta, Agora);

        // a conta manda esperar 10 minutos
        _resposta = new(429, "", TimeSpan.FromMinutes(10));
        var l = await limites.LerAsync(Conta, Agora.AddMinutes(3));
        Assert.Equal("a conta limitou as consultas", l.Erro);
        Assert.Equal(64, l.Janelas[0].Uso); // a de antes continua à vista
        Assert.Equal(Agora, l.Em);          // com a idade dela
        Assert.Equal(2, _consultas.Count);

        // na espera ninguém consulta, e o porquê continua dito
        _resposta = new(200, Transcript.Limites(Agora));
        var naEspera = await limites.LerAsync(Conta, Agora.AddMinutes(12));
        Assert.Equal("a conta limitou as consultas", naEspera.Erro);
        Assert.Equal(2, _consultas.Count);

        // passou: consulta, e o erro sai
        var depois = await limites.LerAsync(Conta, Agora.AddMinutes(14));
        Assert.Null(depois.Erro);
        Assert.Equal(Agora.AddMinutes(14), depois.Em);
        Assert.Equal(3, _consultas.Count);

        // sem o Retry-After são 5 minutos; outro erro, 1
        _resposta = new(429, "");
        await limites.LerAsync(Conta, Agora.AddMinutes(20));
        await limites.LerAsync(Conta, Agora.AddMinutes(24));
        Assert.Equal(4, _consultas.Count);
        _resposta = new(500, "erro");
        Assert.Equal("sem resposta da conta", (await limites.LerAsync(Conta, Agora.AddMinutes(26))).Erro);
        await limites.LerAsync(Conta, Agora.AddMinutes(26.5));
        Assert.Equal(5, _consultas.Count);
        await limites.LerAsync(Conta, Agora.AddMinutes(28));
        Assert.Equal(6, _consultas.Count);
    }

    [Fact]
    public async Task SemLoginOuComEleVencidoNaoConsulta()
    {
        var limites = Novo();

        var sem = await limites.LerAsync(Conta, Agora);
        Assert.Equal("sem login do Claude nesta conta", sem.Erro);
        Assert.Empty(sem.Janelas);
        Assert.Null(sem.Em);
        Assert.Equal("", sem.Plano);

        // vencido, quem renova é o Claude: daqui não sai consulta que só voltaria 401
        Transcript.Login(Conta, Agora.AddMinutes(-5), "pro");
        var vencido = await limites.LerAsync(Conta, Agora.AddMinutes(2));
        Assert.Equal("login vencido: abra o Claude", vencido.Erro);
        Assert.Equal("Pro", vencido.Plano);
        Assert.Empty(_consultas);

        // o Claude renovou: a volta seguinte consulta
        Transcript.Login(Conta, Agora.AddHours(8));
        Assert.Null((await limites.LerAsync(Conta, Agora.AddMinutes(4))).Erro);
        Assert.Single(_consultas);

        // e a conta recusando o login (401) diz a mesma coisa
        _resposta = new(401, "");
        Assert.Equal("login vencido: abra o Claude", (await limites.LerAsync(Conta, Agora.AddMinutes(10))).Erro);
    }

    /// <summary>O arquivo como o ctop o grava (PowerShell, com o fuso e os campos que a conta manda a mais).</summary>
    [Fact]
    public async Task LeOArquivoQueOCtopGravou()
    {
        Transcript.Login(Conta, Agora.AddHours(1));
        Directory.CreateDirectory(Estado);
        File.WriteAllText(Path.Combine(Estado, "limits-cache.json"),
            "{\"at\":\"" + Transcript.Iso(Agora.AddSeconds(-30)) + "\",\"data\":{\"five_hour\":{\"utilization\":24.0,\"resets_at\":\"" +
            Transcript.Iso(Agora.AddHours(4)) + "\",\"limit_dollars\":null,\"locked_reason\":null},\"seven_day\":{\"utilization\":38.0," +
            "\"resets_at\":\"" + Transcript.Iso(Agora.AddDays(5)) + "\",\"locked_reason\":null},\"seven_day_opus\":null," +
            "\"weekly_scoped_shares\":[{\"limit_index\":2}]},\"nextTry\":\"" + Transcript.Iso(Agora.AddSeconds(-30)) + "\"}");

        var l = await Novo().LerAsync(Conta, Agora);
        Assert.Empty(_consultas); // o ctop acabou de consultar
        Assert.Equal(new[] { 24d, 38d }, l.Janelas.Select(j => j.Uso));
        Assert.Equal(Agora.AddHours(4), l.Janelas[0].Zera);
        Assert.Equal(Agora.AddSeconds(-30), l.Em);

        // arquivo torto não derruba nada: é como se não existisse
        File.WriteAllText(Path.Combine(Estado, "limits-cache.json"), "{ quebrado");
        Assert.Equal(64, (await Novo().LerAsync(Conta, Agora)).Janelas[0].Uso);
        Assert.Single(_consultas);
    }

    /// <summary>Sem uma conta de mentira, nos testes a consulta de verdade não sai: volta sem resposta.</summary>
    [Fact]
    public async Task NosTestesAContaDeVerdadeNuncaEConsultada()
    {
        Transcript.Login(Conta, Agora.AddHours(1));
        var l = await new LimitesDoPlano(null, Estado).LerAsync(Conta, Agora);
        Assert.Equal("sem resposta da conta", l.Erro);
        Assert.Empty(l.Janelas);
    }

    [Fact]
    public void QuandoAJanelaZera()
    {
        Assert.Equal("", LimitesDoPlano.QuandoZera(null, Agora));
        Assert.Equal("zera agora", LimitesDoPlano.QuandoZera(Agora.AddSeconds(-1), Agora));
        Assert.Equal("zera em 1 min", LimitesDoPlano.QuandoZera(Agora.AddSeconds(20), Agora));
        Assert.Equal("zera em 42 min", LimitesDoPlano.QuandoZera(Agora.AddMinutes(42), Agora));
        Assert.Equal("zera em 3h12", LimitesDoPlano.QuandoZera(Agora.AddHours(3).AddMinutes(12), Agora));
        Assert.Equal("zera em 23h05", LimitesDoPlano.QuandoZera(Agora.AddHours(23).AddMinutes(5), Agora));
        Assert.Equal("zera 15/11 09:00", LimitesDoPlano.QuandoZera(new DateTime(2026, 11, 15, 9, 0, 0), Agora));
    }
}
