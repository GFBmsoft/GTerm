using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GTerm.Services;

/// <summary>Quem está logado numa conta do Claude; cada campo é null quando não dá para saber.</summary>
public sealed record Identidade(string? Nome, string? Email, string? Plano)
{
    /// <summary>Como a conta é chamada na tela: o nome de quem está logado, senão o e-mail, senão a pasta.</summary>
    public string Rotulo(string pastaDaConta) =>
        Nome ?? Email ?? Path.GetFileName(pastaDaConta.TrimEnd('\\', '/'));
}

/// <summary>
/// Liga o Claude Code ao ponto de estado da sidebar. Cada terminal nasce com a variável
/// <see cref="VarDoArquivo"/> apontando um arquivo só dele; os hooks instalados no
/// settings.json da conta chamam este mesmo executável com <c>--estado nome</c>, que grava
/// o nome ali, e a sessão lê. Fora do GTerm a variável não existe e o hook não faz nada.
/// </summary>
public static class ClaudeHooks
{
    public const string VarDoArquivo = "GTERM_ESTADO";
    private const string Argumento = "--estado";

    // PostToolUse e não PreToolUse como "rodando": o Pre dispara antes do pedido de
    // permissão, e os dois hooks correndo juntos deixariam o estado ao acaso. As duas
    // ferramentas do Pre são as que param para perguntar algo a quem está no teclado.
    private static readonly (string Evento, string? Filtro, string Estado)[] Ganchos =
    {
        ("SessionStart", null, "pronto"),
        ("UserPromptSubmit", null, "rodando"),
        ("PostToolUse", null, "rodando"),
        ("PreToolUse", "AskUserQuestion|ExitPlanMode", "aguardando"),
        ("Notification", "permission_prompt|elicitation_dialog", "aguardando"),
        ("Stop", null, "concluido"),
        ("SessionEnd", null, "fim"),
    };

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>O que o hook executa: chamado pelo Main antes de qualquer janela.</summary>
    public static bool Avisar(string[] args)
    {
        if (args.Length < 2 || args[0] != Argumento) return false;

        try
        {
            // o carimbo faz dois avisos iguais seguidos serem dois avisos
            if (Environment.GetEnvironmentVariable(VarDoArquivo) is { Length: > 0 } arquivo)
                File.WriteAllText(arquivo, args[1] + " " + DateTime.UtcNow.Ticks);
        }
        catch (Exception)
        {
            // um hook que falha vira mensagem de erro dentro do Claude: melhor perder o aviso
        }
        return true;
    }

    /// <summary>O que a sessão lê do arquivo: só o nome do estado.</summary>
    public static string Evento(string conteudo) => conteudo.Split(' ')[0].Trim();

    /// <summary>
    /// Sem aspas e com barras normais quando dá: assim a mesma linha serve no bash e no
    /// PowerShell, e não se sabe em qual dos dois o Claude vai rodar o hook.
    /// </summary>
    public static string Comando(string exe, string estado)
    {
        var caminho = exe.Replace('\\', '/');
        // atalho: caminho com espaço vai entre aspas, o que só o bash aceita como comando;
        // evoluir quando alguém instalar o GTerm numa pasta com espaço e usar hooks em PowerShell
        if (caminho.Contains(' ')) caminho = "\"" + caminho + "\"";
        return $"{caminho} {Argumento} {estado}";
    }

    /// <summary>
    /// Pastas de configuração do Claude nesta máquina: a padrão, as <c>.claude-*</c> ao
    /// lado dela e as que os projetos usam.
    /// </summary>
    public static IReadOnlyList<string> Contas(IEnumerable<string> dosProjetos)
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var achadas = new List<string> { Path.Combine(perfil, ".claude") };
        try
        {
            achadas.AddRange(Directory.GetDirectories(perfil, ".claude-*")
                .Where(d => File.Exists(Path.Combine(d, "settings.json")) ||
                            File.Exists(Path.Combine(d, ".credentials.json"))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        achadas.AddRange(dosProjetos.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()));

        return achadas.Where(Directory.Exists)
            .Select(d => Path.GetFullPath(d).TrimEnd('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A pasta é uma conta à parte, e não a padrão (<c>~\.claude</c>) nem vazio. A padrão
    /// não pode ir para o CLAUDE_CONFIG_DIR: com a variável definida o Claude procura o
    /// <c>.claude.json</c> dentro da pasta, e não na raiz do perfil onde ele de fato está,
    /// não acha o login e pede para entrar de novo.
    /// </summary>
    public static bool ContaPropria(string? pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) return false;

        var padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        try
        {
            return !Path.GetFullPath(pasta.Trim()).TrimEnd('\\', '/')
                .Equals(padrao, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true; // caminho estranho: quem decide o que fazer com ele é o Claude
        }
    }

    private static string Perfil => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// A pasta da conta que o projeto usa, num formato só: a do projeto quando é uma conta
    /// à parte, a padrão em qualquer outro caso. É a chave dos grupos da sidebar.
    /// </summary>
    public static string PastaDaConta(string? conta)
    {
        if (!ContaPropria(conta)) return Path.Combine(Perfil, ".claude");
        try
        {
            return Path.GetFullPath(conta!.Trim()).TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return conta!.Trim();
        }
    }

    /// <summary>Plano da assinatura da conta (MAX, TEAM...), ou null se não der para saber.</summary>
    public static string? Plano(string pastaDaConta) => Identificar(pastaDaConta).Plano;

    /// <summary>Quem está logado na conta: lê o perfil uma vez só, que o arquivo é grande.</summary>
    public static Identidade Identificar(string pastaDaConta)
    {
        JsonNode? perfil = null;
        try
        {
            // a conta padrão guarda o .claude.json na raiz do perfil; as outras, dentro da pasta
            var arquivo = Path.Combine(ContaPropria(pastaDaConta) ? pastaDaConta : Perfil, ".claude.json");
            if (File.Exists(arquivo)) perfil = JsonNode.Parse(File.ReadAllText(arquivo))?["oauthAccount"];
        }
        catch (Exception)
        {
            // sem perfil legível a conta aparece pelo nome da pasta
        }

        string? Campo(string nome)
        {
            try
            {
                var valor = perfil?[nome]?.GetValue<string>();
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        return new Identidade(Campo("displayName"), Campo("emailAddress"),
            PlanoDoPerfil(Campo("organizationType")) ?? PlanoDasCredenciais(pastaDaConta));
    }

    // o perfil na frente: o subscriptionType das credenciais só muda num login novo, e
    // continua dizendo "pro" numa conta que já passou para o Max
    private static string? PlanoDoPerfil(string? tipo)
    {
        if (tipo is null) return null;
        const string prefixo = "claude_";
        return (tipo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) ? tipo[prefixo.Length..] : tipo)
            .ToUpperInvariant();
    }

    private static string? PlanoDasCredenciais(string pastaDaConta)
    {
        try
        {
            var arquivo = Path.Combine(pastaDaConta, ".credentials.json");
            if (!File.Exists(arquivo)) return null;
            // só o nome do plano: o resto do arquivo são as credenciais, que não interessam aqui
            var plano = JsonNode.Parse(File.ReadAllText(arquivo))?["claudeAiOauth"]?["subscriptionType"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(plano) ? null : plano.ToUpperInvariant();
        }
        catch (Exception)
        {
            return null;
        }
    }


    private static string Arquivo(string pastaDaConta) => Path.Combine(pastaDaConta, "settings.json");

    public static bool Instalado(string pastaDaConta)
    {
        try
        {
            return Ler(pastaDaConta)["hooks"] is JsonObject hooks &&
                   hooks.Any(par => par.Value is JsonArray grupos && grupos.Any(Nosso));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Acrescenta os hooks (trocando os de uma instalação anterior) sem tocar no resto.</summary>
    public static void Instalar(string pastaDaConta, string exe)
    {
        var raiz = Ler(pastaDaConta);
        Tirar(raiz);

        if (raiz["hooks"] is not JsonObject hooks) raiz["hooks"] = hooks = new JsonObject();
        foreach (var (evento, filtro, estado) in Ganchos)
        {
            if (hooks[evento] is not JsonArray grupos) hooks[evento] = grupos = new JsonArray();

            var grupo = new JsonObject();
            if (filtro is not null) grupo["matcher"] = filtro;
            grupo["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = Comando(exe, estado),
            });
            grupos.Add(grupo);
        }

        Gravar(pastaDaConta, raiz);
    }

    public static void Remover(string pastaDaConta)
    {
        if (!File.Exists(Arquivo(pastaDaConta))) return;
        var raiz = Ler(pastaDaConta);
        Tirar(raiz);
        Gravar(pastaDaConta, raiz);
    }

    private static JsonObject Ler(string pastaDaConta)
    {
        var arquivo = Arquivo(pastaDaConta);
        if (!File.Exists(arquivo)) return new JsonObject();

        // arquivo que não é um objeto JSON estoura aqui, de propósito: melhor não instalar
        // do que gravar por cima de um settings.json que não se soube ler
        return JsonNode.Parse(File.ReadAllText(arquivo)) as JsonObject
               ?? throw new InvalidDataException("O settings.json de " + pastaDaConta + " não é um objeto JSON.");
    }

    private static void Gravar(string pastaDaConta, JsonObject raiz)
    {
        var arquivo = Arquivo(pastaDaConta);

        // uma cópia do original, feita uma vez só: é o que havia antes do GTerm mexer
        var copia = arquivo + ".antes-do-gterm";
        if (File.Exists(arquivo) && !File.Exists(copia)) File.Copy(arquivo, copia);

        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, raiz.ToJsonString(Opcoes));
        File.Move(tmp, arquivo, overwrite: true);
    }

    private static bool Nosso(JsonNode? grupo) =>
        grupo?["hooks"] is JsonArray comandos &&
        comandos.Any(c => c?["command"]?.GetValue<string>() is { } linha &&
                          linha.Contains(" " + Argumento + " ", StringComparison.Ordinal) &&
                          linha.Contains("GTerm", StringComparison.OrdinalIgnoreCase));

    private static void Tirar(JsonObject raiz)
    {
        if (raiz["hooks"] is not JsonObject hooks) return;

        foreach (var evento in hooks.Select(par => par.Key).ToList())
        {
            if (hooks[evento] is not JsonArray grupos) continue;
            foreach (var grupo in grupos.Where(Nosso).ToList()) grupos.Remove(grupo);
            if (grupos.Count == 0) hooks.Remove(evento);
        }
        if (hooks.Count == 0) raiz.Remove("hooks");
    }
}
