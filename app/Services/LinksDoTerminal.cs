using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaTerminal;

namespace GTerm.Services;

/// <summary>
/// Ctrl+clique no terminal (pedido do usuário: os caminhos das imagens de antes e depois que o
/// Claude gera). Abre o endereço ou o caminho debaixo do mouse como o Windows abriria: a página
/// no navegador, a pasta no Explorer, o arquivo no programa dele. Caminho só conta se existir:
/// assim uma palavra qualquer não vira link.
/// </summary>
public static class LinksDoTerminal
{
    private static readonly Regex Endereco = new(@"(?:https?|file)://[^\s<>""'`]+", RegexOptions.IgnoreCase);

    // "arquivo.cs:42", "arquivo.cs:42:7", "arquivo.cs#L42": o Claude aponta a linha assim
    private static readonly Regex ComLinha = new(@"^(.+?)(?:(?::\d+){1,2}|#L\d+(?:-L?\d+)?)$");

    // aberto, isto rodaria: só mostra onde está
    private static readonly HashSet<string> Executaveis = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".pif", ".lnk", ".url", ".reg", ".hta", ".cpl", ".msc", ".jar", ".appref-ms",
    };

    /// <summary>O que abrir no ponto do clique, ou null se ali não há link.</summary>
    public static string? Resolver(TerminalLinkContext contexto, string? pasta)
    {
        foreach (var candidato in Candidatos(contexto))
            if (Alvo(candidato, pasta) is { } alvo) return alvo;
        return null;
    }

    /// <summary>Os textos que podem ser o link do clique, do mais provável ao menos.</summary>
    public static IEnumerable<string> Candidatos(TerminalLinkContext contexto)
    {
        var linha = contexto.Line;
        var col = contexto.Column;

        foreach (Match m in Endereco.Matches(linha))
        {
            if (col < m.Index || col >= m.Index + m.Length) continue;
            yield return LimparEndereco(m.Value);
            yield break;
        }

        if (col < 0 || col >= linha.Length || Separa(linha[col])) yield break;

        var inicio = col;
        while (inicio > 0 && !Separa(linha[inicio - 1])) inicio--;
        var fim = col;
        while (fim < linha.Length && !Separa(linha[fim])) fim++;

        var palavra = LimparFim(linha[inicio..fim]);
        if (palavra.Length == 0) yield break;
        yield return palavra;

        // entre aspas ou crases, o nome pode ter espaço: vale o trecho todo
        foreach (var aspa in new[] { '"', '`' })
        {
            var abre = col > 0 ? linha.LastIndexOf(aspa, col - 1) : -1;
            var fecha = linha.IndexOf(aspa, col);
            if (abre >= 0 && fecha > abre + 1) yield return linha[(abre + 1)..fecha].Trim();
        }

        // nome com espaço sem aspas ("C:\Program Files\..."): vai juntando as palavras seguintes
        var ate = fim;
        for (var i = 0; i < 6 && ate < linha.Length && linha[ate] == ' '; i++)
        {
            ate++;
            while (ate < linha.Length && !Separa(linha[ate])) ate++;
            yield return LimparFim(linha[inicio..ate]);
        }

        // o Claude quebra a linha ele mesmo, e um caminho comprido continua na de baixo: no fim
        // da linha, junta o começo da seguinte; no começo, o fim da anterior
        if (SoSeparadores(linha[fim..]) && PrimeiraPalavra(contexto.Below) is { } embaixo)
            yield return LimparFim(linha[inicio..fim] + embaixo);
        if (SoSeparadores(linha[..inicio]) && UltimaPalavra(contexto.Above) is { } emCima)
            yield return LimparFim(emCima + linha[inicio..fim]);
    }

    /// <summary>O endereço como veio, ou o caminho completo do que existe; null se não é nada disso.</summary>
    public static string? Alvo(string texto, string? pasta)
    {
        if (texto.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            texto.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(texto, UriKind.Absolute, out _) ? texto : null;

        if (texto.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
            texto = uri.LocalPath;
        }

        if (ComLinha.Match(texto) is { Success: true } m) texto = m.Groups[1].Value;
        if (texto.Length < 2 || texto.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;

        try
        {
            string? caminho;
            if (texto.StartsWith('~'))
                caminho = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    texto[1..].TrimStart('\\', '/'));
            else if (Path.IsPathRooted(texto))
                caminho = texto;
            // relativo só com cara de caminho: "app\x.cs", "CLAUDE.md"; uma palavra solta que por
            // acaso é o nome de uma pasta do projeto não vira link
            else if (pasta is not null && (texto.IndexOfAny(['\\', '/']) >= 0 || Path.HasExtension(texto)))
                caminho = Path.Combine(pasta, texto);
            else
                return null;

            caminho = Path.GetFullPath(caminho);
            return File.Exists(caminho) || Directory.Exists(caminho) ? caminho : null;
        }
        catch (Exception)
        {
            return null; // caminho mal formado
        }
    }

    /// <summary>Abre como o Windows abriria; o que rodaria (um .exe, um script) só aparece marcado no Explorer.</summary>
    public static void Abrir(string alvo)
    {
        try
        {
            if (File.Exists(alvo) && Executaveis.Contains(Path.GetExtension(alvo)))
                MostrarNaPasta(alvo);
            else
                Process.Start(new ProcessStartInfo(alvo) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // nenhum programa para esse tipo de arquivo: mostra onde ele está
            if (File.Exists(alvo)) MostrarNaPasta(alvo);
        }
    }

    private static void MostrarNaPasta(string arquivo)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + arquivo + "\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // sem o Explorer não há o que fazer
        }
    }

    // espaço, aspas, o que costuma cercar um caminho num texto, e os desenhos do Claude (│ ⎿ ● …)
    private static bool Separa(char c) =>
        char.IsWhiteSpace(c) || "\"'`<>|*?()[]{}".Contains(c) || (c > 0x7F && !char.IsLetterOrDigit(c));

    private static bool SoSeparadores(string texto) => texto.All(Separa);

    // pontuação da frase colada ao caminho: "veja C:\x\a.png." ou "em app\x.cs:"
    private static string LimparFim(string texto) => texto.TrimEnd('.', ',', ';', ':', '!', '?');

    private static string LimparEndereco(string endereco)
    {
        endereco = endereco.TrimEnd('.', ',', ';', ':', '!', '?', '*');
        // o ")" de um link em markdown ou de um parêntese da frase, sem o "(" dentro do endereço
        while (endereco.EndsWith(')') && endereco.Count(c => c == ')') > endereco.Count(c => c == '('))
            endereco = endereco[..^1];
        return endereco;
    }

    private static string? PrimeiraPalavra(string linha)
    {
        var inicio = 0;
        while (inicio < linha.Length && Separa(linha[inicio])) inicio++;
        var fim = inicio;
        while (fim < linha.Length && !Separa(linha[fim])) fim++;
        return fim > inicio ? linha[inicio..fim] : null;
    }

    private static string? UltimaPalavra(string linha)
    {
        var fim = linha.Length;
        while (fim > 0 && Separa(linha[fim - 1])) fim--;
        var inicio = fim;
        while (inicio > 0 && !Separa(linha[inicio - 1])) inicio--;
        return fim > inicio ? linha[inicio..fim] : null;
    }
}
