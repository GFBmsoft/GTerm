using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaTerminal;
using GTerm.Models;
using GTerm.Services;
using GTerm.ViewModels;
using GTerm.Views;
using Xunit;

namespace GTerm.Tests;

public class ShellsTests
{
    [Fact]
    public void PowerShell7QuandoEstaNoPathSenaoOQueVemComOWindows()
    {
        var com = Shells.Comando("pwsh", f => f == @"C:\ps7\pwsh.exe", @"C:\outra;C:\ps7");
        Assert.StartsWith("\"C:\\ps7\\pwsh.exe\" -NoLogo -NoExit -EncodedCommand ", com.Linha);

        var sem = Shells.Comando("pwsh", _ => false, @"C:\outra");
        Assert.StartsWith("powershell.exe -NoLogo -NoExit -EncodedCommand ", sem.Linha);
    }

    [Fact]
    public void GitBashUsaOBashDaInstalacaoENaoOMintty()
    {
        var (linha, ambiente) = Shells.Comando("gitbash",
            f => f is @"C:\Git\cmd\git.exe" or @"C:\Git\bin\bash.exe", @"C:\Git\cmd");

        Assert.Equal("\"C:\\Git\\bin\\bash.exe\" --login -i", linha);
        Assert.Equal("1", ambiente!["CHERE_INVOKING"]);
    }

    [Fact]
    public void GitBashAusenteDizOQueFazer()
    {
        var erro = Assert.Throws<FileNotFoundException>(() => Shells.Comando("gitbash", _ => false, ""));
        Assert.Contains("Git Bash não encontrado", erro.Message);
    }

    [Fact]
    public void ShellDesconhecidoCaiNoPadrao() => Assert.Equal(Shells.Padrao, Shells.Achar("zsh").Id);
}

public class AtividadeTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void EnterComecaEAMarcaDoPromptTerminaComOCodigoDeSaida()
    {
        var a = new Atividade();

        a.Saida(B("\x1b]133;D;0\x07PS> "), T0); // primeiro prompt: nada rodou ainda
        Assert.Equal(EstadoDoTerminal.Ocioso, a.Estado);

        a.Entrada(B("dir"), T0);
        Assert.Equal(EstadoDoTerminal.Ocioso, a.Estado);
        a.Entrada(B("\r"), T0);
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Saida(B("saída\r\n\x1b]133;D;0\x07PS> "), T0);
        Assert.Equal(EstadoDoTerminal.Concluido, a.Estado);

        a.Entrada(B("\r"), T0);
        a.Saida(B("\x1b]133;D;3\x07PS> "), T0);
        Assert.Equal(EstadoDoTerminal.Erro, a.Estado);
    }

    /// <summary>O cmd fecha com ESC \ e não informa código; e a leitura corta em qualquer byte.</summary>
    [Fact]
    public void MarcaCortadaEntrePedacosESemCodigo()
    {
        var a = new Atividade();
        a.Entrada(B("\r"), T0);

        foreach (var pedaco in new[] { "ok\r\n\x1b", "]13", "3;D\x1b", "\\C:\\>" })
            a.Saida(B(pedaco), T0);

        Assert.Equal(EstadoDoTerminal.Concluido, a.Estado);
    }

    [Fact]
    public void CampainhaNoMeioDoComandoPedeAtencaoAteAlguemDigitar()
    {
        var a = new Atividade();
        a.Saida(B("\x07"), T0); // no prompt (Tab sem opção) não é pedido de nada
        Assert.Equal(EstadoDoTerminal.Ocioso, a.Estado);

        a.Entrada(B("\r"), T0);
        a.Saida(B("Continuar? \x07"), T0);
        a.Saida(B("(s/n) "), T0);
        Assert.Equal(EstadoDoTerminal.Aguardando, a.Estado);

        a.Entrada(B("s"), T0);
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Saida(B("\x1b]9;4;1;50\x07"), T0); // barra de progresso não é notificação
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);
        a.Saida(B("\x1b]9;terminei de pensar\x07"), T0);
        Assert.Equal(EstadoDoTerminal.Aguardando, a.Estado);
    }

    [Fact]
    public void SilencioViraAguardandoEVoltaQuandoASaidaRetoma()
    {
        var a = new Atividade();
        a.Entrada(B("\r"), T0);

        a.Tique(T0 + Atividade.Silencio - TimeSpan.FromSeconds(1));
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Tique(T0 + Atividade.Silencio);
        Assert.Equal(EstadoDoTerminal.Aguardando, a.Estado);

        a.Saida(B("compilando..."), T0 + Atividade.Silencio);
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);
    }
}

public class ClaudeTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Com o Claude aberto o estado é o que os hooks dizem, do cia até a saída.</summary>
    [Fact]
    public void AvisosDoClaudeMandamNoEstado()
    {
        var a = new Atividade();
        a.Entrada(B("cia Financeiro\r"), T0);
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Claude("pronto"); // abriu e está parado no prompt dele
        Assert.Equal(EstadoDoTerminal.Ocioso, a.Estado);

        a.Entrada(B("\r"), T0); // Enter em menu ou quebra de linha não é pedido
        Assert.Equal(EstadoDoTerminal.Ocioso, a.Estado);

        a.Claude("rodando");
        a.Tique(T0 + TimeSpan.FromMinutes(3)); // pensar calado não é esperar
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Claude("aguardando");
        a.Saida(B("Permitir este comando?"), T0);
        Assert.Equal(EstadoDoTerminal.Aguardando, a.Estado);
        a.Entrada(B("1"), T0); // respondeu: volta a rodar sem esperar o próximo aviso
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);

        a.Claude("concluido");
        Assert.Equal(EstadoDoTerminal.Concluido, a.Estado);

        a.Claude("rodando");
        a.Saida(B("\x1b]133;D;1\x07PS> "), T0); // morreu sem avisar: o prompt do shell voltou
        Assert.Equal(EstadoDoTerminal.Erro, a.Estado);
        a.Entrada(B("\r"), T0); // e o Enter volta a valer
        Assert.Equal(EstadoDoTerminal.Rodando, a.Estado);
    }

    [Fact]
    public void InstalarERemoverHooksPreservaOQueJaHavia()
    {
        var conta = Directory.CreateTempSubdirectory("gterm-conta-").FullName;
        try
        {
            var arquivo = Path.Combine(conta, "settings.json");
            File.WriteAllText(arquivo, """
                {
                  "model": "opus",
                  "language": "português",
                  "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo do-usuario" } ] } ] }
                }
                """);

            Assert.False(ClaudeHooks.Instalado(conta));
            ClaudeHooks.Instalar(conta, @"D:\Apps\GTerm\GTerm.exe");
            ClaudeHooks.Instalar(conta, @"D:\Apps\GTerm\GTerm.exe"); // de novo não duplica
            Assert.True(ClaudeHooks.Instalado(conta));

            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arquivo))!;
            Assert.Equal("português", json["language"]!.GetValue<string>());
            Assert.Equal(2, json["hooks"]!["Stop"]!.AsArray().Count);
            Assert.Equal("D:/Apps/GTerm/GTerm.exe --estado concluido",
                json["hooks"]!["Stop"]![1]!["hooks"]![0]!["command"]!.GetValue<string>());
            Assert.Equal("permission_prompt|elicitation_dialog",
                json["hooks"]!["Notification"]![0]!["matcher"]!.GetValue<string>());
            Assert.Contains("\"model\": \"opus\"", File.ReadAllText(arquivo + ".antes-do-gterm"));

            // o app mudou de pasta: os hooks continuam lá, mas chamam o executável antigo
            const string novo = @"D:\Programas\GTerm\GTerm.exe";
            Assert.False(ClaudeHooks.Desatualizado(conta, @"d:\apps\gterm\GTerm.exe"));
            Assert.True(ClaudeHooks.Desatualizado(conta, novo));
            Assert.Equal(new[] { conta }, ClaudeHooks.Corrigir(new[] { conta }, novo));
            Assert.Empty(ClaudeHooks.Corrigir(new[] { conta }, novo));
            json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arquivo))!;
            Assert.Equal("D:/Programas/GTerm/GTerm.exe --estado concluido",
                json["hooks"]!["Stop"]![1]!["hooks"]![0]!["command"]!.GetValue<string>());

            ClaudeHooks.Remover(conta);
            Assert.False(ClaudeHooks.Instalado(conta));
            Assert.False(ClaudeHooks.Desatualizado(conta, novo)); // sem hooks não há o que corrigir
            json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arquivo))!;
            Assert.Equal("echo do-usuario", json["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
            Assert.Null(json["hooks"]!["Notification"]);
            Assert.Equal("opus", json["model"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(conta, true);
        }
    }

    /// <summary>
    /// A conta padrão informada por extenso não vira CLAUDE_CONFIG_DIR: com a variável o
    /// Claude deixa de achar o login dela e pede para entrar de novo.
    /// </summary>
    [Fact]
    public void ContaPadraoNaoVaiParaOAmbienteDoTerminal()
    {
        var padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        foreach (var conta in new[] { "", padrao, padrao.ToUpperInvariant() + "\\" })
        {
            var ambiente = Arranque.DoProjeto(new Projeto { Shell = "cmd", Conta = conta }).Ambiente!;
            Assert.False(ambiente.ContainsKey("CLAUDE_CONFIG_DIR"), conta);
        }

        var outra = Arranque.DoProjeto(new Projeto { Shell = "cmd", Conta = padrao + "-bm" }).Ambiente!;
        Assert.Equal(padrao + "-bm", outra["CLAUDE_CONFIG_DIR"]);

        // o Claude usa o cursor do terminal (a barra), não o bloco que ele desenha
        var claude = Arranque.DoClaude(new Projeto(), "claude.exe", "").Ambiente!;
        Assert.Equal("1", claude["CLAUDE_CODE_NATIVE_CURSOR"]);
        Assert.False(claude.ContainsKey("CLAUDE_CONFIG_DIR"));
    }

    [Fact]
    public void ArgumentoQueNaoEDeHookAbreOAppNormalmente()
    {
        Assert.False(ClaudeHooks.Avisar(Array.Empty<string>()));
        Assert.False(ClaudeHooks.Avisar(new[] { "arquivo.txt" }));
        Assert.Equal("aguardando", ClaudeHooks.Evento("aguardando 638000000000000000"));
    }
}

public class ConPtyTests
{
    /// <summary>O que se escreve chega ao shell, a saída volta, e ele abre na pasta pedida.</summary>
    [Fact]
    public async Task CmdNoPseudoconsoleRespondeNaPastaPedida()
    {
        var pasta = Directory.CreateTempSubdirectory("gterm-pty-").FullName;
        try
        {
            using var pty = ConPty.Iniciar("cmd.exe", pasta, 120, 30);

            var saida = new StringBuilder();
            _ = Task.Run(() =>
            {
                var buf = new byte[4096];
                try
                {
                    int n;
                    while ((n = pty.Saida.Read(buf, 0, buf.Length)) > 0)
                        lock (saida) saida.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });

            pty.Escrever(Encoding.UTF8.GetBytes("set /a 40+2 & cd\r"));

            var relogio = Stopwatch.StartNew();
            string texto;
            do
            {
                await Task.Delay(100);
                lock (saida) texto = saida.ToString();
            } while (!texto.Contains("42") && relogio.Elapsed < TimeSpan.FromSeconds(20));

            Assert.Contains("42", texto);
            Assert.Contains(Path.GetFileName(pasta), texto);
        }
        finally
        {
            try { Directory.Delete(pasta, true); } catch (IOException) { }
        }
    }
}

/// <summary>
/// O que o GTerm lê do cia: as bases, o estado dele (favoritos e recentes) e a sessão que
/// ele retoma. Tudo em pastas temporárias.
/// </summary>
public class CiaTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("gterm-cia-").FullName;

    public void Dispose()
    {
        try
        {
            // os objetos do git são somente leitura
            foreach (var f in Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_raiz, true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void ProjetosDasBasesComOTipoComoNoCia()
    {
        var b = Path.Combine(_raiz, "DBISAM");
        void Projeto(string nome, string? arquivo)
        {
            Directory.CreateDirectory(Path.Combine(b, nome));
            if (arquivo is not null) File.WriteAllText(Path.Combine(b, nome, arquivo), "");
        }
        Projeto("Financeiro", "Financeiro.dpr");
        Projeto("bm-api", "BmApi.csproj");
        Projeto("Painel", "go.mod");
        Projeto("site", "package.json");
        Projeto("scripts", "gera.py");
        Projeto("Setup", "publicar.ps1");
        Projeto("Docs", null);
        var oculta = Directory.CreateDirectory(Path.Combine(b, ".vs"));
        oculta.Attributes |= FileAttributes.Hidden;

        var projetos = Cia.Projetos(new[] { b + "\\", Path.Combine(_raiz, "não-existe") });

        Assert.Equal(new[] { "bm-api", "Docs", "Financeiro", "Painel", "scripts", "Setup", "site" },
            projetos.Select(p => p.Nome));
        Assert.Equal(new[] { "csharp", "pasta", "delphi", "go", "python", "pwsh", "node" },
            projetos.Select(p => p.Tipo));
        Assert.All(projetos, p => Assert.Equal(b, p.Base));
        Assert.Equal(char.ConvertFromUtf32(0xF0B0B), Cia.Icone("delphi").Glifo);
    }

    /// <summary>
    /// O ConvertTo-Json do PowerShell grava lista de um item só como o item, e o formato
    /// antigo do estado guardava só o nome do projeto.
    /// </summary>
    [Fact]
    public void EstadoDoCiaNoFormatoDeleENoAntigo()
    {
        var arquivo = Path.Combine(_raiz, "estado.json");
        Assert.Same(EstadoDoCia.Vazio, Cia.LerEstado(arquivo));

        File.WriteAllText(arquivo, """
            {
              "favoritos": "Financeiro",
              "recentes": [
                { "nome": "Notas", "quando": "2026-10-27T22:18:50", "pedido": 6529, "modo": "writer" },
                { "caminho": "D:\\Projetos\\Private\\GTerm", "nome": "GTerm", "quando": "", "pedido": "", "modo": "memória" }
              ]
            }
            """);
        var estado = Cia.LerEstado(arquivo)!;

        Assert.Equal(new[] { "Financeiro" }, estado.Favoritos);
        Assert.Equal("Notas", estado.Recentes[0].Referencia);
        Assert.Equal(new DateTime(2026, 10, 27, 22, 18, 50), estado.Recentes[0].Quando);
        Assert.Equal("6529", estado.Recentes[0].Pedido);
        Assert.Equal(@"D:\Projetos\Private\GTerm", estado.Recentes[1].Referencia);
        Assert.Null(estado.Recentes[1].Quando);

        // o cia gravando no mesmo instante: quem lê fica com o que já tinha
        File.WriteAllText(arquivo, """{ "favoritos": [ "Fin""");
        Assert.Null(Cia.LerEstado(arquivo));
    }

    [Fact]
    public void GravarFavoritosDeixaORestoDoArquivoComoEstava()
    {
        var arquivo = Path.Combine(_raiz, "cia", "estado.json");
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, """
            {
              "favoritos": [ "D:\\Projetos\\DBISAM\\Financeiro" ],
              "recentes": [ { "caminho": "D:\\Projetos\\Private\\GTerm", "modo": "memória" } ],
              "sessoes": { "Financeiro": "abc" }
            }
            """);

        Cia.GravarFavoritos(arquivo, new[] { @"D:\Projetos\Private\GTerm", @"D:\Projetos\DBISAM\Financeiro" });

        var texto = File.ReadAllText(arquivo);
        Assert.Contains("memória", texto); // acento legível, como o cia grava
        Assert.Contains("\"abc\"", texto);
        Assert.NotEqual(0xEF, File.ReadAllBytes(arquivo)[0]); // sem BOM
        Assert.False(File.Exists(arquivo + ".tmp"));

        var estado = Cia.LerEstado(arquivo)!;
        Assert.Equal(new[] { @"D:\Projetos\Private\GTerm", @"D:\Projetos\DBISAM\Financeiro" }, estado.Favoritos);
        Assert.Equal(@"D:\Projetos\Private\GTerm", Assert.Single(estado.Recentes).Referencia);
    }

    /// <summary>
    /// A mesma escolha do Get-UltimaSessao do cia-dash: a sessão mais recente que teve
    /// conversa, com o título que o usuário deu na frente do que o Claude gerou.
    /// </summary>
    [Fact]
    public void UltimaSessaoEAQueOCiaRetoma()
    {
        var pasta = @"D:\Projetos\DBISAM\Financeiro";
        var conta = Path.Combine(_raiz, ".claude");
        Assert.Null(Cia.UltimaSessao(pasta, conta));

        var dir = Path.Combine(conta, "projects", "D--Projetos-DBISAM-Financeiro");
        Directory.CreateDirectory(dir);
        void Sessao(string id, DateTime quando, params string[] linhas)
        {
            var arquivo = Path.Combine(dir, id + ".jsonl");
            File.WriteAllLines(arquivo, linhas);
            File.SetLastWriteTime(arquivo, quando);
        }

        var hoje = DateTime.Today;
        Sessao("antiga", hoje.AddDays(-3), """{"type":"ai-title","aiTitle":"Remessa"}""");
        Sessao("com-conversa", hoje.AddHours(-2),
            """{"type":"user","message":"oi"}""",
            """{"type":"ai-title","aiTitle":"Boleto do Sicoob"}""",
            """{"type":"custom-title","customTitle":"Ajuste no boleto"}""",
            new string('x', 2048));
        Sessao("aberta-e-fechada", hoje.AddHours(-1), """{"type":"user","message":"oi"}""");
        Sessao("cortada", hoje.AddMinutes(-30), """{"type":"last-prompt","lastPrompt":"continua""");

        var s = Cia.UltimaSessao(pasta, conta)!;
        Assert.Equal("com-conversa", s.Id);
        Assert.Equal("Ajuste no boleto", s.Titulo);
        Assert.Equal(hoje.AddHours(-2), s.Quando);
        Assert.Equal(2, s.KB);
    }

    /// <summary>As decisões do Open-CIA, uma por linha: é o que o Claude recebe na linha de comando.</summary>
    [Fact]
    public void AberturaDecididaComoOCia()
    {
        var pequena = new UltimaSessao("abc", DateTime.Now, 120, "Boleto");
        var grande = pequena with { KB = Cia.CompactarKB };
        UltimaSessao? NaoProcura() => throw new InvalidOperationException("a sessão não devia ser procurada");

        Assert.Equal(new Abertura("--resume abc", "retomada", ""), Cia.Decidir(null, true, () => pequena));
        Assert.Equal(new Abertura("--resume abc /compact", "retomada", ""), Cia.Decidir(null, false, () => grande));
        Assert.Equal(new Abertura("/delphi-writer", "writer", ""), Cia.Decidir(null, true, () => null));
        Assert.Equal(new Abertura("/retomar", "memória", ""), Cia.Decidir(null, false, () => null));
        Assert.Equal(new Abertura("/delphi-writer", "writer", ""), Cia.Decidir("-Novo", true, NaoProcura));
        Assert.Equal(new Abertura("\"/pedido 6529\"", "writer", "6529"), Cia.Decidir("6529", true, NaoProcura));
        Assert.Equal(new Abertura("/delphi-pr", "pr", ""), Cia.Decidir("-Pr", true, NaoProcura));
        Assert.Equal(new Abertura("", "limpo", ""), Cia.Decidir("-Limpo", false, NaoProcura));
    }

    [Fact]
    public void ClaudeDoPathOuDoInstaladorNativo()
    {
        var existe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\b\claude.exe", @"C:\Users\x\.local\bin\claude.exe",
        };
        Assert.Equal(@"C:\b\claude.exe", Cia.Executavel(existe.Contains, @"C:\a; C:\b", @"C:\Users\x"));
        Assert.Equal(@"C:\Users\x\.local\bin\claude.exe", Cia.Executavel(existe.Contains, @"C:\a", @"C:\Users\x"));
        Assert.Null(Cia.Executavel(existe.Contains, @"C:\a", @"C:\Users\y"));
    }

    /// <summary>Como o cia-dash -Registrar: na frente, um por pasta (ou nome, no formato antigo), oito no máximo.</summary>
    [Fact]
    public void RecenteVaiParaAFrenteComoOCiaGrava()
    {
        var arquivo = Path.Combine(_raiz, "estado.json");
        var antigos = string.Join(",\n", Enumerable.Range(1, 8).Select(i =>
            $$"""{ "caminho": "D:\\Projetos\\DBISAM\\M{{i}}", "nome": "M{{i}}", "quando": "", "pedido": "", "modo": "writer" }"""));
        File.WriteAllText(arquivo, $$"""
            {
              "favoritos": [ "D:\\Projetos\\DBISAM\\Financeiro" ],
              "recentes": [
                { "nome": "Financeiro", "quando": "", "pedido": "", "modo": "writer" },
                { "caminho": "d:\\projetos\\dbisam\\FINANCEIRO", "nome": "Financeiro", "quando": "", "pedido": "", "modo": "pr" },
                {{antigos}}
              ],
              "sessoes": { "Financeiro": "abc" }
            }
            """);

        Cia.RegistrarRecente(arquivo, @"D:\Projetos\DBISAM\Financeiro", "6529", "writer", new DateTime(2026, 10, 27, 15, 30, 0));

        var estado = Cia.LerEstado(arquivo)!;
        Assert.Equal(8, estado.Recentes.Count);
        Assert.Equal(new Recente(@"D:\Projetos\DBISAM\Financeiro", new DateTime(2026, 10, 27, 15, 30, 0), "6529", "writer"),
            estado.Recentes[0]);
        Assert.Equal(new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7" },
            estado.Recentes.Skip(1).Select(r => Path.GetFileName(r.Referencia)));
        Assert.Equal(new[] { @"D:\Projetos\DBISAM\Financeiro" }, estado.Favoritos);
        Assert.Contains("\"abc\"", File.ReadAllText(arquivo));
    }

    [Fact]
    public void QuandoETamanhoComoNoCia()
    {
        var agora = new DateTime(2026, 10, 27, 15, 0, 0);
        Assert.Equal("agora", Cia.Quando(agora.AddSeconds(-30), agora));
        Assert.Equal("há 50 min", Cia.Quando(agora.AddMinutes(-50), agora));
        Assert.Equal("há 6 h", Cia.Quando(new DateTime(2026, 10, 27, 9, 0, 0), agora));
        Assert.Equal("ontem", Cia.Quando(new DateTime(2026, 10, 26, 23, 0, 0), agora));
        Assert.Equal("há 7 dias", Cia.Quando(new DateTime(2026, 10, 20, 8, 0, 0), agora));
        Assert.Equal("01/09/2026", Cia.Quando(new DateTime(2026, 9, 1, 8, 0, 0), agora));
        Assert.Equal("340 KB", Cia.Tamanho(340));
        Assert.Equal("1,3 MB", Cia.Tamanho(1300));
    }

    /// <summary>
    /// O quadro do projeto, como o Get-GitInfo do cia-dash lê: o branch, o que falta mandar
    /// e receber, os arquivos alterados (novos contam) e o último commit.
    /// </summary>
    [Fact]
    public void GitDoProjetoComoOCiaMostra()
    {
        var lido = Cia.LerGit("""
            # branch.oid 0123456789abcdef
            # branch.head develop
            # branch.upstream origin/develop
            # branch.ab +2 -1
            1 .M N... 100644 100644 100644 aaa bbb Financeiro.pas
            ? Novo.pas

            """, "1761660000\tMerge pull request #1 from bmsoft\n");
        Assert.Equal(new InfoDoGit(true, false, "develop", 2, 2, 1,
            DateTimeOffset.FromUnixTimeSeconds(1761660000).LocalDateTime, "Merge pull request #1 from bmsoft"), lido);
        Assert.Equal(new InfoDoGit(true, false, "(detached)", 0, 0, 0, null, ""),
            Cia.LerGit("# branch.oid (initial)\n# branch.head (detached)\n", null));

        // pasta sem .git nem chama o git
        Assert.Same(InfoDoGit.SemGit, Cia.Git(_raiz));

        // um repositório de verdade, com um commit e uma alteração
        var repo = Directory.CreateDirectory(Path.Combine(_raiz, "repo")).FullName;
        bool Git(params string[] argumentos)
        {
            var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-C", repo, "-c", "user.name=Teste", "-c", "user.email=teste@gterm" }.Concat(argumentos))
                info.ArgumentList.Add(a);
            try
            {
                using var git = Process.Start(info)!;
                git.WaitForExit();
                return git.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
        if (!Git("init", "-b", "principal")) return; // sem o git instalado não há o que testar
        File.WriteAllText(Path.Combine(repo, "a.txt"), "1");
        Assert.True(Git("add", "."));
        Assert.True(Git("commit", "-m", "Primeiro commit"));
        File.WriteAllText(Path.Combine(repo, "a.txt"), "2");

        var git = Cia.Git(repo);
        Assert.Equal("principal", git.Branch);
        Assert.Equal(1, git.Alterados);
        Assert.Equal("Primeiro commit", git.Mensagem);
        Assert.InRange(git.Commit!.Value, DateTime.Now.AddMinutes(-5), DateTime.Now.AddMinutes(1));
    }
}

/// <summary>Tudo que grava o workspace fica nesta classe: dentro dela o xunit roda em série.</summary>
public class JanelaTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("gterm-proj-").FullName;

    private string Bmsoft => Path.Combine(_raiz, "DBISAM");
    private string Particular => Path.Combine(_raiz, "Private");
    private string Estado => Path.Combine(_raiz, "cia", "estado.json");
    private string Financeiro => Path.Combine(Bmsoft, "Financeiro");
    private string Notas => Path.Combine(Bmsoft, "Notas");
    private string PastaDoGTerm => Path.Combine(Particular, "GTerm");

    public JanelaTests() => File.Delete(WorkspaceStore.FilePath);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    /// <summary>
    /// Duas bases como as do cia: Financeiro e Notas na da BMSoft, GTerm na Private.
    /// Financeiro é favorito e Notas foi aberto há pouco pelo cia, com pedido. Shell cmd e o
    /// cia trocado por um echo: nenhum teste chama o cia nem o Claude de verdade.
    /// </summary>
    private Workspace Montar()
    {
        void Projeto(string pasta, string arquivo)
        {
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, arquivo), "");
        }
        Projeto(Financeiro, "Financeiro.dpr");
        Projeto(Notas, "Notas.dproj");
        Projeto(PastaDoGTerm, "GTerm.sln");
        File.WriteAllText(Path.Combine(Notas, "CLAUDE.md"), "");

        Directory.CreateDirectory(Path.GetDirectoryName(Estado)!);
        File.WriteAllText(Estado, $$"""
            {
              "favoritos": [ {{Json(Financeiro)}} ],
              "recentes": [
                { "caminho": {{Json(Notas)}}, "nome": "Notas", "quando": "{{DateTime.Now.AddMinutes(-20):s}}", "pedido": "6529", "modo": "writer" }
              ],
              "sessoes": { "Financeiro": "abc" }
            }
            """);

        var ws = new Workspace
        {
            Bases = new() { Bmsoft, Particular },
            EstadoDoCia = Estado,
            Shell = "cmd",
            Claude = "cmd.exe /k echo",
            UltimaPasta = Financeiro,
        };
        WorkspaceStore.Save(ws);
        return ws;
    }

    private static string Json(string texto) => System.Text.Json.JsonSerializer.Serialize(texto);

    private static async Task<bool> Esperar(Func<bool> condicao)
    {
        var relogio = Stopwatch.StartNew();
        do
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        } while (!condicao() && relogio.Elapsed < TimeSpan.FromSeconds(20));
        return condicao();
    }

    private static string Tela(TerminalSessao s) =>
        string.Join("\n", Enumerable.Range(0, s.Modelo.Terminal.Rows).Select(i => s.Modelo.Terminal.Engine.GetLine(i)));

    private static void Foto(TopLevel janela, string nome)
    {
        if (Environment.GetEnvironmentVariable("GTERM_SHOTS") is not { Length: > 0 } shots) return;
        Directory.CreateDirectory(shots);
        using var frame = janela.CaptureRenderedFrame();
        frame?.Save(Path.Combine(shots, nome));
    }

    private static string[] Titulos(MainViewModel vm) => vm.Linhas.OfType<GrupoDaArvore>().Select(g => g.Titulo).ToArray();

    private static string[] Nomes(MainViewModel vm, string grupo) =>
        vm.Linhas.OfType<LinhaDoProjeto>().Where(l => l.Grupo == grupo).Select(l => l.Projeto.Nome).ToArray();

    private static ProjetoViewModel Projeto(MainViewModel vm, string nome) => vm.Projetos.Single(p => p.Nome == nome);

    /// <summary>Os textos à vista no quadro do projeto parado.</summary>
    private static List<string?> Quadro(Window janela) => janela.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("quadro"))
        .GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    [Fact]
    public void WorkspaceVoltaComoFoiGravado()
    {
        var ws = Montar();
        var lido = WorkspaceStore.Load();

        Assert.Equal(ws.Bases, lido.Bases);
        Assert.Equal(Estado, lido.EstadoDoCia);
        Assert.Equal("cmd.exe /k echo", lido.Claude);
        Assert.Equal("cmd", lido.Shell);
        Assert.Equal(Financeiro, lido.UltimaPasta);
        Assert.Null(lido.Recolhidos);
        Assert.StartsWith(Path.GetTempPath(), WorkspaceStore.FilePath, StringComparison.OrdinalIgnoreCase);

        // workspace novo dos testes nunca aponta para o cia de verdade
        Assert.Empty(new Workspace().Bases);
        Assert.StartsWith(Path.GetTempPath(), new Workspace().EstadoDoCia, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A janela de verdade, com a árvore populada: erro de binding só aparece quando o
    /// template é construído. A árvore é a do cia (Favoritos, Recentes e as bases, estas
    /// recolhidas), o último projeto volta selecionado e parado, e nada roda até o usuário
    /// mandar abrir. Abrir sobe o Claude direto, grava o recente e põe o projeto em Abertos.
    /// </summary>
    [AvaloniaFact]
    public async Task ArvoreDoCiaEOClaudeSoAbreNoBotao()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Private" }, Titulos(vm));
            Assert.Equal(new[] { "Financeiro" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            Assert.Equal("1", vm.Linhas.OfType<LinhaDoProjeto>().Single().Extra); // o [1] do cia: Ctrl+1
            Assert.Equal(5, janela.GetVisualDescendants().OfType<ListBoxItem>().Count());
            Assert.Equal(3, vm.Projetos.Count);

            var financeiro = vm.Selecionado!;
            Assert.Equal("Financeiro", financeiro.Nome);
            Assert.Same(financeiro, (vm.LinhaSelecionada as LinhaDoProjeto)?.Projeto);
            Assert.True(vm.MostrarParado);
            Assert.Null(vm.Aviso);
            Assert.False(vm.HaTerminalRodando);
            Assert.Empty(vm.Sessoes);
            Assert.True(financeiro.EhDaBmsoft);
            Assert.False(Projeto(vm, "GTerm").EhDaBmsoft);
            Assert.Equal(Cia.Icone("csharp").Glifo, Projeto(vm, "GTerm").Icone);

            // sem sessão do Claude nesta pasta: o botão diz o que o cia vai fazer
            Assert.True(await Esperar(() => financeiro.SessaoLida));
            Assert.Equal("Abrir o Claude", financeiro.RotuloDeAbrir);
            Assert.Contains("/delphi-writer", financeiro.TextoDaSessao);

            // e o quadro do cia: sem git, sem CLAUDE.md, nunca aberto
            Assert.True(await Esperar(() => financeiro.GitLido));
            Assert.Equal("Delphi", financeiro.Tipo);
            Dispatcher.UIThread.RunJobs();
            var quadro = Quadro(janela);
            Assert.Contains("sem git", quadro);
            Assert.Contains("não", quadro);
            Assert.DoesNotContain("carregando…", quadro);
            Assert.DoesNotContain("limpo", quadro);
            Assert.False(financeiro.TemSessao);
            Assert.False(financeiro.TemAbertura);

            // a pesquisa começa na altura do cartão do terminal; os outros jeitos de abrir são pílulas
            double Topo(Visual v) => v.TranslatePoint(default, janela)!.Value.Y;
            Assert.Equal(Topo(janela.FindControl<Border>("Cartao")!), Topo(janela.FindControl<TextBox>("CaixaDoFiltro")!));
            Assert.Equal(4, janela.GetVisualDescendants().OfType<Button>()
                .Count(b => b.Classes.Contains("colorida") && b.IsEffectivelyVisible));
            Foto(janela, "arvore.png");

            // o recente mostra quando e o pedido; a base aberta mostra os projetos dela
            vm.Linhas.OfType<GrupoDaArvore>().Single(g => g.Titulo == "Recentes").AlternarCommand.Execute(null);
            vm.Linhas.OfType<GrupoDaArvore>().Single(g => g.Titulo == "DBISAM").AlternarCommand.Execute(null);
            Assert.Equal(new[] { "Notas" }, Nomes(vm, MainViewModel.GrupoRecentes));
            Assert.Equal("há 20 min #6529", vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Grupo == MainViewModel.GrupoRecentes).Extra);
            Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, "base:" + Bmsoft));
            Assert.Equal(new[] { "base:" + Particular }, WorkspaceStore.Load().Recolhidos);
            Dispatcher.UIThread.RunJobs();
            Foto(janela, "arvore-aberta.png");

            // a estrela da linha favorita, ou tira dos favoritos, sem abrir o menu: cheia nos
            // favoritos, em qualquer grupo em que o projeto apareça
            Button Estrela(string grupo, string nome) => janela.GetVisualDescendants().OfType<Button>().Single(b =>
                b.Classes.Contains("estrela") && b.DataContext is LinhaDoProjeto l && l.Grupo == grupo && l.Projeto.Nome == nome);
            Assert.Contains("favorito", Estrela(MainViewModel.GrupoFavoritos, "Financeiro").Classes);
            Assert.Contains("favorito", Estrela("base:" + Bmsoft, "Financeiro").Classes);
            var estrelaDasNotas = Estrela("base:" + Bmsoft, "Notas");
            Assert.DoesNotContain("favorito", estrelaDasNotas.Classes);

            // a cheia fica à vista sem o mouse em cima; a vazada só na linha do mouse e na escolhida
            Assert.Equal(1, Estrela("base:" + Bmsoft, "Financeiro").Opacity);
            Assert.Equal(0, estrelaDasNotas.Opacity);
            estrelaDasNotas.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            Assert.Contains("favorito", Estrela("base:" + Bmsoft, "Notas").Classes);
            Assert.Equal("Tirar dos favoritos", ToolTip.GetTip(Estrela(MainViewModel.GrupoFavoritos, "Notas")));
            Estrela(MainViewModel.GrupoFavoritos, "Notas").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Financeiro" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            Assert.Equal(new[] { Financeiro }, Cia.LerEstado(Estado)!.Favoritos);

            await financeiro.IniciarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(financeiro.Parado);
            Assert.False(vm.MostrarParado);
            Assert.True(vm.HaTerminalRodando);

            // o aberto ganha uma aba, à frente e na cor do projeto; a árvore não muda
            Assert.Equal(new[] { financeiro }, vm.Abertos);
            Assert.True(financeiro.AVista);
            Assert.Equal(GroupPalette.DoNome("Financeiro"), financeiro.CorHex);
            Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Private" }, Titulos(vm));
            Assert.Single(janela.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("aba"));
            Assert.Single(janela.GetVisualDescendants().OfType<TerminalControl>());

            // a primeira aba não muda a altura da faixa: a pesquisa fica onde estava
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Topo(janela.FindControl<Border>("BarraDaAba")!), Topo(janela.FindControl<TextBox>("CaixaDoFiltro")!));

            // o Claude sobe direto, sem shell, com o que o cia escolheria (sem sessão: a skill),
            // e o projeto vai para a frente dos recentes do cia
            var sessao = Assert.Single(vm.Sessoes);
            Assert.True(await Esperar(() => Tela(sessao).Contains("/delphi-writer")), Tela(sessao));
            Assert.DoesNotContain("echo", Tela(sessao)); // nada foi digitado num shell
            Assert.Equal(new Recente(Financeiro, null, "", "writer"),
                Cia.LerEstado(Estado)!.Recentes[0] with { Quando = null });
            Assert.True(await Esperar(() => Nomes(vm, MainViewModel.GrupoRecentes).FirstOrDefault() == "Financeiro"),
                "o recente gravado não chegou à árvore");
            Foto(janela, "janela.png");

            // o filtro vira a lista dos que batem, com a base de cada um; Enter vai ao primeiro
            vm.Filtro = "gt";
            Assert.Equal(new[] { "Resultados" }, Titulos(vm));
            Assert.Equal("Private", vm.Linhas.OfType<LinhaDoProjeto>().Single().Extra);
            vm.EscolherPrimeiro();
            Assert.Equal("GTerm", vm.Selecionado?.Nome);
            Assert.True(vm.MostrarParado);
            Assert.False(vm.Sessoes[0].Ativa);
            Assert.False(financeiro.AVista); // a aba fica, sem estar à frente
            var gterm = vm.Selecionado!;
            Assert.True(await Esperar(() => gterm.SessaoLida));
            Assert.Contains("/retomar", gterm.TextoDaSessao);
            Dispatcher.UIThread.RunJobs();
            Foto(janela, "filtro.png");

            // sem filtro, o GTerm fica na base recolhida: sem linha marcada, mas à vista
            vm.Filtro = "";
            Assert.Null(vm.LinhaSelecionada);
            Assert.Equal("GTerm", vm.Selecionado?.Nome);
            Assert.Equal(PastaDoGTerm, WorkspaceStore.Load().UltimaPasta);

            // Ctrl+Tab passa pelos abertos; Ctrl+1 é o primeiro favorito, Ctrl+9 não existe
            vm.SelecionarVizinho(1);
            Assert.Same(financeiro, vm.Selecionado);
            Assert.True(vm.Sessoes[0].Ativa);
            vm.SelecionarPorNumero(9);
            Assert.Same(financeiro, vm.Selecionado);

            // o quadro do cia num projeto com CLAUDE.md, aberto pelo cia há pouco, num pedido.
            // O git e a sessão vêm postos à mão: a sessão estaria na conta do Claude de verdade
            var notas = Projeto(vm, "Notas");
            vm.Selecionado = notas;
            Assert.True(await Esperar(() => notas.GitLido && notas.SessaoLida));
            Assert.True(notas.TemClaudeMd);
            notas.Git = new InfoDoGit(true, false, "develop", 3, 1, 0, DateTime.Now.AddDays(-1), "Merge pull request #1 from bmsoft");
            notas.UltimaSessao = new UltimaSessao("abc", DateTime.Now.AddDays(-1), 748, "Contagem de MensagemInicial.ini por grupo");
            Dispatcher.UIThread.RunJobs();
            quadro = Quadro(janela);
            foreach (var texto in new[]
                     {
                         "develop", "↑1", "3 arquivos alterados", "último commit ontem", "Merge pull request #1 from bmsoft",
                         "sim", "sessão ontem", "748 KB", "Contagem de MensagemInicial.ini por grupo",
                         " · pedido 6529 (writer)",
                     })
                Assert.Contains(texto, quadro);
            Assert.Contains(quadro, t => t?.StartsWith("aberto há 2") == true); // os 20 min do Montar, e o que o teste levou
            Assert.DoesNotContain("limpo", quadro);
            Assert.DoesNotContain("sem git", quadro);
            Assert.Equal("Retomar a sessão", notas.RotuloDeAbrir);
            Assert.Equal("Abre com /compact: a sessão passou de 300 KB", notas.TextoDaSessao);
            Foto(janela, "quadro.png");
        }
        finally
        {
            vm.EncerrarTudo(); // sem terminal rodando a janela fecha sem perguntar
            janela.Close();
        }
    }

    /// <summary>
    /// A ordem dos favoritos (a do Ctrl+número e do [n] do cia) muda arrastando a linha: ela
    /// acompanha o mouse, e a ordem nova só é gravada ao soltar. Clique sem arrastar só
    /// escolhe, e Alt+setas faz o mesmo que o arrastar pelo teclado.
    /// </summary>
    [AvaloniaFact]
    public void FavoritosMudamDeOrdemArrastando()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Projeto(vm, "Notas").FavoritarCommand.Execute(null);
            Projeto(vm, "GTerm").FavoritarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Financeiro", "Notas", "GTerm" }, Nomes(vm, MainViewModel.GrupoFavoritos));

            // em cima do nome: a estrela e o ▶ ficam na ponta direita da linha
            Point Linha(string nome)
            {
                var item = janela.GetVisualDescendants().OfType<ListBoxItem>().Single(i =>
                    i.IsVisible && i.DataContext is LinhaDoProjeto { NosFavoritos: true } l && l.Projeto.Nome == nome);
                return item.TranslatePoint(new Point(60, item.Bounds.Height / 2), janela)!.Value;
            }

            var gterm = Linha("GTerm");
            janela.MouseDown(gterm, MouseButton.Left);
            janela.MouseMove(new Point(gterm.X, gterm.Y - 8), RawInputModifiers.LeftMouseButton);
            Assert.Equal(new[] { "Financeiro", "Notas", "GTerm" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            janela.MouseMove(Linha("Financeiro"), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "GTerm", "Financeiro", "Notas" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            Assert.Equal(new[] { Financeiro, Notas, PastaDoGTerm }, Cia.LerEstado(Estado)!.Favoritos); // ainda não soltou

            janela.MouseUp(Linha("GTerm"), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { PastaDoGTerm, Financeiro, Notas }, Cia.LerEstado(Estado)!.Favoritos);
            Assert.Equal(new[] { "1", "2", "3" },
                vm.Linhas.OfType<LinhaDoProjeto>().Where(l => l.NosFavoritos).Select(l => l.Extra));
            vm.SelecionarPorNumero(1);
            Assert.Equal("GTerm", vm.Selecionado?.Nome);

            var notas = Linha("Notas");
            janela.MouseDown(notas, MouseButton.Left);
            janela.MouseUp(notas, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Notas", vm.Selecionado?.Nome);
            Assert.Equal(new[] { PastaDoGTerm, Financeiro, Notas }, Cia.LerEstado(Estado)!.Favoritos);

            janela.FindControl<ListBox>("Lista")!.Focus();
            janela.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { PastaDoGTerm, Notas, Financeiro }, Cia.LerEstado(Estado)!.Favoritos);
            Assert.Equal("Notas", vm.Selecionado?.Nome);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>O motivo de o app existir: nada se encerra sem confirmação.</summary>
    [AvaloniaFact]
    public async Task EncerrarSoComConfirmacao()
    {
        Montar();
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos);
        try
        {
            var p = vm.Selecionado!;
            await p.IniciarCommand.ExecuteAsync(null);

            dialogos.Resposta = false;
            await p.EncerrarCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogos.Perguntas);
            Assert.True(p.Vivo);

            dialogos.Resposta = true;
            await p.EncerrarCommand.ExecuteAsync(null);
            Assert.False(p.Vivo);
            Assert.Empty(vm.Sessoes);
            Assert.True(vm.MostrarParado);
            Assert.Empty(vm.Abertos); // a aba vai junto
            Assert.False(vm.HaTerminalRodando);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    /// <summary>
    /// A marca do prompt atravessa o pseudoconsole de verdade: sem isso o ponto da árvore
    /// nunca sairia do amarelo.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("cmd", '>')]
    [InlineData("gitbash", '$')]
    public async Task ComandoNoShellDeVerdadeTerminaVerde(string shell, char prompt)
    {
        Directory.CreateDirectory(_raiz);
        using var sessao = new TerminalSessao(new Projeto { Pasta = _raiz, Shell = shell });
        try
        {
            sessao.Iniciar();
        }
        catch (FileNotFoundException)
        {
            return; // sem Git instalado não há o que testar
        }

        Assert.True(await Esperar(() => Tela(sessao).Contains(prompt)), "sem prompt");
        Assert.Equal(EstadoDoTerminal.Ocioso, sessao.Estado);

        sessao.Modelo.Send("echo oi\r");
        Assert.Equal(EstadoDoTerminal.Rodando, sessao.Estado);
        Assert.True(await Esperar(() => sessao.Estado == EstadoDoTerminal.Concluido), "ficou em " + sessao.Estado);
    }

    /// <summary>
    /// O comando de abertura é digitado quando o shell mostra o prompt, e o aviso de um
    /// hook chega à sessão pelo arquivo que ela mesma entregou ao shell na variável.
    /// </summary>
    [AvaloniaFact]
    public async Task ComandoDeAberturaRodaEOAvisoDoHookChega()
    {
        var projeto = new Projeto { Pasta = _raiz, Shell = "cmd", Comando = "echo aberto-%GTERM_ESTADO:~-7%" };
        using var sessao = new TerminalSessao(projeto);
        sessao.Iniciar();

        // a saída do echo prova as duas coisas: o comando rodou e a variável existe no shell
        Assert.True(await Esperar(() => Tela(sessao).Contains("\naberto-.estado")), Tela(sessao));
        Assert.True(await Esperar(() => sessao.Estado == EstadoDoTerminal.Concluido), "ficou em " + sessao.Estado);

        // o que o hook faria: o próprio shell grava o aviso no arquivo dele
        sessao.Modelo.Send("echo aguardando 1> \"%GTERM_ESTADO%\"\r");
        Assert.True(await Esperar(() => sessao.Estado == EstadoDoTerminal.Aguardando), "ficou em " + sessao.Estado);
    }

    /// <summary>
    /// Sair pelo comando do próprio programa, como o /exit do Claude: o Ctrl+C antes larga o
    /// que estava digitado, e o comando chega inteiro, com o Enter. Aqui, o exit do cmd.
    /// </summary>
    [AvaloniaFact]
    public async Task SairDigitaOComandoDeSaida()
    {
        using var sessao = new TerminalSessao(new Projeto { Pasta = _raiz, Shell = "cmd" });
        sessao.Iniciar();
        Assert.True(await Esperar(() => Tela(sessao).Contains('>')), "sem prompt");

        sessao.Modelo.Send("echo meio digitado");
        Assert.True(await sessao.SairAsync("exit", TimeSpan.FromSeconds(10)), Tela(sessao));
        Assert.True(sessao.Encerrada);
        Assert.DoesNotContain("digitadoexit", Tela(sessao));
        Assert.True(await sessao.SairAsync("exit", TimeSpan.FromSeconds(10))); // já saiu: nada a fazer
    }

    /// <summary>
    /// Os outros jeitos de abrir o Claude: com um já aberto, só depois de confirmar, e num
    /// processo novo. Ao sair do Claude, o Enter retoma a sessão em vez de repetir o modo.
    /// </summary>
    [AvaloniaFact]
    public async Task OutrosJeitosDeAbrirSoInterrompemComConfirmacao()
    {
        Montar();
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos) { PrazoDaSaida = TimeSpan.FromSeconds(1) }; // o echo não sai pelo /exit
        try
        {
            var p = vm.Selecionado!;
            await p.IniciarCommand.ExecuteAsync(null);
            var antes = p.Sessao!;
            antes.Medir(160, 30); // sem janela não há tela para medir; os próximos herdam esta medida
            Assert.True(await Esperar(() => Tela(antes).Contains("/delphi-writer")), Tela(antes));
            Assert.Equal(0, dialogos.Perguntas); // abrir num projeto parado não pergunta nada

            dialogos.Resposta = false;
            await p.SessaoNovaCommand.ExecuteAsync(null);
            Assert.Same(antes, p.Sessao);
            Assert.Equal(1, dialogos.Perguntas);

            dialogos.Resposta = true;
            dialogos.Texto = "12a"; // pedido vai para a linha de comando: só número
            await p.PedidoCommand.ExecuteAsync(null);
            Assert.Same(antes, p.Sessao);
            Assert.Contains("inválido", vm.Aviso);

            // o de antes recebe o /exit; como o echo não sai por ele, é encerrado no fim do prazo
            dialogos.Texto = "4321";
            await p.PedidoCommand.ExecuteAsync(null);
            Assert.Contains("/exit", Tela(antes));
            var pedido = p.Sessao!;
            Assert.NotSame(antes, pedido);
            Assert.True(p.Vivo);
            Assert.Null(vm.Aviso);
            Assert.True(await Esperar(() => Tela(pedido).Contains("\"/pedido 4321\"")), Tela(pedido));
            Assert.Equal("4321", Cia.LerEstado(Estado)!.Recentes[0].Pedido);

            // o Claude saiu: o terminal avisa, e o Enter abre de novo retomando, sem o pedido
            pedido.Modelo.Send("exit\r");
            Assert.True(await Esperar(() => pedido.Encerrada), "o processo não terminou");
            Assert.False(p.Vivo);
            Assert.Contains("Claude encerrado — Enter retoma a sessão", Tela(pedido));
            pedido.Modelo.Send("\r");
            Assert.True(await Esperar(() => !pedido.Encerrada && Tela(pedido).Contains("/delphi-writer")), Tela(pedido));
            Assert.Same(pedido, p.Sessao);
            Assert.Equal("", Cia.LerEstado(Estado)!.Recentes[0].Pedido);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    /// <summary>
    /// Favoritar grava no estado do cia sem mexer no resto dele, e o que o cia grava de lá
    /// (ou uma pasta nova na base) aparece na árvore sem reabrir o app.
    /// </summary>
    [AvaloniaFact]
    public async Task FavoritosSaoOsDoCiaNosDoisSentidos()
    {
        Montar();
        var vm = new MainViewModel(new Dialogos());
        try
        {
            var notas = Projeto(vm, "Notas");
            notas.FavoritarCommand.Execute(null);
            Assert.True(notas.Favorito);
            Assert.Equal("Tirar dos favoritos", notas.RotuloDoFavorito);
            Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, MainViewModel.GrupoFavoritos));

            // a ordem é a do Ctrl+número e do [n] do cia
            vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Projeto == notas).SubirCommand.Execute(null);
            Assert.Equal(new[] { "Notas", "Financeiro" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            vm.SelecionarPorNumero(1);
            Assert.Same(notas, vm.Selecionado);

            var gravado = Cia.LerEstado(Estado)!;
            Assert.Equal(new[] { Notas, Financeiro }, gravado.Favoritos);
            Assert.Equal(Notas, Assert.Single(gravado.Recentes).Referencia);
            Assert.Contains("\"abc\"", File.ReadAllText(Estado)); // as sessões do cim ficam

            Projeto(vm, "Financeiro").FavoritarCommand.Execute(null);
            Assert.Equal(new[] { Notas }, Cia.LerEstado(Estado)!.Favoritos);

            // o cia gravou de lá: entra o GTerm nos favoritos
            Cia.GravarFavoritos(Estado, new[] { Notas, PastaDoGTerm });
            Assert.True(await Esperar(() => Projeto(vm, "GTerm").Favorito), "o favorito gravado pelo cia não chegou");
            Assert.Equal(new[] { "Notas", "GTerm" }, Nomes(vm, MainViewModel.GrupoFavoritos));

            // pasta nova na base vira projeto
            Directory.CreateDirectory(Path.Combine(Bmsoft, "Compras"));
            Assert.True(await Esperar(() => vm.Projetos.Any(p => p.Nome == "Compras")), "a pasta nova não apareceu");
            Assert.Equal(new[] { "Compras", "Financeiro", "Notas", "GTerm" }, vm.Projetos.Select(p => p.Nome));
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    [AvaloniaFact]
    public async Task PreferenciasDeFonteValemNaHoraEFicamGravadas()
    {
        Montar();
        var dialogos = new Dialogos { PreferenciasNovas = new Preferencias("JetBrainsMono NFM", 15, "") };
        var vm = new MainViewModel(dialogos);
        try
        {
            Assert.Equal(MainViewModel.FontePadrao, vm.Fonte);
            Assert.Equal(13, vm.TamanhoDaFonte);

            await vm.AbrirPreferenciasCommand.ExecuteAsync(null);

            Assert.StartsWith("JetBrainsMono NFM,", vm.Fonte);
            Assert.Equal(15, vm.TamanhoDaFonte);
            Assert.Equal("JetBrainsMono NFM", WorkspaceStore.Load().Fonte);
            Assert.Equal(15, WorkspaceStore.Load().TamanhoDaFonte);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    [AvaloniaFact]
    public async Task PastaQueSumiuViraAvisoEmVezDeDerrubarOApp()
    {
        Montar();
        var vm = new MainViewModel(new Dialogos());
        try
        {
            Directory.Delete(Financeiro, true);
            await vm.Selecionado!.IniciarCommand.ExecuteAsync(null);

            Assert.Contains("Pasta não encontrada", vm.Aviso);
            Assert.Empty(vm.Sessoes);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    /// <summary>
    /// Aberto de dentro de uma sessão do Claude, o app não repassa aos shells o que herdou
    /// dela: foi o que deixou o prompt e o Claude sem cor depois de uma atualização.
    /// </summary>
    [Theory]
    [InlineData("NO_COLOR", true)]
    [InlineData("CLAUDECODE", true)]
    [InlineData("CLAUDE_CODE_CHILD_SESSION", true)]
    [InlineData("CLAUDE_CONFIG_DIR", true)]
    [InlineData("GTERM_ESTADO", true)]
    [InlineData("GTERM_HOME", false)]
    [InlineData("PATH", false)]
    [InlineData("POSH_THEMES_PATH", false)]
    public void SoAHerancaDoClaudeSaiDoAmbiente(string nome, bool herdada) =>
        Assert.Equal(herdada, ClaudeHooks.Herdada(nome));

    /// <summary>
    /// A cor é do projeto, a da aba: sem escolha, tirada do nome como no GTest; escolhida no
    /// menu da aba, vale na hora e fica gravada. A tela das preferências abre com as contas.
    /// </summary>
    [AvaloniaFact]
    public void CorDaAbaVemDoNomeOuDoMenuEFicaGravada()
    {
        var conta = Path.Combine(_raiz, ".claude-teste");
        Directory.CreateDirectory(conta);
        File.WriteAllText(Path.Combine(conta, ".credentials.json"), """{"claudeAiOauth":{"subscriptionType":"pro"}}""");
        Assert.Equal("PRO", ClaudeHooks.Plano(conta));

        // o plano do perfil vale mais que o das credenciais, que fica velho depois de um upgrade
        File.WriteAllText(Path.Combine(conta, ".claude.json"),
            """{"oauthAccount":{"emailAddress":"equipe@exemplo.com","organizationType":"claude_team"}}""");
        Assert.Equal("equipe@exemplo.com", ClaudeHooks.Identificar(conta).Rotulo(conta));
        Assert.Equal("TEAM", ClaudeHooks.Plano(conta));

        // a mesma conta do GTest: soma dos caracteres do nome, na paleta dos grupos do GRepos
        Assert.Equal(GroupPalette.Cores["Financeiro".Sum(c => c) % GroupPalette.Cores.Length], GroupPalette.DoNome("Financeiro"));

        Montar();
        var vm = new MainViewModel(new Dialogos());
        Assert.All(vm.Projetos, p => Assert.Equal(GroupPalette.DoNome(p.Nome), p.CorHex));
        Assert.Empty(WorkspaceStore.Load().Projetos); // cor automática não precisa ser lembrada

        var notas = Projeto(vm, "Notas");
        notas.EscolherCorCommand.Execute("#db4c9b");
        Assert.Equal("#DB4C9B", notas.CorHex);
        notas.EscolherCorCommand.Execute("não é cor"); // ignorado
        Assert.Equal("#DB4C9B", notas.CorHex);

        Assert.Equal("#DB4C9B", Assert.Single(WorkspaceStore.Load().Projetos).Cor);
        Assert.Equal("#DB4C9B", Projeto(new MainViewModel(new Dialogos()), "Notas").CorHex);

        var padrao = ClaudeHooks.PastaDaConta(null);
        var tela = new PreferenciasWindow(new Preferencias("", 13, ""), new[] { padrao, conta });
        try
        {
            tela.Show();
            Dispatcher.UIThread.RunJobs();
            Foto(tela, "preferencias.png");
        }
        finally
        {
            tela.Close();
        }
    }

    /// <summary>
    /// Dois projetos abertos, um fora da vista (sem tela para se medir). Ele termina um
    /// comando sem ninguém olhando: o nome fica marcado e o título da janela avisa, até o
    /// projeto ser visto. De quebra, o segundo terminal e a ordem dos abertos.
    /// </summary>
    [AvaloniaFact]
    public async Task ProjetoForaDaVistaChamaAtencao()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            vm.JanelaAtiva = true;

            var notas = Projeto(vm, "Notas");
            var financeiro = Projeto(vm, "Financeiro");
            await notas.IniciarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            await financeiro.IniciarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(financeiro, vm.Selecionado);
            Assert.Equal(new[] { notas, financeiro }, vm.Abertos);
            Assert.Equal(new[] { false, true }, vm.Abertos.Select(p => p.AVista));

            var oculta = notas.Sessao!;
            Assert.True(await Esperar(() => Tela(oculta).Contains("/delphi-writer")), "o Claude do projeto que não está à vista não subiu");
            Assert.True(await Esperar(() => Tela(financeiro.Sessao!).Contains("/delphi-writer")), "o Claude do projeto à vista não subiu");
            Assert.Equal(financeiro.Sessao!.Modelo.Terminal.Cols, oculta.Modelo.Terminal.Cols);

            // embaixo das abas, os botões da aba da frente: no lugar do menu dela
            var barra = janela.FindControl<Border>("BarraDaAba")!;
            Button Botao(string texto) =>
                barra.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == texto);
            Assert.True(barra.IsVisible);
            Assert.Same(financeiro, vm.AbaAtiva);
            Assert.Same(financeiro.EncerrarCommand, Botao("Encerrar o Claude").Command);
            Assert.Same(financeiro.PedidoCommand, Botao("Pedido…").Command);
            Assert.Null(janela.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("aba")).ContextMenu);

            // o Claude termina a resposta no projeto que ninguém está olhando: o que o hook
            // faria é gravar "concluido" no arquivo da sessão. Enquanto roda, não pede nada
            oculta.Modelo.Send("echo concluido> \"%GTERM_ESTADO%\"\r");
            Assert.False(notas.PedeAtencao);
            Assert.True(await Esperar(() => notas.PedeAtencao), "o projeto fora da vista não chamou atenção");
            Assert.Equal("GTerm — Notas espera por você", vm.Titulo);
            Assert.False(financeiro.PedeAtencao);

            // segundo terminal do projeto à vista: outro shell, embaixo do principal, e o
            // projeto passa a ser lembrado no workspace
            Assert.True(vm.PodeAuxiliar);
            await vm.AlternarAuxiliarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.AuxiliarVisivel);
            Assert.True(Assert.Single(WorkspaceStore.Load().Projetos).Auxiliar);
            var pilula = janela.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.AlternarAuxiliarCommand);
            Assert.Contains("ligado", pilula.Classes); // a pílula do canto fica acesa
            var auxiliar = financeiro.AuxiliarSessao!;
            Assert.True(await Esperar(() => Tela(auxiliar).Contains(Path.GetFileName(Financeiro) + ">")), "o segundo terminal não subiu");
            Foto(janela, "auxiliar.png");
            var grupos = janela.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("grupo")).ToList();
            Assert.NotEmpty(grupos);
            Assert.All(grupos, b => Assert.True(b.IsEffectivelyEnabled, "título de grupo desabilitado"));
            Assert.All(grupos, b => Assert.Equal(1, b.Opacity));

            // ir até o projeto é vê-lo: a marca sai, e ele não tem segundo terminal
            vm.SelecionarVizinho(-1);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(notas, vm.Selecionado);
            Assert.Equal(new[] { true, false }, vm.Abertos.Select(p => p.AVista));
            Assert.False(notas.PedeAtencao);
            Assert.Equal("GTerm", vm.Titulo);
            Assert.False(vm.AuxiliarVisivel);
            Assert.DoesNotContain("ligado", pilula.Classes);

            // a barra troca com a aba; a cor sai da paleta do botão dela
            Assert.Same(notas.EncerrarCommand, Botao("Encerrar o Claude").Command);
            var cor = janela.FindControl<Button>("CorDaAba")!;
            cor.Flyout!.ShowAt(cor);
            Dispatcher.UIThread.RunJobs();
            var rosa = ((MenuFlyout)cor.Flyout).Items.OfType<MenuItem>().Single(m => m.Header as string == "Rosa");
            Assert.NotNull(rosa.Command); // a paleta alcança o projeto, mesmo sendo um popup
            var corDoFinanceiro = financeiro.CorHex;
            rosa.Command!.Execute(rosa.CommandParameter);
            cor.Flyout.Hide();
            Assert.Equal("#DB4C9B", notas.CorHex);
            Assert.Equal(corDoFinanceiro, financeiro.CorHex); // só a da frente muda

            // o clique na aba traz o projeto de volta, com o segundo terminal dele
            financeiro.MostrarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(financeiro, vm.Selecionado);
            Assert.True(vm.AuxiliarVisivel);
            Foto(janela, "abas.png");
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// Fechar o segundo terminal mata um shell: só com confirmação. Encerrar o principal
    /// leva o segundo junto. O cim sem conta informada fica com a única conta à parte.
    /// </summary>
    [AvaloniaFact]
    public async Task SegundoTerminalSoFechaComConfirmacao()
    {
        Montar();
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos);
        try
        {
            var p = vm.Selecionado!;
            Assert.False(vm.PodeAuxiliar); // sem o terminal do projeto não há segundo
            await p.IniciarCommand.ExecuteAsync(null);

            await vm.AlternarAuxiliarCommand.ExecuteAsync(null);
            Assert.True(vm.AuxiliarVisivel);
            Assert.Equal(0, dialogos.Perguntas); // abrir não pergunta nada

            await vm.AlternarAuxiliarCommand.ExecuteAsync(null);
            Assert.True(vm.AuxiliarVisivel);
            Assert.Equal(1, dialogos.Perguntas);

            dialogos.Resposta = true;
            await p.EncerrarCommand.ExecuteAsync(null);
            Assert.Empty(vm.Sessoes);
            Assert.Empty(vm.Auxiliares); // o segundo terminal vai junto com o do projeto
            Assert.False(vm.HaTerminalRodando);
            Assert.True(vm.MostrarParado);
        }
        finally
        {
            vm.EncerrarTudo();
        }

        var bm = Path.Combine(_raiz, ".claude-bm");
        Assert.Equal(bm, ClaudeHooks.ContaDoProjeto("", "cim Financeiro -SemMenu", new[] { bm }));
        Assert.Equal("", ClaudeHooks.ContaDoProjeto("", "cim", new[] { bm, bm + "2" })); // duas: não dá para adivinhar
        Assert.Equal("", ClaudeHooks.ContaDoProjeto("", "cia Financeiro", new[] { bm }));
        Assert.Equal(@"C:\outra", ClaudeHooks.ContaDoProjeto(@"C:\outra", "cim", new[] { bm }));
    }

    private sealed class Dialogos : IDialogService
    {
        public bool Resposta;
        public int Perguntas;
        public string? Texto;
        public Preferencias? PreferenciasNovas;

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Perguntas++;
            return Task.FromResult(Resposta);
        }

        public Task<string?> PromptAsync(string title, string label) => Task.FromResult(Texto);

        public Task<Preferencias?> PreferenciasAsync(Preferencias atuais, IReadOnlyList<string> contas) =>
            Task.FromResult(PreferenciasNovas);
    }
}
