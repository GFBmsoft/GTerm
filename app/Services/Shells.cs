using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GTerm.Services;

/// <summary>Um shell que o terminal sabe abrir.</summary>
public sealed record Shell(string Id, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Linha de comando e ambiente de cada shell, achando o executável na máquina.</summary>
public static class Shells
{
    public const string Padrao = "pwsh";

    public static readonly IReadOnlyList<Shell> Todos = new[]
    {
        new Shell("pwsh", "PowerShell"),
        new Shell("cmd", "Prompt de Comando"),
        new Shell("gitbash", "Git Bash"),
    };

    /// <summary>Id desconhecido (workspace editado à mão) cai no padrão.</summary>
    public static Shell Achar(string? id) =>
        Todos.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Todos[0];

    /// <summary>
    /// Linha de comando e ajustes de ambiente (null = herdar). <paramref name="existe"/> e
    /// <paramref name="path"/> só são trocados nos testes.
    /// </summary>
    public static (string Linha, Dictionary<string, string?>? Ambiente) Comando(
        string? id, Func<string, bool>? existe = null, string? path = null)
    {
        existe ??= File.Exists;
        path ??= Environment.GetEnvironmentVariable("PATH") ?? "";

        // Todo shell sai daqui avisando quando um comando termina (OSC 133;D e, onde dá, o
        // código de saída): é disso que a Atividade tira o verde e o vermelho da sidebar.
        switch (Achar(id).Id)
        {
            case "cmd":
                // $E é o ESC; o cmd não tem como pôr o código de saída no prompt
                return ("cmd.exe", new Dictionary<string, string?> { ["PROMPT"] = @"$E]133;D$E\$P$G" });

            case "gitbash":
                var bash = Bash(existe, path) ?? throw new FileNotFoundException(
                    "Git Bash não encontrado. Instale o Git ou escolha outro shell para o projeto.");
                return ($"\"{bash}\" --login -i", new Dictionary<string, string?>
                {
                    // o /etc/profile do Git Bash vai para o HOME sem isto
                    ["CHERE_INVOKING"] = "1",
                    ["MSYSTEM"] = "MINGW64",
                    ["TERM"] = "xterm-256color",
                    // um .bashrc que defina PROMPT_COMMAND vence este, e aí o estado não muda
                    ["PROMPT_COMMAND"] = @"printf '\033]133;D;%s\007' ""$?""",
                });

            default:
                // PowerShell 7 quando instalado; o 5.1 vem com o Windows
                return (PowerShell(existe, path) + " -NoLogo -NoExit -EncodedCommand " + Codificar(MarcaDoPowerShell), null);
        }
    }

    /// <summary>
    /// Linha de comando do painel lateral: um PowerShell sem perfil (para abrir na hora)
    /// que roda o script e sai com ele.
    /// </summary>
    public static string Painel(string script, Func<string, bool>? existe = null, string? path = null) =>
        PowerShell(existe ?? File.Exists, path ?? Environment.GetEnvironmentVariable("PATH") ?? "") +
        " -NoLogo -NoProfile -EncodedCommand " + Codificar(script);

    private static string PowerShell(Func<string, bool> existe, string path) =>
        NoPath("pwsh.exe", existe, path) is { } pwsh ? $"\"{pwsh}\"" : "powershell.exe";

    // em base64 o script chega inteiro, sem briga de aspas na linha de comando
    private static string Codificar(string script) =>
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

    /// <summary>
    /// Roda depois do perfil do usuário e embrulha o prompt que ele deixou (oh-my-posh,
    /// starship, o padrão). O Write-Error devolve o <c>$?</c> de falha que as linhas
    /// anteriores apagaram: o prompt original também decide o que mostrar por ele.
    /// </summary>
    private const string MarcaDoPowerShell = """
        $global:__gtermPrompt = $function:prompt
        function global:prompt {
            $ok = $?
            $codigo = $global:LASTEXITCODE
            $saida = if ($ok) { 0 } elseif ($codigo) { $codigo } else { 1 }
            $marca = "$([char]27)]133;D;$saida$([char]7)"
            $global:LASTEXITCODE = $codigo
            if (-not $ok) { Write-Error 'falha' -ErrorAction Ignore }
            $marca + ($global:__gtermPrompt.Invoke() -join '')
        }
        """;

    private static string? NoPath(string exe, Func<string, bool> existe, string path) =>
        path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Path.Combine(p, exe))
            .FirstOrDefault(existe);

    /// <summary>
    /// O bash.exe da instalação do Git. O git-bash.exe não serve: ele abre a janela do
    /// mintty em vez de conversar pelo pseudoconsole.
    /// </summary>
    private static string? Bash(Func<string, bool> existe, string path)
    {
        // no PATH costuma estar Git\cmd: a raiz da instalação é a pasta acima
        var raizes = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => existe(Path.Combine(p, "git.exe")))
            .Select(p => Path.GetDirectoryName(p.TrimEnd('\\', '/')) ?? "")
            .Concat(new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git"),
            });

        return raizes.Where(r => r.Length > 0)
            .Select(r => Path.Combine(r, "bin", "bash.exe"))
            .FirstOrDefault(existe);
    }
}
