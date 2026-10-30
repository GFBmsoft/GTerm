using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GTerm.Services;

/// <summary>Uma conversa do Claude, como uma linha do cl (Modules\claude-sessoes.ps1).</summary>
/// <param name="Id">O uuid: o nome do .jsonl e o que o --resume recebe.</param>
/// <param name="Tamanho">Em bytes.</param>
/// <param name="Branch">O branch do git em que ela estava; vazio fora do git.</param>
/// <param name="Pasta">De onde foi aberta: o cwd que gera o nome da pasta em projects. Vazia quando não se acha.</param>
/// <param name="Chave">O nome da pasta em projects: o caminho com tudo que não é letra ou número trocado por '-'.</param>
/// <param name="Pid">O claude.exe que está com ela aberta, aqui ou em outro terminal; null fechada.</param>
/// <param name="Bg">Tem um job em segundo plano (jobs\&lt;id&gt;).</param>
public sealed record SessaoDoClaude(string Id, string Titulo, DateTime Quando, long Tamanho, string Branch,
    string Pasta, string Chave, int? Pid = null, bool Bg = false)
{
    public bool Aberta => Pid is not null;
}

/// <summary>
/// As conversas do Claude (a lista do /resume), lidas como o cl lê: só o fim de cada .jsonl
/// para o título e o branch (há transcript com mais de 1 GB, e o Claude regrava o título e o
/// último prompt no fim a cada resposta) e só o começo para a pasta de origem. As favoritas
/// ficam no estado-cl.json do cl, que os dois dividem. Mudou lá, muda aqui.
/// </summary>
public sealed class SessoesDoClaude
{
    // o que já foi lido de cada arquivo, enquanto ele não muda: a lista é relida a cada
    // pouco, e só a conversa em uso cresce
    private readonly Dictionary<string, (long Tamanho, DateTime Quando, Lida? Lida)> _lidas =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record Lida(string Titulo, string Branch, string Pasta);

    /// <summary>
    /// As conversas de todas as pastas, da mais nova para a mais velha. Aberta e fechada sem
    /// conversa (sem título nem prompt) não entra, como no /resume.
    /// </summary>
    /// <param name="pastaDaConta">A pasta de configuração do Claude (~\.claude).</param>
    public IReadOnlyList<SessaoDoClaude> Listar(string pastaDaConta)
    {
        DirectoryInfo[] pastas;
        try
        {
            var projetos = new DirectoryInfo(Path.Combine(pastaDaConta, "projects"));
            pastas = projetos.Exists ? projetos.GetDirectories() : Array.Empty<DirectoryInfo>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<SessaoDoClaude>();
        }

        var abertas = Abertas(pastaDaConta);
        var lista = new List<SessaoDoClaude>();
        lock (_lidas)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pasta in pastas)
            {
                FileInfo[] arquivos;
                try
                {
                    // só a raiz da pasta: as dos subagentes ficam em subpastas
                    arquivos = pasta.GetFiles("*.jsonl");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var f in arquivos)
                {
                    vistos.Add(f.FullName);
                    if (Ler(f, pasta.Name) is not { } lida) continue;

                    var id = Path.GetFileNameWithoutExtension(f.Name);
                    lista.Add(new SessaoDoClaude(id, lida.Titulo, f.LastWriteTime, f.Length, lida.Branch, lida.Pasta,
                        pasta.Name, abertas.TryGetValue(id, out var pid) ? pid : null, EmSegundoPlano(pastaDaConta, id)));
                }
            }
            foreach (var sumiu in _lidas.Keys.Where(k => !vistos.Contains(k)).ToList()) _lidas.Remove(sumiu);
        }
        return lista.OrderByDescending(s => s.Quando).ToList();
    }

    private Lida? Ler(FileInfo f, string chave)
    {
        if (_lidas.TryGetValue(f.FullName, out var antes) && antes.Tamanho == f.Length && antes.Quando == f.LastWriteTime)
            return antes.Lida;

        Lida? lida = null;
        try
        {
            var (titulo, branch) = Titulo(f.FullName, f.Length, completo: false);
            if (titulo is not null) lida = new Lida(titulo, branch, Origem(f.FullName, f.Length, chave));
        }
        catch (IOException)
        {
            return null; // apagada no meio da leitura: entra na próxima
        }
        _lidas[f.FullName] = (f.Length, f.LastWriteTime, lida);
        return lida;
    }

    // a chave literal e o valor logo depois: sem regex, que custava caro em megabytes de texto
    private const string Personalizado = "\"type\":\"custom-title\",\"customTitle\":\"";
    private const string Gerado = "\"type\":\"ai-title\",\"aiTitle\":\"";
    private const string UltimoPrompt = "\"type\":\"last-prompt\",\"lastPrompt\":\"";
    private const string CampoBranch = "\"gitBranch\":\"";
    private const string CampoCwd = "\"cwd\":\"";

    /// <summary>
    /// O título da conversa (o que o usuário deu, senão o que o Claude gerou, senão o último
    /// prompt) e o branch, pelo fim do arquivo: 256 KB, e 4 MB se não achar. Null quando não
    /// teve conversa.
    /// </summary>
    /// <param name="completo">Sem achar nos 4 MB, lê o arquivo inteiro, como o cia faz.</param>
    public static (string? Titulo, string Branch) Titulo(string arquivo, long tamanho, bool completo)
    {
        string? personalizado = null, gerado = null, prompt = null, branch = null;
        const long Maior = 4L * 1024 * 1024;
        // vazio não conta, como no cl (no PowerShell, "" é falso)
        bool Achou() => !string.IsNullOrEmpty(personalizado) || !string.IsNullOrEmpty(gerado) || !string.IsNullOrEmpty(prompt);

        foreach (var maximo in new[] { 256L * 1024, Maior })
        {
            var texto = Trecho(arquivo, maximo, inicio: false);
            personalizado = Ultimo(Personalizado, texto);
            gerado = Ultimo(Gerado, texto);
            prompt = Ultimo(UltimoPrompt, texto);
            branch = Ultimo(CampoBranch, texto);
            if (Achou() || tamanho <= maximo) break;
        }

        if (completo && tamanho > Maior && !Achou())
        {
            // linha a linha: o arquivo inteiro nunca fica na memória
            using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var leitor = new StreamReader(fluxo);
            while (leitor.ReadLine() is { } linha)
            {
                personalizado = Ultimo(Personalizado, linha) ?? personalizado;
                gerado = Ultimo(Gerado, linha) ?? gerado;
                prompt = Ultimo(UltimoPrompt, linha) ?? prompt;
                branch = Ultimo(CampoBranch, linha) ?? branch;
            }
        }

        var titulo = new[] { personalizado, gerado, prompt }.FirstOrDefault(t => !string.IsNullOrEmpty(t));
        return (titulo is null ? null : Regex.Replace(titulo, @"\s+", " ").Trim(),
            branch is null or "HEAD" ? "" : branch);
    }

    // a pasta de origem: o cwd que gera a chave (o cwd muda com os cd da conversa), pelo começo
    private static string Origem(string arquivo, long tamanho, string chave)
    {
        var cwds = new List<string>();
        foreach (var maximo in new[] { 64L * 1024, 1024L * 1024 })
        {
            cwds = Todos(CampoCwd, Trecho(arquivo, maximo, inicio: true));
            if (cwds.Count > 0 || tamanho <= maximo) break;
        }
        return cwds.FirstOrDefault(c => Chave(c) == chave) ?? cwds.FirstOrDefault() ?? "";
    }

    /// <summary>O nome da pasta da conversa em projects: tudo que não é letra ou número vira '-'.</summary>
    public static string Chave(string caminho) => Regex.Replace(caminho, "[^A-Za-z0-9]", "-");

    // até `maximo` bytes do fim (ou do começo) como texto; o Claude pode estar gravando nele
    internal static string Trecho(string arquivo, long maximo, bool inicio)
    {
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var n = (int)Math.Min(Math.Min(maximo, fluxo.Length), int.MaxValue);
        if (!inicio) fluxo.Seek(-n, SeekOrigin.End);
        var buffer = new byte[n];
        var lidos = 0;
        while (lidos < n && fluxo.Read(buffer, lidos, n - lidos) is > 0 and var r) lidos += r;
        return Encoding.UTF8.GetString(buffer, 0, lidos);
    }

    // a string JSON crua que começa em `inicio`: até a primeira aspa sem barra invertida antes
    private static string? ValorEm(string texto, int inicio)
    {
        for (var j = texto.IndexOf('"', inicio); j >= 0; j = texto.IndexOf('"', j + 1))
        {
            var k = j - 1;
            while (k >= inicio && texto[k] == '\\') k--;
            if ((j - 1 - k) % 2 == 0) return texto[inicio..j];
        }
        return null;
    }

    private static string Json(string cru)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + cru + "\"") ?? cru;
        }
        catch (JsonException)
        {
            return cru;
        }
    }

    // a última ocorrência completa: a linha final pode estar pela metade enquanto o Claude grava
    private static string? Ultimo(string campo, string texto)
    {
        for (var i = texto.LastIndexOf(campo, StringComparison.Ordinal); i >= 0;
             i = i == 0 ? -1 : texto.LastIndexOf(campo, i - 1, StringComparison.Ordinal))
        {
            if (ValorEm(texto, i + campo.Length) is { } valor) return Json(valor);
        }
        return null;
    }

    // os valores distintos, na ordem do arquivo
    private static List<string> Todos(string campo, string texto)
    {
        var valores = new List<string>();
        for (var i = texto.IndexOf(campo, StringComparison.Ordinal); i >= 0;
             i = texto.IndexOf(campo, i + campo.Length, StringComparison.Ordinal))
        {
            if (ValorEm(texto, i + campo.Length) is { } valor && !valores.Contains(Json(valor))) valores.Add(Json(valor));
        }
        return valores;
    }

    /// <summary>
    /// As conversas com o claude.exe rodando, pelo sessions\&lt;pid&gt;.json da conta: o id da
    /// conversa e o processo. O teste troca a conferência do processo.
    /// </summary>
    public static Dictionary<string, int> Abertas(string pastaDaConta, Func<int, bool>? rodando = null)
    {
        rodando ??= EhClaude;
        var abertas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] arquivos;
        try
        {
            var dir = Path.Combine(pastaDaConta, "sessions");
            arquivos = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return abertas;
        }

        foreach (var arquivo in arquivos)
        {
            try
            {
                var n = JsonNode.Parse(File.ReadAllText(arquivo));
                if (Cia.Texto(n?["sessionId"]) is { Length: > 0 } id &&
                    int.TryParse(Cia.Texto(n?["pid"]), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                    rodando(pid))
                    abertas[id] = pid;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // o Claude gravando ou apagando o arquivo neste instante
            }
        }
        return abertas;
    }

    private static bool EhClaude(int pid)
    {
        try
        {
            using var processo = Process.GetProcessById(pid);
            return processo.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool EmSegundoPlano(string pastaDaConta, string id) =>
        File.Exists(Path.Combine(pastaDaConta, "jobs", id[..Math.Min(8, id.Length)], "state.json"));

    /// <summary>
    /// Os ids das favoritas do cl, na ordem do arquivo. Null quando o arquivo existe mas não
    /// se lê (o cl gravando naquele instante): quem chama fica com o que já tinha.
    /// </summary>
    public static IReadOnlyList<string>? LerFavoritas(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return Array.Empty<string>();
            return Cia.Itens(JsonNode.Parse(File.ReadAllText(arquivo))?["favoritas"])
                .Select(Cia.Texto).Where(t => t.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Favorita a conversa, na frente das outras como o cl -f faz, ou tira das favoritas.
    /// Verdadeiro quando ela ficou favorita. O resto do arquivo fica como estava.
    /// </summary>
    public static bool TrocarFavorita(string arquivo, string id)
    {
        var raiz = Cia.Abrir(arquivo);
        var favoritas = Cia.Itens(raiz["favoritas"]).Select(Cia.Texto).Where(t => t.Length > 0).ToList();
        var ficou = favoritas.RemoveAll(f => f.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0;
        if (ficou) favoritas.Insert(0, id);
        raiz["favoritas"] = new JsonArray(favoritas.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
        Cia.Gravar(arquivo, raiz);
        return ficou;
    }

    /// <summary>
    /// O que a conversa deixa na conta, o transcript primeiro: a pasta dela em projects
    /// (subagentes e saídas longas), o histórico de arquivos do /rewind, o ambiente e o job em
    /// segundo plano. O history.jsonl fica: é um arquivo só, em que todo Claude aberto escreve.
    /// </summary>
    public static IReadOnlyList<string> Rastros(string pastaDaConta, SessaoDoClaude s)
    {
        // só uuid: um id vazio apontaria para a pasta file-history inteira
        if (!Guid.TryParse(s.Id, out _)) return Array.Empty<string>();

        var projeto = Path.Combine(pastaDaConta, "projects", s.Chave);
        return new[]
        {
            Path.Combine(projeto, s.Id + ".jsonl"),
            Path.Combine(projeto, s.Id),
            Path.Combine(pastaDaConta, "file-history", s.Id),
            Path.Combine(pastaDaConta, "session-env", s.Id),
            Path.Combine(pastaDaConta, "jobs", s.Id[..8]),
        }.Where(c => File.Exists(c) || Directory.Exists(c)).ToList();
    }

    /// <summary>"agora", "há 5 min", "há 3 h", "ontem 14:32" ou "27/10 09:15": o _ClQuando do cl.</summary>
    public static string Quando(DateTime d, DateTime agora)
    {
        var passou = agora - d;
        if (passou.TotalMinutes < 1) return "agora";
        if (passou.TotalMinutes < 60) return $"há {(int)passou.TotalMinutes} min";
        if (d.Date == agora.Date) return $"há {(int)passou.TotalHours} h";
        if (d.Date == agora.Date.AddDays(-1)) return "ontem " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
        return d.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>"340 KB", "23,4 MB" ou "1,8 GB".</summary>
    public static string Tamanho(long bytes)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("N1", br) + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("N1", br) + " MB";
        return Math.Max(1, bytes / 1024).ToString("N0", br) + " KB";
    }

    /// <summary>O grupo do dia, como o cl separa: Hoje, Ontem, Esta semana ou Mais antigas.</summary>
    public static string Dia(DateTime d, DateTime agora) => (agora.Date - d.Date).Days switch
    {
        <= 0 => "Hoje",
        1 => "Ontem",
        < 7 => "Esta semana",
        _ => "Mais antigas",
    };
}
