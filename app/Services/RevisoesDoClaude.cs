using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
public sealed record Revisao(string Id, EstadoDaRevisao Estado, TimeSpan Duracao, int Passos);

/// <summary>
/// Acompanha o /code-review pelos arquivos do Claude. Ele roda em segundo plano, como um
/// agente da conversa, em projects\&lt;chave&gt;\&lt;sessão&gt;\subagents: o .forked-skill.json diz
/// qual skill é, o .jsonl cresce a cada passo e o .meta.json ganha finished no fim. Interrompido,
/// ele não marca nada: quem conta é o aviso na conversa principal.
///
/// O agente não diz em que fase da receita está, então não há percentual a mostrar.
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

    // o .jsonl cresce a cada passo: só o que chegou desde a última leitura é lido
    private readonly Dictionary<string, (long Lido, int Passos)> _lidos = new(StringComparer.OrdinalIgnoreCase);

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
        return new Revisao(agente, estado, fim - marca.LastWriteTime, passos);
    }

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
