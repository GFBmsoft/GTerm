using System;
using System.Collections.Generic;
using System.Linq;

namespace GTerm.Services;

/// <summary>
/// Cores dos grupos. São pontos de 7px que aparecem nos dois temas, então os tons
/// são médios — nem claros demais para o fundo branco, nem escuros para o preto.
/// </summary>
public static class GroupPalette
{
    public static readonly string[] Cores =
    {
        "#2F7BE8", // azul
        "#1F9D55", // verde
        "#E07B00", // laranja
        "#D93F3F", // vermelho
        "#8B5CF6", // roxo
        "#0E9384", // turquesa
        "#B08500", // mostarda
        "#DB4C9B", // rosa
        "#6B7280", // cinza
    };

    public static string Padrao => Cores[0];

    /// <summary>
    /// A cor de quem não escolheu uma, tirada do nome: estável entre execuções (o
    /// GetHashCode não é) e a mesma conta do GTest, para o projeto ter a mesma cor nos dois.
    /// </summary>
    public static string DoNome(string nome) => Cores[(nome ?? "").Sum(c => c) % Cores.Length];

    /// <summary>Aceita "#RGB", "#RRGGBB" e nomes já normalizados; devolve null se não for cor.</summary>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        if (t[0] != '#') t = "#" + t;

        if (t.Length == 4 && t.Skip(1).All(Uri.IsHexDigit))
            return $"#{t[1]}{t[1]}{t[2]}{t[2]}{t[3]}{t[3]}".ToUpperInvariant();

        if (t.Length == 7 && t.Skip(1).All(Uri.IsHexDigit))
            return t.ToUpperInvariant();

        return null;
    }
}
