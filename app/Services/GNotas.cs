using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GTerm.Services;

/// <summary>Um item da nota: a linha "- " na margem, com o que vem depois dela recuado.</summary>
/// <param name="Secao">O título "## " de cima (Melhorias, Bugs); vazio antes do primeiro.</param>
/// <param name="Texto">A linha sem o traço, sem a marca de feito e sem o ";" do fim.</param>
/// <param name="Feito">Marcado com ✅ (ou [x], como tarefa): já foi feito.</param>
public sealed record ItemDaNota(string Secao, string Texto, bool Feito);

/// <summary>A nota de um projeto no caderno Projetos do GNotas.</summary>
public sealed record NotaDoProjeto(string Arquivo, IReadOnlyList<ItemDaNota> Itens)
{
    public int Pendentes => Itens.Count(i => !i.Feito);
    public int Feitos => Itens.Count(i => i.Feito);
}

/// <summary>
/// O caderno Projetos do GNotas (D:\Projetos\Private\GNotas): o que falta em cada programa, numa
/// nota com o nome dele (Projetos\GTerm.md), em "## Melhorias" e "## Bugs". Cada item é uma linha
/// "- " na margem, e o recuado embaixo dela é detalhe dele, com as imagens na galeria do GNotas
/// (a pasta anexos). Feito, o item ganha um ✅ no começo, e as imagens dele saem da galeria: quem
/// faz isso é a skill /gnotas, que o botão Notas abre. O GTerm só lê, para o quadro e o botão.
/// </summary>
public static class GNotas
{
    /// <summary>O caderno das coisas a fazer de cada programa.</summary>
    public const string Caderno = "Projetos";

    /// <summary>A marca de feito, no começo do item.</summary>
    public const string Feito = "✅";

    /// <summary>Vazia é a pasta das notas do próprio GNotas; nos testes, uma pasta temporária.</summary>
    public static string PastaPadrao() =>
        Cia.PastaDeTeste is { } teste ? Path.Combine(teste, "gnotas") : "";

    /// <summary>
    /// A pasta das notas: a do workspace, se houver; senão a das configurações do GNotas
    /// (%LOCALAPPDATA%\GNotas\config.json), e sem elas a de fábrica dele (Documentos\GNotas).
    /// Null quando não existe nenhuma. Nos testes nunca chega às do usuário.
    /// </summary>
    public static string? Pasta(string? doWorkspace)
    {
        if (doWorkspace is { Length: > 0 }) return Directory.Exists(doWorkspace) ? doWorkspace : null;
        if (Cia.PastaDeTeste is not null) return null;

        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GNotas", "config.json");
        try
        {
            if (File.Exists(config) &&
                JsonNode.Parse(Compartilhado.Texto(config))?["PastaDasNotas"] is JsonValue v &&
                v.TryGetValue<string>(out var pasta) && Directory.Exists(pasta))
                return pasta;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // configuração ilegível: o GNotas também cai na de fábrica
        }

        var deFabrica = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GNotas");
        return Directory.Exists(deFabrica) ? deFabrica : null;
    }

    /// <summary>
    /// A nota do projeto no caderno Projetos, pelo nome da pasta dele, sem diferença de
    /// maiúsculas (Windock.md é a do WinDock). Null sem caderno ou sem nota.
    /// </summary>
    public static string? NotaDo(string? pastaDasNotas, string projeto)
    {
        if (pastaDasNotas is null) return null;
        var caderno = Path.Combine(pastaDasNotas, Caderno);
        try
        {
            return Directory.Exists(caderno)
                ? Directory.EnumerateFiles(caderno, "*.md").FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), projeto, StringComparison.OrdinalIgnoreCase))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A nota lida; null se ela sumiu ou está presa. O GNotas grava nela: lê sem travar.</summary>
    public static NotaDoProjeto? Ler(string arquivo)
    {
        try
        {
            return new NotaDoProjeto(arquivo, Itens(Compartilhado.Texto(arquivo)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly Regex Item = new(@"^[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Tarefa = new(@"^\[(?<x>[ xX])\]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Os itens da nota: só as linhas de lista na margem. O que vem recuado é detalhe do item de
    /// cima (sub-itens, imagens), e o resto (títulos, parágrafos) não conta.
    /// </summary>
    public static IReadOnlyList<ItemDaNota> Itens(string texto)
    {
        var itens = new List<ItemDaNota>();
        var secao = "";
        foreach (var linha in texto.Replace("\r\n", "\n").Split('\n'))
        {
            if (linha.StartsWith("## ", StringComparison.Ordinal))
            {
                secao = linha[3..].Trim();
                continue;
            }

            if (Item.Match(linha) is not { Success: true } m) continue;
            var resto = m.Groups[1].Value.Trim();
            var feito = false;
            if (resto.StartsWith(Feito, StringComparison.Ordinal))
            {
                feito = true;
                resto = resto[Feito.Length..].TrimStart();
            }
            else if (Tarefa.Match(resto) is { Success: true } t)
            {
                feito = t.Groups["x"].Value != " ";
                resto = resto[t.Length..];
            }

            resto = resto.TrimEnd(';', ' ', '\t');
            if (resto.Length > 0) itens.Add(new ItemDaNota(secao, resto, feito));
        }
        return itens;
    }
}
