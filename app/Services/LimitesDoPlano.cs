using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace GTerm.Services;

/// <summary>Uma janela do plano: quanto dela já foi usado (0 a 100) e quando zera.</summary>
public sealed record JanelaDoPlano(string Nome, double Uso, DateTime? Zera, string? Bloqueio);

/// <summary>
/// As janelas do plano da conta, como o /usage mostra.
/// </summary>
/// <param name="Em">Quando a conta disse isto: pode ser de minutos atrás (cache em disco, espera depois de um erro).</param>
/// <param name="Erro">Por que a consulta de agora não saiu; as janelas, se houver, são as da última que saiu.</param>
/// <param name="Plano">O plano da conta ("Max", "Pro"…), do arquivo de login; vazio sem ele.</param>
public sealed record Limites(IReadOnlyList<JanelaDoPlano> Janelas, DateTime? Em, string? Erro, string Plano);

/// <summary>
/// Os limites de 5 horas e de 7 dias do plano, que não estão nos transcripts: vêm da conta, pelo
/// mesmo endpoint do /usage (<c>api.anthropic.com/api/oauth/usage</c>), com o login que o próprio
/// Claude Code grava em <c>.credentials.json</c>. É o Get-Limits do ctop
/// (<c>Scripts\claude-stats\claude-stats.ps1</c>): **mudou lá, muda aqui.** Não é uma API pública
/// estável.
/// <para>
/// O endpoint responde 429 a consultas seguidas, e o ctop e o GTerm consultam a mesma conta: por
/// isso o estado fica no arquivo do ctop (<c>%LOCALAPPDATA%\claude-stats\limits-cache.json</c>,
/// <c>{at, data, nextTry}</c>) e os dois o respeitam. Uma consulta a cada <see cref="Validade"/>
/// no máximo, cinco minutos de espera depois de um 429, um depois de outro erro, e nesse meio
/// tempo vale a última resposta boa, com a idade dela. O login nunca é renovado daqui: vencido,
/// quem renova é o Claude, ao ser aberto.
/// </para>
/// </summary>
public sealed class LimitesDoPlano
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    /// <summary>O que a conta respondeu: o código HTTP, o corpo e, num 429, quanto ela mandou esperar.</summary>
    public readonly record struct Resposta(int Status, string Corpo, TimeSpan? Esperar = null);

    private readonly Func<string, Task<Resposta>> _consultar;
    private readonly string _pasta;

    /// <param name="consultar">Do login para a resposta da conta. Os testes põem uma de mentira.</param>
    /// <param name="pastaDoEstado">Onde fica o arquivo do estado; a do ctop se não vier.</param>
    public LimitesDoPlano(Func<string, Task<Resposta>>? consultar = null, string? pastaDoEstado = null)
    {
        _consultar = consultar ?? Consultar;
        _pasta = pastaDoEstado ?? (Cia.PastaDeTeste is { } teste
            ? Path.Combine(teste, "claude-stats")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "claude-stats"));
    }

    // as janelas que interessam, na ordem em que o /usage mostra
    private static readonly (string Chave, string Nome)[] Janelas =
    {
        ("five_hour", "Sessão (5h)"),
        ("seven_day", "Semana (7d)"),
        ("seven_day_opus", "Semana Opus"),
        ("seven_day_sonnet", "Semana Sonnet"),
    };

    /// <summary>
    /// O arquivo do estado. Da conta padrão é o do ctop; de outra conta, um ao lado, com o nome da
    /// pasta dela: o limite é de cada conta.
    /// </summary>
    public string Arquivo(string pastaDaConta)
    {
        var conta = Path.GetFileName(pastaDaConta.TrimEnd('\\', '/'));
        return Path.Combine(_pasta, conta is ".claude" or "" ? "limits-cache.json" : $"limits-cache-{conta}.json");
    }

    public async Task<Limites> LerAsync(string pastaDaConta, DateTime agora)
    {
        var arquivo = Arquivo(pastaDaConta);
        var (em, dados, proxima, guardado) = LerEstado(arquivo);
        var (login, vence, plano) = Login(pastaDaConta);

        // na espera depois de um erro não se consulta, com ou sem resposta antiga para mostrar
        if (agora < proxima) return Montar(dados, em, guardado, plano);
        if (dados is not null && agora - em < Validade) return Montar(dados, em, null, plano);

        string erro;
        var espera = TimeSpan.FromMinutes(1);
        try
        {
            if (login is null)
            {
                erro = "sem login do Claude nesta conta";
            }
            else if (vence <= agora)
            {
                erro = "login vencido: abra o Claude";
            }
            else
            {
                var r = await _consultar(login);
                if (r.Status == 200 && JsonNode.Parse(r.Corpo) is JsonObject novos)
                {
                    Gravar(arquivo, agora, novos, agora, null);
                    return Montar(novos, agora, null, plano);
                }

                erro = r.Status switch
                {
                    401 => "login vencido: abra o Claude",
                    429 => "a conta limitou as consultas",
                    _ => "sem resposta da conta",
                };
                if (r.Status == 429) espera = r.Esperar is { } pedida ? pedida + TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            erro = "sem resposta da conta";
        }

        Gravar(arquivo, em, dados, agora + espera, erro);
        return Montar(dados, em, erro, plano);
    }

    private static Limites Montar(JsonNode? dados, DateTime? em, string? erro, string plano)
    {
        var janelas = new List<JanelaDoPlano>();
        foreach (var (chave, nome) in Janelas)
        {
            if (dados?[chave] is not JsonObject j || j["utilization"] is not JsonValue v || !v.TryGetValue<double>(out var uso)) continue;
            janelas.Add(new JanelaDoPlano(nome, uso, Data(j["resets_at"]),
                Cia.Texto(j["locked_reason"]) is { Length: > 0 } motivo ? motivo : null));
        }
        return new Limites(janelas, dados is null ? null : em, erro, plano);
    }

    /// <summary>"zera agora", "zera em 42 min", "zera em 3h12", "zera 15/11 09:00": o Format-Reset do ctop.</summary>
    public static string QuandoZera(DateTime? zera, DateTime agora)
    {
        if (zera is not { } z) return "";
        var falta = z - agora;
        return falta <= TimeSpan.Zero ? "zera agora"
            : falta < TimeSpan.FromHours(1) ? $"zera em {Math.Max(1, (int)falta.TotalMinutes)} min"
            : falta < TimeSpan.FromHours(24) ? $"zera em {(int)falta.TotalHours}h{falta.Minutes:00}"
            : "zera " + z.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    // o login que o Claude Code gravou, quando vence e o plano da conta
    private static (string? Login, DateTime Vence, string Plano) Login(string pastaDaConta)
    {
        try
        {
            var arquivo = Path.Combine(pastaDaConta, ".credentials.json");
            if (!File.Exists(arquivo)) return (null, default, "");

            var oauth = JsonNode.Parse(Texto(arquivo))?["claudeAiOauth"];
            var login = Cia.Texto(oauth?["accessToken"]);
            var vence = oauth?["expiresAt"] is JsonValue v && v.TryGetValue<long>(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
                : DateTime.MaxValue;
            var plano = Cia.Texto(oauth?["subscriptionType"]);
            if (plano.Length > 0) plano = char.ToUpperInvariant(plano[0]) + plano[1..];
            return (login.Length > 0 ? login : null, vence, plano);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            return (null, default, "");
        }
    }

    private static (DateTime Em, JsonNode? Dados, DateTime Proxima, string? Erro) LerEstado(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return default;
            var estado = JsonNode.Parse(Texto(arquivo));
            return (Data(estado?["at"]) ?? DateTime.MinValue, estado?["data"] as JsonObject,
                Data(estado?["nextTry"]) ?? DateTime.MinValue, Cia.Texto(estado?["erro"]) is { Length: > 0 } erro ? erro : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    // sem conseguir gravar, a próxima volta consulta de novo. O "erro" é daqui: o ctop não o lê,
    // e é o que o cartão mostra enquanto dura a espera
    private static void Gravar(string arquivo, DateTime em, JsonNode? dados, DateTime proxima, string? erro)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
            var estado = new JsonObject
            {
                ["at"] = em.ToString("o", CultureInfo.InvariantCulture),
                ["data"] = dados?.DeepClone(),
                ["nextTry"] = proxima.ToString("o", CultureInfo.InvariantCulture),
            };
            if (erro is not null) estado["erro"] = erro;

            // em .tmp e renomeando: o ctop pode ler no mesmo instante
            var provisorio = arquivo + ".tmp";
            File.WriteAllText(provisorio, estado.ToJsonString());
            File.Move(provisorio, arquivo, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // as datas vêm com o fuso (da conta, do ctop e daqui): viram a hora local
    private static DateTime? Data(JsonNode? no) =>
        Cia.Texto(no) is { Length: > 0 } texto &&
        DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var data)
            ? data.LocalDateTime
            : null;

    private static string Texto(string arquivo)
    {
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static async Task<Resposta> Consultar(string login)
    {
        // nos testes, nunca a conta de verdade: quem quer uma resposta passa a sua no construtor
        if (Cia.PastaDeTeste is not null) return new Resposta(0, "");

        using var pedido = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login);
        pedido.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        pedido.Headers.UserAgent.ParseAdd("GTerm");
        using var resposta = await Http.SendAsync(pedido);
        return new Resposta((int)resposta.StatusCode, await resposta.Content.ReadAsStringAsync(), resposta.Headers.RetryAfter?.Delta);
    }
}
