using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GTerm.Services;

/// <summary>Uma pasta de projeto dentro de uma das bases do cia.</summary>
/// <param name="Base">A pasta de onde ele veio (D:\Projetos\DBISAM, D:\Projetos\Private...).</param>
/// <param name="Tipo">delphi, csharp, go, node, python, pwsh ou pasta: decide o ícone.</param>
public sealed record ProjetoDoCia(string Nome, string Pasta, string Base, string Tipo);

/// <summary>Um projeto aberto pelo cia: entra na lista de recentes.</summary>
/// <param name="Referencia">O caminho do projeto; no formato antigo do estado, só o nome.</param>
public sealed record Recente(string Referencia, DateTime? Quando, string Pedido, string Modo);

public sealed record EstadoDoCia(IReadOnlyList<string> Favoritos, IReadOnlyList<Recente> Recentes)
{
    public static readonly EstadoDoCia Vazio = new(Array.Empty<string>(), Array.Empty<Recente>());
}

/// <summary>A sessão do Claude que o cia retoma no projeto.</summary>
public sealed record UltimaSessao(string Id, DateTime Quando, long KB, string Titulo);

/// <summary>O git do projeto, como o quadro do dashboard do cia mostra (o Get-GitInfo dele).</summary>
/// <param name="Repositorio">A pasta tem .git.</param>
/// <param name="Erro">O git recusou a pasta: quase sempre o dubious ownership (safe.directory).</param>
/// <param name="Alterados">Arquivos alterados ou novos, como no git status.</param>
/// <param name="Commit">Quando foi o último commit; null num repositório sem commit.</param>
public sealed record InfoDoGit(bool Repositorio, bool Erro, string Branch, int Alterados, int Ahead, int Behind,
    DateTime? Commit, string Mensagem)
{
    public static readonly InfoDoGit SemGit = new(false, false, "", 0, 0, 0, null, "");
    public static readonly InfoDoGit Recusado = SemGit with { Repositorio = true, Erro = true };
}

/// <summary>Como o Claude abre no projeto, decidido como o cia decidiria.</summary>
/// <param name="Argumentos">Da linha de comando do claude: --resume, /compact, a skill.</param>
/// <param name="Modo">O que vai para o recente (retomada, writer, memória, pr, limpo).</param>
/// <param name="Pedido">O número do pedido do Mantis, ou vazio.</param>
public sealed record Abertura(string Argumentos, string Modo, string Pedido);

/// <summary>
/// O que o GTerm sabe do cia (o atalho do perfil, em Modules\aliases.ps1, e o dashboard
/// Scripts\cia\cia-dash.ps1): de onde vêm os projetos, os favoritos e recentes que os dois
/// dividem, a sessão que ele retoma e como ele abre o Claude. O GTerm abre o Claude direto,
/// sem shell, com as mesmas decisões; o cia no terminal continua valendo.
/// </summary>
public static class Cia
{
    /// <summary>Acima disso o cia retoma a sessão já mandando /compact ($global:CiaCompactKB).</summary>
    public const int CompactarKB = 300;

    /// <summary>Desvio usado só pelos testes, para nunca lerem nem gravarem o estado real do cia.</summary>
    internal static string? PastaDeTeste { get; set; }

    /// <summary>As bases do cia ($global:CiaBases): a primeira é a da BMSoft.</summary>
    public static List<string> BasesPadrao() =>
        PastaDeTeste is null ? new() { @"D:\Projetos\DBISAM", @"D:\Projetos\Private" } : new();

    public static string EstadoPadrao() => PastaDeTeste is { } teste
        ? Path.Combine(teste, "estado.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Scripts", "cia", "estado.json");

    /// <summary>
    /// As subpastas de cada base, na ordem das bases e por nome dentro de cada uma, como no
    /// dashboard do cia. Pasta oculta (.git, .vs) não é projeto; base que não existe nesta
    /// máquina fica de fora.
    /// </summary>
    public static IReadOnlyList<ProjetoDoCia> Projetos(IEnumerable<string> bases)
    {
        var lista = new List<ProjetoDoCia>();
        foreach (var b in bases.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim().TrimEnd('\\', '/')))
        {
            List<DirectoryInfo> pastas;
            try
            {
                pastas = new DirectoryInfo(b).GetDirectories()
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            lista.AddRange(pastas.Select(d => new ProjetoDoCia(d.Name, d.FullName, b, Tipo(d.FullName))));
        }
        return lista;
    }

    /// <summary>O tipo do projeto pelos arquivos da raiz, nos mesmos critérios do cia.</summary>
    public static string Tipo(string pasta)
    {
        string[] arquivos;
        try
        {
            arquivos = Directory.GetFiles(pasta).Select(Path.GetFileName).ToArray()!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "pasta";
        }

        bool Termina(params string[] finais) =>
            arquivos.Any(a => finais.Any(f => a.EndsWith(f, StringComparison.OrdinalIgnoreCase)));
        bool Existe(string nome) => arquivos.Any(a => a.Equals(nome, StringComparison.OrdinalIgnoreCase));

        if (Termina(".dproj", ".dpr", ".pas")) return "delphi";
        if (Termina(".sln", ".csproj")) return "csharp";
        if (Existe("go.mod")) return "go";
        if (Existe("package.json")) return "node";
        if (Termina(".py")) return "python";
        if (Termina(".ps1")) return "pwsh";
        return "pasta";
    }

    /// <summary>
    /// O ícone do tipo, em Nerd Font, com a cor do tema do Terminal-Icons (o mesmo do ls e
    /// do dashboard do cia). Delphi não existe lá: é o "D" em caixa.
    /// </summary>
    public static (string Glifo, string Cor) Icone(string tipo) => tipo switch
    {
        "delphi" => (char.ConvertFromUtf32(0xF0B0B), "#EE5F5B"),
        "csharp" => ("\uE70C", "#EE82EE"),
        "go" => ("\uE724", "#20B2AA"),
        "node" => ("\uE718", "#6B8E23"),
        "python" => ("\uE73C", "#4B8BBE"),
        "pwsh" => (char.ConvertFromUtf32(0xF07B7), "#00BFFF"),
        _ => ("\uF413", "#6C7086"),
    };

    /// <summary>O nome do tipo, ao lado da pasta no quadro do projeto.</summary>
    public static string NomeDoTipo(string tipo) => tipo switch
    {
        "delphi" => "Delphi",
        "csharp" => "C# / .NET",
        "go" => "Go",
        "node" => "Node",
        "python" => "Python",
        "pwsh" => "PowerShell",
        _ => "pasta",
    };

    /// <summary>
    /// Favoritos e recentes do estado do cia. Null quando o arquivo existe mas não se lê (o
    /// cia gravando naquele instante): quem chama fica com o que já tinha.
    /// </summary>
    public static EstadoDoCia? LerEstado(string arquivo)
    {
        JsonNode? raiz;
        try
        {
            if (!File.Exists(arquivo)) return EstadoDoCia.Vazio;
            raiz = JsonNode.Parse(File.ReadAllText(arquivo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        var favoritos = Itens(raiz?["favoritos"]).Select(Texto).Where(t => t.Length > 0).ToList();
        var recentes = Itens(raiz?["recentes"])
            .Select(n => new Recente(
                Texto(n?["caminho"]) is { Length: > 0 } caminho ? caminho : Texto(n?["nome"]),
                DateTime.TryParse(Texto(n?["quando"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var q)
                    ? q
                    : null,
                Texto(n?["pedido"]),
                Texto(n?["modo"])))
            .Where(r => r.Referencia.Length > 0)
            .ToList();
        return new EstadoDoCia(favoritos, recentes);
    }

    /// <summary>
    /// Troca a lista de favoritos e deixa o resto do arquivo como estava (os recentes, as
    /// sessões do cim).
    /// </summary>
    public static void GravarFavoritos(string arquivo, IEnumerable<string> pastas)
    {
        var raiz = Abrir(arquivo);
        raiz["favoritos"] = new JsonArray(pastas.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        Gravar(arquivo, raiz);
    }

    /// <summary>
    /// Põe o projeto na frente dos recentes, como o <c>cia-dash -Registrar</c>: um por pasta
    /// (no formato antigo, por nome) e oito no máximo.
    /// </summary>
    public static void RegistrarRecente(string arquivo, string pasta, string pedido, string modo, DateTime quando)
    {
        var raiz = Abrir(arquivo);
        var nome = Path.GetFileName(pasta.TrimEnd('\\', '/'));
        bool Mesmo(JsonNode? n) => Texto(n?["caminho"]) is { Length: > 0 } caminho
            ? caminho.Equals(pasta, StringComparison.OrdinalIgnoreCase)
            : Texto(n?["nome"]).Equals(nome, StringComparison.OrdinalIgnoreCase);

        var novo = new JsonObject
        {
            ["caminho"] = pasta,
            ["nome"] = nome,
            ["quando"] = quando.ToString("s", CultureInfo.InvariantCulture),
            ["pedido"] = pedido,
            ["modo"] = modo,
        };
        var antigos = Itens(raiz["recentes"]).Where(n => n is not null && !Mesmo(n)).Take(7).Select(n => n!.DeepClone());
        raiz["recentes"] = new JsonArray(new JsonNode?[] { novo }.Concat(antigos).ToArray());
        Gravar(arquivo, raiz);
    }

    // arquivo que não se lê não é sobrescrito: a exceção sobe e o resto do estado fica
    private static JsonObject Abrir(string arquivo) => File.Exists(arquivo)
        ? JsonNode.Parse(File.ReadAllText(arquivo)) as JsonObject ?? new JsonObject()
        : new JsonObject();

    // em .tmp e renomeia, como o workspace: o cia nunca lê um arquivo pela metade
    private static void Gravar(string arquivo, JsonObject raiz)
    {
        if (Path.GetDirectoryName(arquivo) is { Length: > 0 } pasta) Directory.CreateDirectory(pasta);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, raiz.ToJsonString(Json), new UTF8Encoding(false));
        File.Move(tmp, arquivo, overwrite: true);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static IEnumerable<JsonNode?> Itens(JsonNode? n) => n switch
    {
        JsonArray lista => lista,
        null => Array.Empty<JsonNode?>(),
        _ => new[] { n }, // o ConvertTo-Json do PowerShell grava lista de um item só como o item
    };

    private static string Texto(JsonNode? n) => n is JsonValue v
        ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()).Trim()
        : "";

    /// <summary>
    /// A sessão que o cia retoma (Get-UltimaSessao do cia-dash): das dez mais recentes, a
    /// primeira que teve conversa. Aberta e fechada sem nada (sem título nem prompt) não conta.
    /// </summary>
    /// <param name="pastaDaConta">A pasta de configuração do Claude (~\.claude).</param>
    public static UltimaSessao? UltimaSessao(string pasta, string pastaDaConta)
    {
        var dir = Path.Combine(pastaDaConta, "projects", Regex.Replace(pasta, "[^A-Za-z0-9]", "-"));
        List<FileInfo> arquivos;
        try
        {
            if (!Directory.Exists(dir)) return null;
            arquivos = new DirectoryInfo(dir).GetFiles("*.jsonl")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(10)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var f in arquivos)
        {
            string? personalizado = null, gerado = null, pedido = null;
            try
            {
                // a sessão em uso está sendo escrita pelo Claude neste momento
                using var fluxo = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leitor = new StreamReader(fluxo);
                while (leitor.ReadLine() is { } linha)
                {
                    if (!linha.Contains("\"type\":\"custom-title\"") && !linha.Contains("\"type\":\"ai-title\"") &&
                        !linha.Contains("\"type\":\"last-prompt\"")) continue;
                    try
                    {
                        var n = JsonNode.Parse(linha);
                        if (Texto(n?["customTitle"]) is { Length: > 0 } c) personalizado = c;
                        if (Texto(n?["aiTitle"]) is { Length: > 0 } a) gerado = a;
                        if (Texto(n?["lastPrompt"]) is { Length: > 0 } p) pedido = p;
                    }
                    catch (JsonException)
                    {
                        // linha cortada no fim do arquivo: o Claude ainda está escrevendo
                    }
                }
            }
            catch (IOException)
            {
                continue;
            }

            if ((personalizado ?? gerado ?? pedido) is { } titulo)
                return new UltimaSessao(Path.GetFileNameWithoutExtension(f.Name), f.LastWriteTime,
                    (long)Math.Round(f.Length / 1024d), titulo);
        }
        return null;
    }

    /// <summary>
    /// O branch, as alterações e o último commit do projeto, pelo git status e pelo git log,
    /// como o dashboard do cia lê. Roda o git: chame fora da tela.
    /// </summary>
    public static InfoDoGit Git(string pasta)
    {
        if (!Directory.Exists(Path.Combine(pasta, ".git")) && !File.Exists(Path.Combine(pasta, ".git")))
            return InfoDoGit.SemGit;

        return RodarGit(pasta, "status", "--porcelain=v2", "--branch") is { } status
            ? LerGit(status, RodarGit(pasta, "log", "-1", "--format=%ct%x09%s"))
            : InfoDoGit.Recusado;
    }

    /// <summary>A saída do <c>git status --porcelain=v2 --branch</c> e do <c>git log -1</c> (data e assunto).</summary>
    public static InfoDoGit LerGit(string status, string? log)
    {
        string branch = "";
        int alterados = 0, ahead = 0, behind = 0;
        foreach (var linha in status.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (linha.StartsWith("# branch.head ", StringComparison.Ordinal)) branch = linha[14..];
            else if (Regex.Match(linha, @"^# branch\.ab \+(\d+) -(\d+)") is { Success: true } ab)
            {
                ahead = int.Parse(ab.Groups[1].Value, CultureInfo.InvariantCulture);
                behind = int.Parse(ab.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else if (!linha.StartsWith('#')) alterados++;
        }

        DateTime? commit = null;
        var mensagem = "";
        if (log?.Trim().Split('\t', 2) is [var segundos, var assunto] &&
            long.TryParse(segundos, NumberStyles.None, CultureInfo.InvariantCulture, out var s))
        {
            commit = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;
            mensagem = assunto.Trim();
        }
        return new InfoDoGit(true, false, branch, alterados, ahead, behind, commit, mensagem);
    }

    // null quando o git recusa a pasta, não responde em 10 s ou não está instalado
    private static string? RodarGit(string pasta, params string[] argumentos)
    {
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(pasta);
        foreach (var a in argumentos) info.ArgumentList.Add(a);
        // só olhar: o status não trava o índice de quem estiver usando o git nessa hora
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        try
        {
            using var git = Process.Start(info)!;
            var saida = git.StandardOutput.ReadToEndAsync();
            _ = git.StandardError.ReadToEndAsync();
            if (!git.WaitForExit(10_000))
            {
                git.Kill();
                return null;
            }
            return git.ExitCode == 0 ? saida.Result : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// O que o Open-CIA (Modules\aliases.ps1) decide ao abrir o projeto, para o GTerm chamar
    /// o Claude direto, sem shell no meio. Sem modo, retoma a última sessão com conversa,
    /// com /compact acima de <see cref="CompactarKB"/>. Sem sessão, ou nos outros modos, abre
    /// na skill: /delphi-writer na base da BMSoft, /retomar nas outras, /pedido, /delphi-pr,
    /// ou nenhuma. Mudou lá, muda aqui.
    /// </summary>
    /// <param name="modo">-Novo, -Limpo, -Pr, o número de um pedido, ou null.</param>
    /// <param name="ultimaSessao">Só é chamada quando o modo retoma: os .jsonl são grandes.</param>
    public static Abertura Decidir(string? modo, bool daBmsoft, Func<UltimaSessao?> ultimaSessao)
    {
        var limpo = modo == "-Limpo";
        var pr = modo == "-Pr";
        var pedido = modo is { Length: > 0 } m && m.All(char.IsAsciiDigit) ? m : "";
        var sessao = modo is null ? ultimaSessao() : null;

        var prompt = limpo ? null
            : pr ? "/delphi-pr"
            : pedido.Length > 0 ? "/pedido " + pedido
            : daBmsoft ? "/delphi-writer"
            : "/retomar";
        var argumentos = sessao is not null
            ? "--resume " + sessao.Id + (sessao.KB >= CompactarKB ? " /compact" : "")
            : prompt is null ? "" : Aspas(prompt);

        // o modo que o cia grava no recente
        var gravado = limpo ? "limpo" : pr ? "pr" : sessao is not null ? "retomada" : daBmsoft ? "writer" : "memória";
        return new Abertura(argumentos, gravado, pedido);
    }

    /// <summary>
    /// O claude.exe: o do PATH, senão o do instalador nativo (%USERPROFILE%\.local\bin).
    /// Null quando não há. Os parâmetros só são trocados nos testes.
    /// </summary>
    public static string? Executavel(Func<string, bool>? existe = null, string? path = null, string? perfil = null)
    {
        existe ??= File.Exists;
        path ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        perfil ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Path.Combine(p, "claude.exe"))
            .Append(Path.Combine(perfil, ".local", "bin", "claude.exe"))
            .FirstOrDefault(existe);
    }

    /// <summary>Entre aspas quando tem espaço: é um argumento só na linha de comando.</summary>
    public static string Aspas(string texto) =>
        texto.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? texto : "\"" + texto.Replace("\"", "\\\"") + "\"";

    /// <summary>"agora", "há 5 min", "há 3 h", "ontem", "há 4 dias" ou a data: o Format-Rel do cia.</summary>
    public static string Quando(DateTime d, DateTime agora)
    {
        var passou = agora - d;
        if (passou.TotalMinutes < 1) return "agora";
        if (passou.TotalMinutes < 60) return $"há {(int)passou.TotalMinutes} min";
        if (d.Date == agora.Date) return $"há {(int)passou.TotalHours} h";
        if (d.Date == agora.Date.AddDays(-1)) return "ontem";
        var dias = (agora.Date - d.Date).Days;
        return dias < 30 ? $"há {dias} dias" : d.ToString("dd/MM/yyyy");
    }

    /// <summary>"340 KB" ou "1,2 MB".</summary>
    public static string Tamanho(long kb) =>
        kb >= 1024 ? (kb / 1024d).ToString("N1", CultureInfo.GetCultureInfo("pt-BR")) + " MB" : kb + " KB";
}
