using System;
using System.IO;
using System.Text.Json;
using GTerm.Models;

namespace GTerm.Services;

/// <summary>Persistência do workspace (projetos e o último selecionado).</summary>
public static class WorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public const string NomeDoArquivo = "workspace.json";

    /// <summary>Desvio usado só pelos testes, para nunca alcançarem o workspace real.</summary>
    internal static string? PastaPadraoDeTeste { get; set; }

    /// <summary>
    /// %APPDATA%\GTerm por padrão. GTERM_HOME vence — é como se roda o app sem tocar no
    /// workspace real.
    /// </summary>
    public static string Directory =>
        Environment.GetEnvironmentVariable("GTERM_HOME") is { Length: > 0 } custom
            ? custom
            : PastaPadraoDeTeste ??
              Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GTerm");

    public static string FilePath => Path.Combine(Directory, NomeDoArquivo);

    public static Workspace Load() => LoadFrom(FilePath);

    public static Workspace LoadFrom(string file)
    {
        try
        {
            if (!File.Exists(file)) return new Workspace();
            var raw = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Workspace>(raw, Options) ?? new Workspace();
        }
        catch (Exception)
        {
            // workspace corrompido não pode impedir a abertura do aplicativo
            return new Workspace();
        }
    }

    public static void Save(Workspace ws) => SaveTo(FilePath, ws);

    public static void SaveTo(string file, Workspace ws)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(ws, Options);
        // grava em arquivo temporário e substitui: evita workspace truncado se o
        // processo morrer no meio da escrita
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, file, overwrite: true);
    }
}
