using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GTerm.Services;

/// <summary>
/// Os tokens de um conjunto de respostas do Claude, crus, como o ctop soma (cada tipo com peso 1).
/// Não é a conta do /usage, que pondera pelo modelo e pelo cache: essa vem de <see cref="LimitesDoPlano"/>.
/// </summary>
public readonly record struct Uso(long Entrada, long Saida, long CacheGravado, long CacheLido, int Respostas)
{
    public long Total => Entrada + Saida + CacheGravado + CacheLido;

    /// <summary>Quanto do que o Claude leu veio do cache (o "cache hit" do ctop); null sem nada lido.</summary>
    public double? AcertoDoCache => Entrada + CacheGravado + CacheLido is > 0 and var lido ? 100d * CacheLido / lido : null;

    public static Uso operator +(Uso a, Uso b) => new(a.Entrada + b.Entrada, a.Saida + b.Saida,
        a.CacheGravado + b.CacheGravado, a.CacheLido + b.CacheLido, a.Respostas + b.Respostas);

    public static Uso operator -(Uso a, Uso b) => new(a.Entrada - b.Entrada, a.Saida - b.Saida,
        a.CacheGravado - b.CacheGravado, a.CacheLido - b.CacheLido, a.Respostas - b.Respostas);
}

/// <summary>Uma conversa: o que ela gastou (com os subagentes dela) e o contexto de agora.</summary>
/// <param name="Contexto">Os tokens que ela relê a cada turno (<see cref="Cia.ContextoDaSessao"/>); null sem resposta ainda.</param>
/// <param name="Janela">O tamanho da janela de contexto, deduzido: ver <see cref="ConsumoDoClaude.Janela"/>.</param>
public sealed record ConsumoDaSessao(string Id, Uso Uso, DateTime? Inicio, DateTime? Ultima, long? Contexto, long Janela);

/// <summary>Os tokens do projeto por dia, do mais antigo até hoje, de todas as conversas dele.</summary>
public sealed record ConsumoDoProjeto(IReadOnlyList<long> PorDia, DateTime PrimeiroDia)
{
    public long Total => PorDia.Sum();
}

/// <summary>
/// O consumo do Claude Code lido dos transcripts (<c>projects\&lt;chave&gt;\*.jsonl</c>), como o ctop
/// (<c>Scripts\claude-stats\claude-stats.ps1</c>, o <c>-Slim</c>) lê: cada resposta traz o bloco
/// <c>usage</c>, repetido em várias linhas com o mesmo <c>message.id</c>. **Mudou lá, muda aqui.**
/// <para>
/// A leitura é incremental: o .jsonl só cresce, então cada arquivo guarda até onde já foi contado e
/// a volta seguinte lê só o que o Claude acrescentou. Foi reler a sessão inteira a cada 3 s que fez
/// o painel lateral do cia ser removido (lento, e o Claude avisava "Transcript writes are failing
/// (EBUSY)"). Pelo mesmo motivo todo arquivo é aberto compartilhando gravação e exclusão.
/// </para>
/// </summary>
public sealed class ConsumoDoClaude
{
    // um .jsonl: a conversa, ou um subagente dela
    private sealed class Diario
    {
        public Diario(DateTime criado) => Criado = criado;

        public DateTime Criado;
        public long Lido;               // o fim da última linha inteira já contada
        public Uso Uso;
        public DateTime? Inicio, Ultima;
        public long MaiorContexto;
        public long UltimoContexto;
        public long ContextoMedidoEm = -1;
        public long? Contexto;
        public readonly Dictionary<DateTime, long> PorDia = new();
    }

    private readonly record struct Resposta(Diario Dono, Uso Uso, DateTime Dia);

    // a pasta de um projeto em projects\: os diários dela e quem contou cada resposta
    private sealed class Pasta
    {
        public readonly Dictionary<string, Diario> Diarios = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Resposta> Respostas = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Pasta> _pastas = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _trava = new();

    private static string PastaDoProjeto(string pastaDaConta, string pasta) =>
        Path.Combine(pastaDaConta, "projects", SessoesDoClaude.Chave(pasta));

    /// <summary>
    /// A conversa do Claude aberto no processo, pelo <c>sessions\&lt;pid&gt;.json</c> da conta, e a pasta
    /// em que ele diz estar. Null enquanto ele não grava o arquivo.
    /// </summary>
    public static (string Id, string Pasta)? SessaoDoProcesso(string pastaDaConta, int pid)
    {
        try
        {
            var arquivo = Path.Combine(pastaDaConta, "sessions", pid + ".json");
            if (!File.Exists(arquivo)) return null;

            var sessao = JsonNode.Parse(Texto(arquivo));
            return Cia.Texto(sessao?["sessionId"]) is { Length: > 0 } id ? (id, Cia.Texto(sessao?["cwd"])) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A conversa mais nova do projeto, para o projeto sem o Claude aberto; null sem nenhuma.</summary>
    public static string? UltimaSessao(string pastaDaConta, string pasta)
    {
        try
        {
            var dir = PastaDoProjeto(pastaDaConta, pasta);
            if (!Directory.Exists(dir)) return null;
            return new DirectoryInfo(dir).GetFiles("*.jsonl").OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.GetFileNameWithoutExtension(f.Name)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// O que a conversa gastou até agora, com os subagentes dela (<c>&lt;id&gt;\subagents</c>), e o
    /// contexto. Null se ela ainda não tem arquivo: o Claude só o cria na primeira mensagem.
    /// </summary>
    public ConsumoDaSessao? Sessao(string pastaDaConta, string pasta, string id)
    {
        var dir = PastaDoProjeto(pastaDaConta, pasta);
        var principal = Path.Combine(dir, id + ".jsonl");

        lock (_trava)
        {
            try
            {
                if (!File.Exists(principal)) return null;

                var p = PastaDe(dir);
                var d = Ler(p, principal);
                var uso = d.Uso;

                var agentes = Path.Combine(dir, id, "subagents");
                if (Directory.Exists(agentes))
                    foreach (var agente in Directory.EnumerateFiles(agentes, "*.jsonl"))
                        uso += Ler(p, agente).Uso;

                // o contexto vem do fim do arquivo, como o quadro do projeto mede (o /compact o
                // derruba sem que nenhuma resposta diga). Só de novo quando o arquivo cresceu
                if (d.ContextoMedidoEm != d.Lido)
                {
                    d.Contexto = Cia.ContextoDaSessao(principal) ?? (d.UltimoContexto > 0 ? d.UltimoContexto : null);
                    d.ContextoMedidoEm = d.Lido;
                }

                return new ConsumoDaSessao(id, uso, d.Inicio, d.Ultima, d.Contexto,
                    Janela(Math.Max(d.MaiorContexto, d.Contexto ?? 0)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Os tokens do projeto em cada um dos últimos <paramref name="dias"/> dias, de todas as
    /// conversas e subagentes dele. Arquivo que não é gravado desde antes do período não tem nada
    /// nele, e nem é aberto: há pastas com gigabytes de conversa antiga.
    /// </summary>
    public ConsumoDoProjeto Projeto(string pastaDaConta, string pasta, int dias, DateTime agora)
    {
        var dir = PastaDoProjeto(pastaDaConta, pasta);
        var primeiro = agora.Date.AddDays(1 - dias);
        var porDia = new long[dias];

        lock (_trava)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    var p = PastaDe(dir);

                    // o mais antigo primeiro: é ele quem conta a resposta que outro copiou
                    foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                                 .Where(f => f.LastWriteTime >= primeiro).OrderBy(f => f.CreationTimeUtc).ToList())
                        Ler(p, f.FullName);

                    foreach (var d in p.Diarios.Values)
                        foreach (var (dia, tokens) in d.PorDia)
                            if ((dia - primeiro).Days is >= 0 and var i && i < dias)
                                porDia[i] += tokens;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // o que deu para ler fica
            }
        }
        return new ConsumoDoProjeto(porDia, primeiro);
    }

    /// <summary>
    /// A janela de contexto não está na conversa (o modelo pedido é o mesmo com 200 mil ou 1 milhão):
    /// é a menor das duas em que cabe o maior contexto que a sessão já teve. Com 190 mil numa janela
    /// de 1 milhão, mostra 200 mil: é o que dá para saber. É a regra do Get-CtxLimit do ctop, que
    /// até 13/11/2026 tinha um degrau de 500 mil no meio: essa janela não existe, e com 486 mil os
    /// dois diziam 97% de uma conversa que estava na metade. Lá entra o contexto de agora; aqui, o
    /// maior que a sessão já teve, que não volta para 200 mil depois de um /compact.
    /// </summary>
    public static long Janela(long maiorContexto)
    {
        foreach (var janela in new[] { 200_000L, 1_000_000L })
            if (maiorContexto <= janela)
                return janela;
        return (long)Math.Ceiling(maiorContexto / 500_000d) * 500_000;
    }

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"850", "8,4 mil", "84 mil", "1,2 mi", "1,18 bi": o Format-Num do ctop, em português.</summary>
    public static string Numero(long n) => n switch
    {
        < 1_000 => n.ToString(Brasil),
        < 10_000 => (n / 1_000d).ToString("0.#", Brasil) + " mil",
        < 999_500 => Math.Round(n / 1_000d).ToString("0", Brasil) + " mil", // acima disso arredondaria para "1000 mil"
        < 100_000_000 => (n / 1_000_000d).ToString("0.#", Brasil) + " mi",
        < 999_500_000 => Math.Round(n / 1_000_000d).ToString("0", Brasil) + " mi",
        _ => (n / 1_000_000_000d).ToString("0.##", Brasil) + " bi",
    };

    private Pasta PastaDe(string dir) => _pastas.TryGetValue(dir, out var p) ? p : _pastas[dir] = new Pasta();

    // um arquivo pequeno inteiro, sem travar quem grava nele
    private static string Texto(string arquivo)
    {
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }

    // uma linha de tool result com um arquivo embutido passa de megabytes; acima disto não é
    // resposta do Claude, e é pulada sem ir para a memória
    private const int MaiorLinha = 64 * 1024 * 1024;

    // conta o que foi acrescentado ao arquivo desde a última leitura, linha inteira por linha inteira
    private static Diario Ler(Pasta p, string caminho)
    {
        if (!p.Diarios.TryGetValue(caminho, out var d))
            p.Diarios[caminho] = d = new Diario(File.GetCreationTimeUtc(caminho));

        FileStream fluxo;
        try
        {
            fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return d; // apagado entre a listagem e a leitura: vale o que já tinha sido contado
        }

        using (fluxo)
        {
            // o tamanho deste instante: o Claude pode estar gravando, e o que entrar depois fica
            // para a próxima volta
            var fim = fluxo.Length;
            if (fim < d.Lido) Esquecer(p, d); // não é mais o mesmo arquivo
            if (fim == d.Lido) return d;

            fluxo.Seek(d.Lido, SeekOrigin.Begin);
            var buffer = new byte[1 << 18];
            var usados = 0;
            var posicao = d.Lido;
            var pulando = false;

            while (posicao < fim)
            {
                if (usados == buffer.Length)
                {
                    if (buffer.Length >= MaiorLinha)
                    {
                        d.Lido += usados;
                        usados = 0;
                        pulando = true;
                    }
                    else
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }
                }

                var lidos = fluxo.Read(buffer, usados, (int)Math.Min(buffer.Length - usados, fim - posicao));
                if (lidos <= 0) break;
                posicao += lidos;
                usados += lidos;

                var inicio = 0;
                int quebra;
                while ((quebra = Array.IndexOf(buffer, (byte)'\n', inicio, usados - inicio)) >= 0)
                {
                    if (pulando) pulando = false; // o resto da linha gigante
                    else Linha(p, d, new ReadOnlyMemory<byte>(buffer, inicio, quebra - inicio));
                    inicio = quebra + 1;
                }

                // a linha pela metade vai para o começo e espera o resto
                d.Lido += inicio;
                usados -= inicio;
                if (pulando)
                {
                    d.Lido += usados;
                    usados = 0;
                }
                else if (usados > 0 && inicio > 0)
                {
                    Buffer.BlockCopy(buffer, inicio, buffer, 0, usados);
                }
            }
        }
        return d;
    }

    private static void Linha(Pasta p, Diario d, ReadOnlyMemory<byte> linha)
    {
        // quase tudo no arquivo não é resposta: nem vira JSON
        var bytes = linha.Span;
        if (bytes.IndexOf("\"usage\":{"u8) < 0 || bytes.IndexOf("\"type\":\"assistant\""u8) < 0) return;

        try
        {
            using var doc = JsonDocument.Parse(linha);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object ||
                !raiz.TryGetProperty("type", out var tipo) || tipo.ValueKind != JsonValueKind.String || tipo.GetString() != "assistant" ||
                !raiz.TryGetProperty("message", out var mensagem) || mensagem.ValueKind != JsonValueKind.Object ||
                !mensagem.TryGetProperty("usage", out var uso) || uso.ValueKind != JsonValueKind.Object)
                return;

            long Valor(string nome) =>
                uso.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

            var resposta = new Uso(Valor("input_tokens"), Valor("output_tokens"), Valor("cache_creation_input_tokens"),
                Valor("cache_read_input_tokens"), 1);
            if (resposta.Total == 0) return; // a resposta sintética de um erro da API

            var quando = raiz.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String &&
                         DateTimeOffset.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lido)
                ? lido.LocalDateTime
                : (DateTime?)null;

            var lateral = raiz.TryGetProperty("isSidechain", out var s) && s.ValueKind == JsonValueKind.True;
            var id = mensagem.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            Contar(p, d, id, resposta, quando, lateral);
        }
        catch (JsonException)
        {
            // linha cortada ou torta: não é dela que vem o consumo
        }
    }

    private static void Contar(Pasta p, Diario d, string? id, Uso uso, DateTime? quando, bool lateral)
    {
        if (id is not null && p.Respostas.TryGetValue(id, out var contada))
        {
            // sessão bifurcada copia o histórico para um .jsonl novo: a resposta é de quem a teve
            // primeiro, seja qual for a ordem em que os arquivos são lidos
            if (contada.Dono != d && contada.Dono.Criado <= d.Criado) return;

            // do mesmo arquivo (a resposta vem em várias linhas, e a última traz o uso completo) ou
            // de um mais novo, que a copiou: sai de lá e entra aqui
            contada.Dono.Uso -= contada.Uso;
            if (contada.Dono.PorDia.TryGetValue(contada.Dia, out var tinha))
                contada.Dono.PorDia[contada.Dia] = tinha - contada.Uso.Total;
        }

        var dia = (quando ?? DateTime.MinValue).Date;
        d.Uso += uso;
        d.PorDia[dia] = d.PorDia.GetValueOrDefault(dia) + uso.Total;
        if (id is not null) p.Respostas[id] = new Resposta(d, uso, dia);

        if (quando is { } q)
        {
            if (d.Inicio is null || q < d.Inicio) d.Inicio = q;
            if (d.Ultima is null || q > d.Ultima) d.Ultima = q;
        }

        if (lateral) return;
        d.UltimoContexto = uso.Entrada + uso.CacheGravado + uso.CacheLido;
        d.MaiorContexto = Math.Max(d.MaiorContexto, d.UltimoContexto);
    }

    private static void Esquecer(Pasta p, Diario d)
    {
        foreach (var id in p.Respostas.Where(r => r.Value.Dono == d).Select(r => r.Key).ToList()) p.Respostas.Remove(id);
        d.Lido = 0;
        d.Uso = default;
        d.Inicio = d.Ultima = null;
        d.MaiorContexto = d.UltimoContexto = 0;
        d.ContextoMedidoEm = -1;
        d.Contexto = null;
        d.PorDia.Clear();
    }
}
