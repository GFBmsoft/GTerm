using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GTerm.Services;

public enum EstadoDaRevisao
{
    Rodando,
    Concluida,
    /// <summary>Foi interrompida, ou o agente parou de escrever sem dizer que terminou.</summary>
    Parada,
}

/// <summary>Um /code-review de uma conversa.</summary>
/// <param name="Id">O agente que revisa.</param>
/// <param name="Duracao">Há quanto tempo está rodando, ou quanto levou.</param>
/// <param name="Passos">Ferramentas que o agente usou até agora.</param>
/// <param name="Percentual">100 só quando terminou; antes, a estimativa de <see cref="RevisoesDoClaude.Percentual"/>.</param>
/// <param name="Tipico">Quanto as revisões anteriores levam.</param>
/// <param name="Amostras">Quantas revisões anteriores entraram na conta; zero é o valor padrão.</param>
public sealed record Revisao(string Id, EstadoDaRevisao Estado, TimeSpan Duracao, int Passos, int Percentual,
    TimeSpan Tipico, int Amostras);

/// <summary>O último /code-review terminado de um projeto, em qualquer conversa.</summary>
/// <param name="Quando">Quando terminou.</param>
/// <param name="Resumo">A primeira frase do resultado, como o agente escreveu.</param>
/// <param name="CommitsDepois">Commits que entraram depois: a revisão não os viu.</param>
public sealed record RevisaoDoProjeto(DateTime Quando, string Resumo, int CommitsDepois);

/// <summary>
/// Acompanha o /code-review pelos arquivos do Claude. Ele roda em segundo plano, como um
/// agente da conversa, em projects\&lt;chave&gt;\&lt;sessão&gt;\subagents: o .forked-skill.json diz
/// qual skill é, o .jsonl cresce a cada passo e o .meta.json ganha finished no fim. Interrompido,
/// ele não marca nada: quem conta é o aviso na conversa principal.
///
/// O agente não diz em que fase da receita está: o percentual é uma estimativa, pelo tempo
/// corrido contra a mediana das revisões terminadas. Os passos não servem: vêm em rajadas, e
/// aos 40% do tempo as revisões reais estavam entre 25% e 58% deles.
/// </summary>
public sealed class RevisoesDoClaude
{
    private const string Sufixo = ".forked-skill.json";

    /// <summary>Terminada há mais que isto, a revisão sai da barra.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Sem escrever por mais que isto e sem ter terminado, conta como parada. O agente pensa
    /// calado por mais de um minuto e meio entre um passo e outro.
    /// </summary>
    public static readonly TimeSpan Silencio = TimeSpan.FromMinutes(10);

    /// <summary>A duração esperada enquanto não houver nenhuma revisão terminada.</summary>
    public static readonly TimeSpan SemHistorico = TimeSpan.FromMinutes(6);

    // a primeira linha da receita diz o esforço: `xhigh effort → 10 inline angles → ...
    private static readonly Regex Esforco = new("`([a-z]+) effort", RegexOptions.Compiled);

    // o .jsonl cresce a cada passo: só o que chegou desde a última leitura é lido
    private readonly Dictionary<string, (long Lido, int Passos)> _lidos = new(StringComparer.OrdinalIgnoreCase);

    // calculado uma vez por revisão: com o tempo fixo, o percentual dela nunca volta
    private readonly Dictionary<string, (TimeSpan Tipico, int Amostras)> _tipicos = new();

    // a árvore relê os projetos a cada pouco, e o resultado de uma revisão não muda mais
    private readonly Dictionary<string, (DateTime Quando, string Resumo)> _resumos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RevisaoDoProjeto?> _dosProjetos = new(StringComparer.OrdinalIgnoreCase);

    // o Claude pode estar gravando um arquivo bem na hora da leitura: fica o que já se sabia
    private readonly Dictionary<int, Revisao?> _ultimas = new();

    /// <summary>
    /// A revisão mais nova da conversa aberta no processo. Null sem nenhuma, ou se a última
    /// terminou há mais de <see cref="Validade"/>.
    /// </summary>
    /// <param name="pastaDaConta">A pasta de configuração do Claude (~\.claude).</param>
    /// <param name="pid">O claude.exe da aba.</param>
    /// <param name="pasta">A pasta do projeto, se o Claude não informar a dele.</param>
    public Revisao? Ultima(string pastaDaConta, int pid, string pasta, DateTime agora)
    {
        try
        {
            return _ultimas[pid] = Ler(pastaDaConta, pid, pasta, agora);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return _ultimas.GetValueOrDefault(pid);
        }
    }

    /// <summary>
    /// A última revisão terminada do projeto, de qualquer conversa dele, tenha sido pedida
    /// aqui ou no terminal. Interrompida não conta: não revisou nada até o fim.
    /// </summary>
    public RevisaoDoProjeto? UltimaDoProjeto(string pastaDaConta, string pasta)
    {
        // a árvore relê todos, e escolher um projeto relê o dele: podem cruzar
        lock (_dosProjetos)
        {
            try
            {
                return _dosProjetos[pasta] = LerDoProjeto(pastaDaConta, pasta);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return _dosProjetos.GetValueOrDefault(pasta);
            }
        }
    }

    private RevisaoDoProjeto? LerDoProjeto(string pastaDaConta, string pasta)
    {
        var conversas = Path.Combine(pastaDaConta, "projects", SessoesDoClaude.Chave(pasta));
        if (!Directory.Exists(conversas)) return null;

        FileInfo? ultima = null;
        var pastas = Directory.EnumerateDirectories(conversas).Select(d => Path.Combine(d, "subagents")).Where(Directory.Exists);
        foreach (var agentes in pastas)
            foreach (var marca in new DirectoryInfo(agentes).GetFiles("agent-*" + Sufixo))
                if (DiarioDaTerminada(marca) is { } diario && (ultima is null || diario.LastWriteTime > ultima.LastWriteTime))
                    ultima = diario;

        return ultima is null ? null
            : new RevisaoDoProjeto(ultima.LastWriteTime, Resumo(ultima), Cia.CommitsDepois(pasta, ultima.LastWriteTime));
    }

    // o .jsonl da revisão, se ela for um /code-review e tiver terminado
    private static FileInfo? DiarioDaTerminada(FileInfo marca)
    {
        var nome = Path.Combine(marca.DirectoryName!, marca.Name[..^Sufixo.Length]);
        var meta = nome + ".meta.json";
        var diario = new FileInfo(nome + ".jsonl");
        return File.Exists(meta) && diario.Exists &&
               Cia.Texto(JsonNode.Parse(File.ReadAllText(meta))?["finished"]) == "true" &&
               Cia.Texto(JsonNode.Parse(File.ReadAllText(marca.FullName))?["skillName"]) == "code-review"
            ? diario : null;
    }

    // a última fala do agente é o resultado: ele não tem campos, é texto livre
    private string Resumo(FileInfo diario)
    {
        if (_resumos.TryGetValue(diario.FullName, out var lido) && lido.Quando == diario.LastWriteTime) return lido.Resumo;

        var resumo = "";

        // Enumerable por extenso: num SDK mais novo, .Reverse() num array é o do Span, que não devolve nada
        foreach (var linha in Enumerable.Reverse(SessoesDoClaude.Trecho(diario.FullName, 256 * 1024, inicio: false).Split('\n')))
        {
            if (!linha.Contains("\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                var no = JsonNode.Parse(linha);
                if (Cia.Texto(no?["type"]) != "assistant" || no?["message"]?["content"] is not JsonArray conteudo) continue;

                var fala = string.Join("\n", conteudo.Where(c => Cia.Texto(c?["type"]) == "text").Select(c => Cia.Texto(c?["text"])));
                if (fala.Trim().Length == 0) continue;
                resumo = PrimeiraFrase(fala);
                break;
            }
            catch (JsonException)
            {
                // a primeira linha do trecho vem cortada no meio
            }
        }

        _resumos[diario.FullName] = (diario.LastWriteTime, resumo);
        return resumo;
    }

    /// <summary>A primeira frase, sem a marcação de Markdown, curta o bastante para o quadro.</summary>
    public static string PrimeiraFrase(string texto)
    {
        const int Maximo = 160;

        var limpo = Regex.Replace(texto, "[*`#]", "").Trim().Split('\n')[0].Trim();
        var fim = limpo.IndexOf(". ", StringComparison.Ordinal);
        if (fim > 0) limpo = limpo[..(fim + 1)];
        return limpo.Length > Maximo ? limpo[..(Maximo - 1)].TrimEnd() + "…" : limpo;
    }

    private Revisao? Ler(string pastaDaConta, int pid, string pasta, DateTime agora)
    {
        var aberta = Path.Combine(pastaDaConta, "sessions", pid + ".json");
        if (!File.Exists(aberta)) return null;

        var sessao = JsonNode.Parse(File.ReadAllText(aberta));
        if (Cia.Texto(sessao?["sessionId"]) is not { Length: > 0 } id) return null;
        var cwd = Cia.Texto(sessao?["cwd"]);

        var conversa = Path.Combine(pastaDaConta, "projects", SessoesDoClaude.Chave(cwd.Length > 0 ? cwd : pasta));
        var pastaDosAgentes = Path.Combine(conversa, id, "subagents");
        if (!Directory.Exists(pastaDosAgentes)) return null;

        var marca = new DirectoryInfo(pastaDosAgentes).GetFiles("agent-*" + Sufixo)
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault(f => Cia.Texto(JsonNode.Parse(File.ReadAllText(f.FullName))?["skillName"]) == "code-review");
        if (marca is null) return null;

        var nome = marca.Name[..^Sufixo.Length];
        var agente = nome["agent-".Length..];
        var diario = Path.Combine(pastaDosAgentes, nome + ".jsonl");
        var existe = File.Exists(diario);
        var ultimaEscrita = existe ? File.GetLastWriteTime(diario) : marca.LastWriteTime;
        var passos = existe ? Passos(diario) : 0;

        var meta = Path.Combine(pastaDosAgentes, nome + ".meta.json");
        var terminou = File.Exists(meta) && Cia.Texto(JsonNode.Parse(File.ReadAllText(meta))?["finished"]) == "true";

        var estado = terminou ? EstadoDaRevisao.Concluida
            : agora - ultimaEscrita > Silencio || Interrompida(Path.Combine(conversa, id + ".jsonl"), agente) ? EstadoDaRevisao.Parada
            : EstadoDaRevisao.Rodando;

        if (estado != EstadoDaRevisao.Rodando && agora - ultimaEscrita > Validade) return null;

        var fim = estado == EstadoDaRevisao.Rodando ? agora : ultimaEscrita;
        var duracao = fim - marca.LastWriteTime;

        if (!_tipicos.TryGetValue(agente, out var tipico))
            tipico = _tipicos[agente] = TempoTipico(pastaDaConta, existe ? EsforcoDe(diario) : "");

        return new Revisao(agente, estado, duracao, passos,
            terminou ? 100 : Percentual(duracao, tipico.Tipico), tipico.Tipico, tipico.Amostras);
    }

    /// <summary>
    /// Quanto da duração esperada já passou: reto até 90% e, daí em diante, cada vez mais
    /// devagar, para seguir andando numa revisão mais longa que as outras. Nunca chega a 100:
    /// esse é o da revisão que terminou de verdade.
    /// </summary>
    public static int Percentual(TimeSpan decorrido, TimeSpan tipico)
    {
        var fracao = decorrido / tipico;
        var estimativa = fracao <= 0.9 ? fracao * 100 : 90 + 9 * (1 - Math.Exp(-2 * (fracao - 0.9)));
        return (int)Math.Clamp(estimativa, 1, 99);
    }

    /// <summary>
    /// A mediana das revisões terminadas da conta, as do mesmo esforço se houver: a demora
    /// muda com ele. Parada não entra, que não diz quanto levaria.
    /// </summary>
    private static (TimeSpan Tipico, int Amostras) TempoTipico(string pastaDaConta, string esforco)
    {
        var projetos = Path.Combine(pastaDaConta, "projects");
        if (!Directory.Exists(projetos)) return (SemHistorico, 0);

        var terminadas = new List<(string Esforco, TimeSpan Duracao)>();
        var pastas = Directory.EnumerateDirectories(projetos).SelectMany(Directory.EnumerateDirectories)
            .Select(d => Path.Combine(d, "subagents")).Where(Directory.Exists);
        foreach (var pasta in pastas)
            foreach (var marca in new DirectoryInfo(pasta).GetFiles("agent-*" + Sufixo))
            {
                if (DiarioDaTerminada(marca) is not { } diario) continue;

                var duracao = diario.LastWriteTime - marca.LastWriteTime;
                if (duracao > TimeSpan.Zero) terminadas.Add((EsforcoDe(diario.FullName), duracao));
            }

        var mesmas = terminadas.Where(t => t.Esforco == esforco).ToList();
        var duracoes = (mesmas.Count > 0 ? mesmas : terminadas).Select(t => t.Duracao).OrderBy(d => d).ToList();
        if (duracoes.Count == 0) return (SemHistorico, 0);

        var meio = duracoes.Count / 2;
        return (duracoes.Count % 2 == 1 ? duracoes[meio] : (duracoes[meio - 1] + duracoes[meio]) / 2, duracoes.Count);
    }

    private static string EsforcoDe(string diario) =>
        Esforco.Match(SessoesDoClaude.Trecho(diario, 4096, inicio: true)) is { Success: true } m ? m.Groups[1].Value : "";

    // só as respostas do agente: os registros dos pedidos à API repetem a conversa inteira
    private int Passos(string diario)
    {
        var (lido, passos) = _lidos.GetValueOrDefault(diario);

        using var fluxo = new FileStream(diario, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fluxo.Length <= lido) return passos;

        fluxo.Seek(lido, SeekOrigin.Begin);
        var novos = new byte[fluxo.Length - lido];
        var n = 0;
        while (n < novos.Length && fluxo.Read(novos, n, novos.Length - n) is > 0 and var r) n += r;

        // a linha final pode estar pela metade enquanto o Claude grava: fica para a próxima
        var fim = Array.LastIndexOf(novos, (byte)'\n', n - 1);
        if (fim < 0) return passos;

        foreach (var linha in Encoding.UTF8.GetString(novos, 0, fim).Split('\n'))
        {
            if (!linha.Contains("\"tool_use\"", StringComparison.Ordinal)) continue;
            try
            {
                var no = JsonNode.Parse(linha);
                if (Cia.Texto(no?["type"]) != "assistant" || no?["message"]?["content"] is not JsonArray conteudo) continue;
                passos += conteudo.Count(c => Cia.Texto(c?["type"]) == "tool_use");
            }
            catch (JsonException)
            {
                // linha estragada não atrapalha a contagem das outras
            }
        }

        _lidos[diario] = (lido + fim + 1, passos);
        return passos;
    }

    // o aviso do fim do agente, no fim da conversa principal: killed quando foi parado
    private static bool Interrompida(string conversa, string agente)
    {
        if (!File.Exists(conversa)) return false;

        var texto = SessoesDoClaude.Trecho(conversa, 64 * 1024, inicio: false);
        var i = texto.LastIndexOf(agente + ".output", StringComparison.Ordinal);
        var status = i < 0 ? -1 : texto.IndexOf("<status>", i, StringComparison.Ordinal);
        return status >= 0 && !texto.AsSpan(status + "<status>".Length).StartsWith("completed", StringComparison.Ordinal);
    }

    /// <summary>"40 s" ou "8 min".</summary>
    public static string Tempo(TimeSpan d) =>
        d.TotalSeconds < 60 ? Math.Max(0, (int)d.TotalSeconds) + " s" : (int)d.TotalMinutes + " min";
}
