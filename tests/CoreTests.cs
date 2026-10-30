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
using GTerm.Controls;
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
        var grande = pequena with { KB = 500_000 };
        UltimaSessao? NaoProcura() => throw new InvalidOperationException("a sessão não devia ser procurada");

        Assert.Equal(new Abertura("--resume abc", "retomada", ""), Cia.Decidir(null, true, () => pequena));

        // de propósito, diferente do cia: sessão grande não compacta ao abrir. É o botão da aba
        Assert.Equal(new Abertura("--resume abc", "retomada", ""), Cia.Decidir(null, false, () => grande));
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

/// <summary>Conversas do Claude numa conta de teste, gravadas como o Claude grava.</summary>
internal static class ContaDeTeste
{
    private static string Json(string texto) => System.Text.Json.JsonSerializer.Serialize(texto);

    /// <summary>O cwd e o branch no começo, o título no fim. Devolve o id.</summary>
    /// <param name="linhas">No lugar do título: as linhas depois da primeira.</param>
    public static string Conversa(string conta, string pasta, string? titulo, DateTime quando, string? id = null,
        params string[] linhas)
    {
        id ??= Guid.NewGuid().ToString();
        var dir = Path.Combine(conta, "projects", SessoesDoClaude.Chave(pasta));
        Directory.CreateDirectory(dir);
        var arquivo = Path.Combine(dir, id + ".jsonl");
        File.WriteAllLines(arquivo, new[]
        {
            """{"type":"user","cwd":""" + Json(pasta) + ""","gitBranch":"develop","message":"oi"}""",
        }.Concat(titulo is null ? linhas : new[] { """{"type":"ai-title","aiTitle":""" + Json(titulo) + "}" }));
        File.SetLastWriteTime(arquivo, quando);
        return id;
    }
}

/// <summary>
/// As conversas do Claude como o cl (Modules\claude-sessoes.ps1) lê: título e branch pelo fim
/// do arquivo, a pasta de origem pelo começo, as favoritas no estado-cl.json dele.
/// </summary>
public class SessoesTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("gterm-cl-").FullName;
    private string Conta => Path.Combine(_raiz, ".claude");

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    [Fact]
    public void ConversasComoOClLe()
    {
        var notas = @"D:\Projetos\DBISAM\Notas";
        var agora = DateTime.Now;
        string Conversa(string id, int minutos, params string[] linhas) =>
            ContaDeTeste.Conversa(Conta, notas, null, agora.AddMinutes(-minutos), id, linhas);

        // o título do usuário vence o gerado, e o último gerado vence o primeiro; o branch é o
        // último, e HEAD (sem branch) não conta
        Conversa("personalizada", 10,
            """{"type":"ai-title","aiTitle":"Gerado"}""",
            """{"type":"custom-title","customTitle":"Do usuário"}""",
            """{"type":"ai-title","aiTitle":"Gerado depois"}""",
            """{"type":"user","gitBranch":"HEAD"}""");
        // aspas e acentos escapados, e quebra de linha vira espaço
        Conversa("escapada", 20, """{"type":"ai-title","aiTitle":"Ajuste \"r\u00e1pido\"\nno boleto"}""");
        // aberta e fechada sem conversa: o /resume esconde
        Conversa("sem-conversa", 5);
        // o título longe do fim: não está nos últimos 256 KB, está nos 4 MB
        Conversa("grande", 30, """{"type":"ai-title","aiTitle":"Achada nos 4 MB"}""",
            "{\"type\":\"assistant\",\"texto\":\"" + new string('x', 300 * 1024) + "\"}");
        // a linha final pela metade (o Claude gravando): vale a anterior completa
        Conversa("cortada", 40, """{"type":"last-prompt","lastPrompt":"antes"}""",
            """{"type":"last-prompt","lastPrompt":"pela met""");

        // aberta numa pasta e depois um cd: a origem é a que gera a chave da pasta
        var outra = ContaDeTeste.Conversa(Conta, notas, null, agora.AddMinutes(-50), "outra-pasta",
            """{"type":"user","cwd":"D:\\Projetos\\Outra"}""", """{"type":"last-prompt","lastPrompt":"continua"}""");
        var arquivo = Path.Combine(Conta, "projects", SessoesDoClaude.Chave(notas), outra + ".jsonl");
        File.WriteAllLines(arquivo, new[] { """{"type":"user","cwd":"D:\\Projetos\\Outra"}""" }.Concat(File.ReadAllLines(arquivo)));
        File.SetLastWriteTime(arquivo, agora.AddMinutes(-50));

        // um job em segundo plano: a pasta tem os 8 primeiros caracteres do id
        Directory.CreateDirectory(Path.Combine(Conta, "jobs", "escapada"));
        File.WriteAllText(Path.Combine(Conta, "jobs", "escapada", "state.json"), "{}");

        var leitor = new SessoesDoClaude();
        var lista = leitor.Listar(Conta);
        Assert.Equal(new[] { "personalizada", "escapada", "grande", "cortada", "outra-pasta" }, lista.Select(s => s.Id));
        Assert.Equal(new[] { "Do usuário", "Ajuste \"rápido\" no boleto", "Achada nos 4 MB", "antes", "continua" },
            lista.Select(s => s.Titulo));
        Assert.Equal("", lista[0].Branch);
        Assert.Equal("develop", lista[1].Branch);
        Assert.All(lista, s => Assert.Equal(notas, s.Pasta));
        Assert.All(lista, s => Assert.Equal("D--Projetos-DBISAM-Notas", s.Chave));
        Assert.True(lista[1].Bg);
        Assert.False(lista[0].Bg);
        Assert.All(lista, s => Assert.False(s.Aberta)); // nenhum claude.exe com elas

        // só o arquivo que mudou é lido de novo
        File.AppendAllLines(Path.Combine(Conta, "projects", "D--Projetos-DBISAM-Notas", "personalizada.jsonl"),
            new[] { """{"type":"custom-title","customTitle":"Renomeada"}""" });
        Assert.Equal("Renomeada", leitor.Listar(Conta).Single(s => s.Id == "personalizada").Titulo);

        // aberta: o sessions\<pid>.json do Claude, com o processo vivo
        Directory.CreateDirectory(Path.Combine(Conta, "sessions"));
        File.WriteAllText(Path.Combine(Conta, "sessions", "4242.json"), """{"pid":4242,"sessionId":"escapada"}""");
        File.WriteAllText(Path.Combine(Conta, "sessions", "4343.json"), """{"pid":4343,"sessionId":"grande"}""");
        Assert.Equal(new Dictionary<string, int> { ["escapada"] = 4242 }, SessoesDoClaude.Abertas(Conta, pid => pid == 4242));

        // a sessão que o cia retoma usa a mesma leitura: a mais nova com conversa
        Assert.Equal("personalizada", Cia.UltimaSessao(notas, Conta)!.Id);
        Assert.Equal("Renomeada", Cia.UltimaSessao(notas, Conta)!.Titulo);
    }

    /// <summary>Como o cl -f: a nova favorita entra na frente, e o resto do arquivo fica.</summary>
    [Fact]
    public void FavoritasSaoAsDoCl()
    {
        var arquivo = Path.Combine(_raiz, "estado-cl.json");
        Assert.Empty(SessoesDoClaude.LerFavoritas(arquivo)!);

        File.WriteAllText(arquivo, """{ "favoritas": "antiga", "outra": 1 }""");
        Assert.True(SessoesDoClaude.TrocarFavorita(arquivo, "nova"));
        Assert.Equal(new[] { "nova", "antiga" }, SessoesDoClaude.LerFavoritas(arquivo));
        Assert.Contains("\"outra\": 1", File.ReadAllText(arquivo));

        Assert.False(SessoesDoClaude.TrocarFavorita(arquivo, "antiga"));
        Assert.Equal(new[] { "nova" }, SessoesDoClaude.LerFavoritas(arquivo));

        // o cl gravando no mesmo instante: quem lê fica com o que já tinha
        File.WriteAllText(arquivo, """{ "favoritas": [ "no""");
        Assert.Null(SessoesDoClaude.LerFavoritas(arquivo));
    }

    [Fact]
    public void QuandoTamanhoEDiaComoNoCl()
    {
        var agora = new DateTime(2026, 10, 28, 15, 0, 0);
        Assert.Equal("há 40 min", SessoesDoClaude.Quando(agora.AddMinutes(-40), agora));
        Assert.Equal("há 6 h", SessoesDoClaude.Quando(new DateTime(2026, 10, 28, 9, 0, 0), agora));
        Assert.Equal("ontem 14:32", SessoesDoClaude.Quando(new DateTime(2026, 10, 27, 14, 32, 0), agora));
        Assert.Equal("25/10 09:15", SessoesDoClaude.Quando(new DateTime(2026, 10, 25, 9, 15, 0), agora));
        Assert.Equal("340 KB", SessoesDoClaude.Tamanho(340 * 1024));
        Assert.Equal("23,4 MB", SessoesDoClaude.Tamanho((long)(23.4 * 1024 * 1024)));
        Assert.Equal("1,8 GB", SessoesDoClaude.Tamanho((long)(1.8 * 1024 * 1024 * 1024)));
        Assert.Equal("Hoje", SessoesDoClaude.Dia(agora.AddHours(-3), agora));
        Assert.Equal("Ontem", SessoesDoClaude.Dia(new DateTime(2026, 10, 27, 23, 0, 0), agora));
        Assert.Equal("Esta semana", SessoesDoClaude.Dia(new DateTime(2026, 10, 22, 8, 0, 0), agora));
        Assert.Equal("Mais antigas", SessoesDoClaude.Dia(new DateTime(2026, 10, 21, 8, 0, 0), agora));
        Assert.Equal(new Abertura("--resume 8a73b8c7-ba5f", "retomada", ""),
            Cia.Decidir(Cia.ModoConversa + "8a73b8c7-ba5f", true, () => throw new InvalidOperationException()));
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
    private string ContaDoClaude => Path.Combine(_raiz, ".claude");
    private string EstadoDoCl => Path.Combine(_raiz, "cia", "estado-cl.json");
    private string Solto => Path.Combine(_raiz, "Solto");

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
            EstadoDoCl = EstadoDoCl,
            ContaDasSessoes = ContaDoClaude,
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

    /// <summary>Os textos à vista no quadro do projeto parado, ou no da conversa escolhida.</summary>
    private static List<string?> Quadro(Window janela) => janela.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("quadro") && b.IsEffectivelyVisible)
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

            // a pesquisa começa na altura do cartão do terminal; os outros jeitos de abrir são
            // pílulas, como Projetos e Sessões em cima, e a escolhida é a acesa. Todas da mesma altura
            double Topo(Visual v) => v.TranslatePoint(default, janela)!.Value.Y;
            Assert.Equal(Topo(janela.FindControl<Border>("Cartao")!), Topo(janela.FindControl<TextBox>("CaixaDoFiltro")!));
            List<Button> Pilulas() => janela.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && (b.Classes.Contains("colorida") || b.Classes.Contains("pilula") || b.Classes.Contains("aba")))
                .ToList();
            Assert.Equal(7, Pilulas().Count(b => b.Classes.Contains("colorida")));
            Assert.DoesNotContain("apagada", Pilulas().Single(b => b.Command == vm.VerProjetosCommand).Classes);
            Assert.Contains("apagada", Pilulas().Single(b => b.Command == vm.VerSessoesCommand).Classes);
            Assert.All(Pilulas(), b => Assert.Equal(24, b.Bounds.Height));
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

            // a primeira aba não muda a altura da faixa: a pesquisa fica onde estava. A barra
            // embaixo das abas tem a altura da pesquisa, e os botões dela são as mesmas pílulas
            Dispatcher.UIThread.RunJobs();
            var barra = janela.FindControl<Border>("BarraDaAba")!;
            var filtro = janela.FindControl<TextBox>("CaixaDoFiltro")!;
            Assert.Equal(Topo(barra), Topo(filtro));
            Assert.Equal(filtro.Bounds.Height, barra.Bounds.Height);
            Assert.All(barra.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible),
                b => Assert.Contains("colorida", b.Classes));
            Assert.All(Pilulas(), b => Assert.Equal(24, b.Bounds.Height));

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
            Assert.Equal("", notas.TextoDaSessao); // com sessão, nada a avisar
            Assert.False(notas.TemTextoDaSessao);
            Foto(janela, "quadro.png");
        }
        finally
        {
            vm.EncerrarTudo(); // sem terminal rodando a janela fecha sem perguntar
            janela.Close();
        }
    }

    /// <summary>
    /// O modo Sessões é o cl com tela: as conversas de todas as pastas, as favoritas em cima e
    /// as outras por dia. A pesquisa filtra pelo título e pelo projeto; escolher mostra o
    /// quadro da conversa no lugar do terminal; retomar abre a aba do projeto com o --resume
    /// dela, também de pasta fora das bases; e há o Claude sem conversa nenhuma.
    /// </summary>
    [AvaloniaFact]
    public async Task SessoesSaoOClComTela()
    {
        Montar();
        Directory.CreateDirectory(Solto);
        var boleto = ContaDeTeste.Conversa(ContaDoClaude, Notas, "Boleto do Sicoob", DateTime.Now.AddMinutes(-30));
        var quadro = ContaDeTeste.Conversa(ContaDoClaude, PastaDoGTerm, "Quadro do cia", DateTime.Now.AddDays(-1));
        var script = ContaDeTeste.Conversa(ContaDoClaude, Solto, "Script solto", DateTime.Now.AddDays(-3));
        File.WriteAllText(EstadoDoCl, $$"""{ "favoritas": [ "{{quadro}}" ] }""");

        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            vm.VerSessoesCommand.Execute(null);
            Assert.True(await Esperar(() => vm.SessoesLidas));
            Dispatcher.UIThread.RunJobs();

            LinhaDaSessao Linha(string id) => vm.Linhas.OfType<LinhaDaSessao>().Single(l => l.Sessao.Id == id);
            Assert.Equal(new[] { "Favoritas", "Hoje", "Esta semana" }, Titulos(vm));
            Assert.Equal(new[] { quadro, boleto, script }, vm.Linhas.OfType<LinhaDaSessao>().Select(l => l.Sessao.Id));
            Assert.StartsWith("Notas · há 3", Linha(boleto).Detalhe);
            Assert.Equal("GTerm", Linha(quadro).Projeto);
            Assert.Equal("Solto", Linha(script).Projeto);
            Assert.Equal("Sessões", vm.MarcaDoFiltro);
            Assert.Equal(3, janela.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("sessao")));

            // entrar nas Sessões mostra a primeira favorita, marcada na árvore
            Assert.Same(Linha(quadro), vm.SessaoAVista);
            Assert.Same(Linha(quadro), vm.LinhaSelecionada);
            Assert.True(vm.MostrarSessao);

            // a estrela cheia fica à vista na favorita
            var estrela = janela.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("estrela") && b.DataContext is LinhaDaSessao l && l.Sessao.Id == quadro);
            Assert.Contains("favorito", estrela.Classes);
            Assert.Equal(1, estrela.Opacity);

            // a pesquisa olha o título e o projeto
            vm.Filtro = "solto";
            Assert.Equal(new[] { script }, vm.Linhas.OfType<LinhaDaSessao>().Select(l => l.Sessao.Id));
            vm.Filtro = "gterm";
            Assert.Equal(new[] { quadro }, vm.Linhas.OfType<LinhaDaSessao>().Select(l => l.Sessao.Id));
            vm.Filtro = "";

            // escolher a conversa mostra o quadro dela no lugar do projeto parado
            vm.LinhaSelecionada = Linha(boleto);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Linha(boleto), vm.SessaoAVista);
            Assert.True(vm.MostrarSessao);
            Assert.False(vm.MostrarParado);
            Assert.Null(vm.Aviso);
            var textos = Quadro(janela);
            Assert.Contains("develop", textos);
            Assert.Contains(textos, t => t?.StartsWith("há 3") == true);
            Assert.Contains(boleto, textos);
            Foto(janela, "sessoes.png");

            // favoritar entra na frente das favoritas do cl, e o Hoje fica vazio
            Linha(boleto).FavoritarCommand.Execute(null);
            Assert.Equal(new[] { boleto, quadro }, SessoesDoClaude.LerFavoritas(EstadoDoCl));
            Assert.Equal(new[] { "Favoritas", "Esta semana" }, Titulos(vm));
            Assert.Same(Linha(boleto), vm.LinhaSelecionada);

            // retomar abre a aba do projeto com o --resume da conversa, e o terminal aparece
            await Linha(boleto).RetomarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var notas = Projeto(vm, "Notas");
            Assert.Same(notas, vm.Selecionado);
            Assert.False(vm.MostrarSessao);
            Assert.True(notas.AVista);
            Assert.True(await Esperar(() => Tela(notas.Sessao!).Contains("--resume " + boleto)), Tela(notas.Sessao!));
            Assert.Equal("retomada", Cia.LerEstado(Estado)!.Recentes[0].Modo);

            // fora das bases a pasta vira um projeto avulso, com aba e sem linha na árvore
            await Linha(script).RetomarCommand.ExecuteAsync(null);
            var solto = vm.Abertos.Last();
            Assert.Equal("Solto", solto.Nome);
            Assert.True(await Esperar(() => Tela(solto.Sessao!).Contains("--resume " + script)), Tela(solto.Sessao!));

            // o Claude sem conversa: limpo, na pasta pessoal, numa aba Claude
            await vm.NovaConversaCommand.ExecuteAsync(null);
            var claude = vm.Abertos.Last();
            Assert.Equal("Claude", claude.Nome);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), claude.Pasta);
            Assert.Same(claude, vm.Selecionado);
            Assert.Equal(new[] { "Notas", "Solto", "Claude" }, vm.Abertos.Select(p => p.Nome));

            // de volta aos projetos, a árvore do cia
            vm.VerProjetosCommand.Execute(null);
            Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Private" }, Titulos(vm));
            Assert.Equal("Projetos", vm.MarcaDoFiltro);
            Assert.Same(claude, vm.Selecionado);
            Assert.False(vm.MostrarSessao);

            // e nas Sessões de novo, sem esperar a leitura: a primeira favorita, a mais nova
            // delas, e não a última conversa escolhida
            vm.VerSessoesCommand.Execute(null);
            Assert.Same(Linha(boleto), vm.SessaoAVista);
            Assert.Same(Linha(boleto), vm.LinhaSelecionada);
            vm.LinhaSelecionada = Linha(script);
            vm.VerProjetosCommand.Execute(null);
            vm.VerSessoesCommand.Execute(null);
            Assert.Same(Linha(boleto), vm.SessaoAVista);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>Os favoritos são todos numerados, como o [n] do cia, também depois do nono.</summary>
    [AvaloniaFact]
    public void FavoritosNumeradosDepoisDoNono()
    {
        Montar();
        for (var i = 1; i <= 10; i++) Directory.CreateDirectory(Path.Combine(Particular, "Extra" + i));
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            foreach (var p in vm.Projetos.Where(p => p.Nome.StartsWith("Extra")).ToList()) p.FavoritarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Enumerable.Range(1, 11).Select(n => n.ToString()),
                vm.Linhas.OfType<LinhaDoProjeto>().Where(l => l.NosFavoritos).Select(l => l.Extra));
        }
        finally
        {
            vm.EncerrarTudo();
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
    /// A cor é do projeto, a da aba: sem escolha, tirada do nome como no GTest; escolhida na
    /// paleta da aba, vale na hora e fica gravada. A tela das preferências abre com as contas.
    /// </summary>
    [AvaloniaFact]
    public void CorDaAbaVemDoNomeOuDaPaletaEFicaGravada()
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
        vm.MudarCor(notas, "#db4c9b");
        Assert.Equal("#DB4C9B", notas.CorHex);
        vm.MudarCor(notas, "não é cor"); // ignorado
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
            Button Botao(string texto) => barra.GetVisualDescendants().OfType<Button>()
                .Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == texto));
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
            var paleta = (PaletaDeCores)((Flyout)cor.Flyout).Content!;
            Assert.Contains("#DB4C9B", PaletaDeCores.Cores);
            Assert.Equal(PaletaDeCores.Cores.Count, paleta.GetVisualDescendants().OfType<Border>().Count(b => b.Child is Border));
            var corDoFinanceiro = financeiro.CorHex;
            paleta.Escolher("#DB4C9B");
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

        public GrupoEscolhido? GrupoNovo;
        public IReadOnlyList<GrupoNaArvore>? PaisOferecidos;
        public string? CorSugerida;

        public Task<GrupoEscolhido?> GrupoAsync(string titulo, string nome, string cor, string? paiId,
            IReadOnlyList<GrupoNaArvore>? pais)
        {
            PaisOferecidos = pais;
            CorSugerida = cor;
            return Task.FromResult(GrupoNovo);
        }
    }

    // ------------------------------------------------------------ organizar a árvore

    private static GrupoDaArvore Titulo(MainViewModel vm, string nome) =>
        vm.Linhas.OfType<GrupoDaArvore>().Single(g => g.Titulo == nome);

    private static string[] Rotulos(MenuFlyout menu) =>
        menu.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToArray();

    /// <summary>
    /// Grupos e subgrupos dos projetos: os do usuário ficam acima das bases, o subgrupo recua
    /// e aparece antes dos projetos do pai, e a contagem é de tudo abaixo. O projeto sai da
    /// base, continua nos Favoritos e volta ao lugar quando o grupo some. Tudo fica gravado.
    /// </summary>
    [AvaloniaFact]
    public void GruposESubgruposDosProjetos()
    {
        Montar();
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos);
        Titulo(vm, "DBISAM").AlternarCommand.Execute(null);
        Titulo(vm, "Private").AlternarCommand.Execute(null);

        var fiscal = vm.CriarGrupo("Fiscal", "#E07B00");
        var nfe = vm.CriarGrupo("NFe", null, fiscal);
        Assert.Equal(new[] { "Favoritos", "Recentes", "Fiscal", "NFe", "DBISAM", "Private" }, Titulos(vm));
        Assert.Equal(0, Titulo(vm, "Fiscal").Nivel);
        Assert.Equal(1, Titulo(vm, "NFe").Nivel);

        var financeiro = Projeto(vm, "Financeiro");
        var notas = Projeto(vm, "Notas");
        vm.MoverProjeto(notas, nfe);
        vm.MoverProjeto(financeiro, fiscal);
        Assert.Equal(new[] { "Notas" }, Nomes(vm, nfe));
        Assert.Equal(new[] { "Financeiro" }, Nomes(vm, fiscal));
        Assert.Empty(Nomes(vm, "base:" + Bmsoft));
        Assert.Equal(new[] { "Financeiro" }, Nomes(vm, MainViewModel.GrupoFavoritos)); // favorito continua favorito
        Assert.Equal(2, Titulo(vm, "Fiscal").Contagem);
        Assert.Equal(1, Titulo(vm, "NFe").Contagem);
        Assert.Equal(0, Titulo(vm, "DBISAM").Contagem);
        Assert.True(vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Grupo == nfe).Recuo.Left >
                    vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Grupo == fiscal).Recuo.Left);

        // subgrupo na base: DBISAM recebe grupo como um grupo qualquer, e o nome não muda
        var ferramentas = vm.CriarGrupo("Ferramentas", null, "base:" + Bmsoft);
        Assert.Equal(new[] { "Favoritos", "Recentes", "Fiscal", "NFe", "DBISAM", "Ferramentas", "Private" }, Titulos(vm));
        Assert.Equal(1, Titulo(vm, "Ferramentas").Nivel);
        vm.AtualizarGrupo("base:" + Bmsoft, "Outro nome", "#DB4C9B", null);
        Assert.Equal("#DB4C9B", ((Avalonia.Media.ISolidColorBrush)Titulo(vm, "DBISAM").Cor).Color.ToString().ToUpperInvariant().Replace("#FF", "#"));

        // recolher o grupo esconde o que está abaixo, e a escolha fica gravada
        Titulo(vm, "Fiscal").AlternarCommand.Execute(null);
        Assert.Equal(new[] { "Favoritos", "Recentes", "Fiscal", "DBISAM", "Ferramentas", "Private" }, Titulos(vm));
        Assert.Equal(2, Titulo(vm, "Fiscal").Contagem);

        // chegar num grupo recolhido abre ele
        vm.MoverProjeto(Projeto(vm, "GTerm"), fiscal);
        Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(vm, fiscal));

        // abrindo de novo: o workspace guardou tudo
        var outra = new MainViewModel(new Dialogos());
        Assert.Contains("Ferramentas", Titulos(outra));
        Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(outra, fiscal));
        Assert.Equal(new[] { "Notas" }, Nomes(outra, nfe));

        // um grupo não vai para dentro de um descendente
        var ws = WorkspaceStore.Load();
        Assert.False(GrupoArvore.PodeFicarDentro(ws.Grupos, fiscal, nfe));
        vm.AtualizarGrupo(fiscal, "Fiscal", "#E07B00", nfe);
        Assert.Null(WorkspaceStore.Load().Grupos.Single(g => g.Id == fiscal).PaiId);

        // excluir sobe um nível: o subgrupo e os projetos vão para o pai
        vm.RemoverGrupo(nfe);
        Assert.Equal(new[] { "Financeiro", "GTerm", "Notas" }, Nomes(vm, fiscal));

        // e sem pai voltam para a base
        vm.RemoverGrupo(fiscal);
        Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, "base:" + Bmsoft));
        Assert.Equal(new[] { "GTerm" }, Nomes(vm, "base:" + Particular));
        Assert.DoesNotContain("Fiscal", Titulos(vm));
    }

    /// <summary>O grupo novo nasce numa cor livre; o subgrupo, na cor do pai, e o diálogo oferece os pais.</summary>
    [AvaloniaFact]
    public async Task GrupoNovoEscolheACorPeloDialogo()
    {
        Montar();
        var dialogos = new Dialogos { GrupoNovo = new GrupoEscolhido("Fiscal", "#D93F3F", null) };
        var vm = new MainViewModel(dialogos);

        var fiscal = await vm.NovoGrupoAsync(null);
        Assert.NotNull(fiscal);
        Assert.Equal("#1F9D55", dialogos.CorSugerida); // azul e roxo sÃ£o das bases
        Assert.Contains(dialogos.PaisOferecidos!, g => g.Grupo.Nome == "DBISAM");

        dialogos.GrupoNovo = new GrupoEscolhido("NFe", "#D93F3F", fiscal);
        await vm.NovoGrupoAsync(fiscal);
        Assert.Equal("#D93F3F", dialogos.CorSugerida);
        Assert.Contains("NFe", Titulos(vm));

        dialogos.GrupoNovo = null;
        Assert.Null(await vm.NovoGrupoAsync(null)); // cancelou

        // o diálogo de verdade abre com os pais e a paleta
        var todos = WorkspaceStore.Load().Grupos;
        var tela = new GrupoWindow("Editar grupo", "Fiscal", "#D93F3F", null, GrupoArvore.EmOrdem(todos));
        tela.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(tela.GetVisualDescendants().OfType<SeletorDeCor>(), s => s.Cor == "#D93F3F");
        Assert.Contains(tela.GetVisualDescendants().OfType<ComboBox>(), c => c.ItemCount == todos.Count + 1);
        tela.Close();
    }

    /// <summary>
    /// Arrastar com o mouse de verdade: para o título de um grupo, para ao lado de outro
    /// projeto, para Ocultos e de volta. Soltar no mesmo lugar não muda nada, e o traço mostra
    /// onde cai enquanto o botão está apertado. O título do grupo arrasta sem recolher.
    /// </summary>
    [AvaloniaFact]
    public void ArrastarProjetosEGruposNaArvore()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            Titulo(vm, "DBISAM").AlternarCommand.Execute(null);
            Titulo(vm, "Private").AlternarCommand.Execute(null);
            var fiscal = vm.CriarGrupo("Fiscal", "#E07B00");
            var soltos = vm.CriarGrupo("Soltos", "#1F9D55");
            Dispatcher.UIThread.RunJobs();

            Control Item(Func<LinhaDaArvore, bool> qual) => janela.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(i => i.DataContext is LinhaDaArvore l && qual(l));
            Point Meio(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), janela)!.Value;
            Point Cima(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 4), janela)!.Value;
            Control Linha(string grupo, string nome) =>
                Item(l => l is LinhaDoProjeto p && p.Grupo == grupo && p.Projeto.Nome == nome);
            Control Grupo(string nome) => Item(l => l is GrupoDaArvore g && g.Titulo == nome);

            void Arrastar(Point de, Point para, Action? noCaminho = null)
            {
                janela.MouseDown(de, MouseButton.Left);
                janela.MouseMove(new Point(de.X, de.Y + (para.Y > de.Y ? 8 : -8)), RawInputModifiers.LeftMouseButton);
                janela.MouseMove(para, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                noCaminho?.Invoke();
                janela.MouseUp(para, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }

            // para o título: o grupo acende enquanto o botão está apertado
            Arrastar(Meio(Linha("base:" + Bmsoft, "Notas")), Meio(Grupo("Fiscal")),
                () => Assert.True(Titulo(vm, "Fiscal").AlvoDeSoltar));
            Assert.False(Titulo(vm, "Fiscal").AlvoDeSoltar);
            Assert.Equal(new[] { "Notas" }, Nomes(vm, fiscal));
            Assert.Equal(new[] { "Financeiro" }, Nomes(vm, "base:" + Bmsoft));

            // ao lado de outro projeto: em cima dele, com o traço naquela linha
            Arrastar(Meio(Linha("base:" + Bmsoft, "Financeiro")), Cima(Linha(fiscal, "Notas")), () =>
            {
                var alvo = vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Grupo == fiscal);
                Assert.True(alvo.SoltarAntes);
                Assert.False(alvo.SoltarDepois);
            });
            Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, fiscal));
            Assert.All(vm.Linhas.OfType<LinhaDoProjeto>(), l => Assert.False(l.SoltarAntes || l.SoltarDepois));

            // dentro do grupo muda a ordem, e ela fica gravada
            Arrastar(Meio(Linha(fiscal, "Notas")), Cima(Linha(fiscal, "Financeiro")));
            Assert.Equal(new[] { "Notas", "Financeiro" }, Nomes(vm, fiscal));
            Assert.Equal(new[] { Notas, Financeiro }, WorkspaceStore.Load().Grupos.Single(g => g.Id == fiscal).Itens);

            // a base não aceita projeto de outra base; a dele, sim
            Arrastar(Meio(Linha("base:" + Particular, "GTerm")), Meio(Grupo("DBISAM")));
            Assert.Equal(new[] { "GTerm" }, Nomes(vm, "base:" + Particular));
            Arrastar(Meio(Linha(fiscal, "Notas")), Meio(Grupo("DBISAM")));
            Assert.Equal(new[] { "Notas" }, Nomes(vm, "base:" + Bmsoft));

            // dentro da base a ordem também muda: fica gravada, e o que chegar depois vai para o fim
            vm.MoverProjeto(Projeto(vm, "Financeiro"), "base:" + Bmsoft);
            Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, "base:" + Bmsoft));
            Dispatcher.UIThread.RunJobs();
            Arrastar(Meio(Linha("base:" + Bmsoft, "Notas")), Cima(Linha("base:" + Bmsoft, "Financeiro")));
            Assert.Equal(new[] { "Notas", "Financeiro" }, Nomes(vm, "base:" + Bmsoft));
            Assert.Equal(new[] { "Notas", "Financeiro" }, Nomes(new MainViewModel(new Dialogos()), "base:" + Bmsoft));

            // nos Favoritos: da base para o título ele vira favorito e fica na base também
            Arrastar(Meio(Linha("base:" + Particular, "GTerm")), Meio(Grupo("Favoritos")));
            Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(vm, MainViewModel.GrupoFavoritos));
            Assert.Equal(new[] { "GTerm" }, Nomes(vm, "base:" + Particular));
            Assert.Equal(new[] { Financeiro, PastaDoGTerm }, Cia.LerEstado(Estado)!.Favoritos);

            // de dentro dos Favoritos para um grupo: vai para o grupo e a ordem deles não muda
            Arrastar(Meio(Linha(MainViewModel.GrupoFavoritos, "Financeiro")), Meio(Grupo("Soltos")));
            Assert.Equal(new[] { "Financeiro" }, Nomes(vm, soltos));
            Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(vm, MainViewModel.GrupoFavoritos));

            // o título do grupo arrasta: vira subgrupo do outro, sem recolher nem abrir nada
            var aberto = Titulo(vm, "Soltos").Recolhido;
            Arrastar(Meio(Grupo("Soltos")), Meio(Grupo("Fiscal")));
            Assert.Equal(fiscal, WorkspaceStore.Load().Grupos.Single(g => g.Id == soltos).PaiId);
            Assert.Equal(1, Titulo(vm, "Soltos").Nivel);
            Assert.Equal(aberto, Titulo(vm, "Soltos").Recolhido);

            // o título da base não arrasta
            Arrastar(Meio(Grupo("Private")), Meio(Grupo("Fiscal")));
            Assert.Equal(0, Titulo(vm, "Private").Nivel);

            // clicar continua sendo clique: o título recolhe, a linha escolhe
            var titulo = Meio(Grupo("Fiscal"));
            janela.MouseDown(titulo, MouseButton.Left);
            janela.MouseUp(titulo, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Titulo(vm, "Fiscal").Recolhido);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// Retomar é para digitar: com o clique de verdade no botão do projeto parado, que some, e
    /// com o Enter na árvore, o teclado acaba no terminal do Claude. Digitar chega ao processo.
    /// </summary>
    [AvaloniaFact]
    public async Task RetomarLevaOTecladoAoTerminal()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var financeiro = vm.Selecionado!;

            var botao = janela.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("primary") && b.IsEffectivelyVisible);
            var meio = botao.TranslatePoint(new Point(botao.Bounds.Width / 2, botao.Bounds.Height / 2), janela)!.Value;
            janela.MouseDown(meio, MouseButton.Left);
            janela.MouseUp(meio, MouseButton.Left);
            Assert.True(await Esperar(() => financeiro.Sessao is { Pid: not null }));
            Dispatcher.UIThread.RunJobs();

            var foco = Assert.IsType<TerminalControl>(janela.FocusManager!.GetFocusedElement());
            Assert.True(foco.IsVisible);
            Assert.Same(financeiro.Sessao!.Modelo, foco.Model);

            // o que se digita vai direto para o Claude, sem clicar no terminal antes
            Assert.True(await Esperar(() => Tela(financeiro.Sessao!).Contains(">")), Tela(financeiro.Sessao!));
            janela.KeyTextInput("echo digitado");
            janela.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(await Esperar(() => Tela(financeiro.Sessao!).Contains("echo digitado")), Tela(financeiro.Sessao!));

            // o mesmo com o Enter na árvore, em outro projeto
            var notas = Projeto(vm, "Notas");
            vm.Ver(notas);
            Dispatcher.UIThread.RunJobs();
            await notas.IniciarCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(notas.Sessao!.Modelo, Assert.IsType<TerminalControl>(janela.FocusManager!.GetFocusedElement()).Model);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>Os arquivos de um /code-review, como o Claude grava: a aba é a do processo informado.</summary>
    private string MontarRevisao(int pid, string pasta, string sessao, string agente, string skill = "code-review")
    {
        var sessoes = Path.Combine(ContaDoClaude, "sessions");
        Directory.CreateDirectory(sessoes);
        File.WriteAllText(Path.Combine(sessoes, pid + ".json"), $$"""{ "pid": {{pid}}, "sessionId": "{{sessao}}", "cwd": {{Json(pasta)}} }""");

        var agentes = Path.Combine(ContaDoClaude, "projects", SessoesDoClaude.Chave(pasta), sessao, "subagents");
        Directory.CreateDirectory(agentes);
        File.WriteAllText(Path.Combine(agentes, $"agent-{agente}.forked-skill.json"), $$"""{ "skillName": "{{skill}}" }""");
        File.WriteAllText(Path.Combine(agentes, $"agent-{agente}.meta.json"), """{ "agentType": "general-purpose" }""");
        File.WriteAllText(Path.Combine(agentes, $"agent-{agente}.jsonl"), "");
        return agentes;
    }

    private static string Passo(int ferramentas) => JsonLinha("assistant", ferramentas);

    private static string JsonLinha(string tipo, int ferramentas)
    {
        var itens = string.Join(",", Enumerable.Repeat("""{"type":"tool_use","name":"Read"}""", ferramentas));
        return "{\"type\":\"" + tipo + "\",\"message\":{\"content\":[" + itens + "]}}" + Environment.NewLine;
    }

    /// <summary>
    /// A revisão pelos arquivos do Claude: rodando enquanto o agente escreve, concluída com o
    /// finished, parada com o aviso de killed ou com o silêncio longo. Os passos contam só as
    /// respostas do agente, sem a linha pela metade e sem os pedidos à API, que repetem a
    /// conversa. Agente de outra skill não conta, e a terminada há muito tempo sai da barra.
    /// </summary>
    [Fact]
    public void RevisaoLidaDosArquivosDoClaude()
    {
        const string Sessao = "11111111-2222-3333-4444-555555555555";
        var leitor = new RevisoesDoClaude();
        Revisao? Ler(DateTime? quando = null) => leitor.Ultima(ContaDoClaude, 4242, Notas, quando ?? DateTime.Now);

        Assert.Null(Ler()); // aba sem conversa conhecida

        // agente de outra skill não é revisão
        var agentes = MontarRevisao(4242, Notas, Sessao, "a0outro", "simplify");
        Assert.Null(Ler());

        File.WriteAllText(Path.Combine(agentes, "agent-a0outro.forked-skill.json"), """{ "skillName": "code-review" }""");
        var diario = Path.Combine(agentes, "agent-a0outro.jsonl");
        Assert.Equal(EstadoDaRevisao.Rodando, Ler()!.Estado);
        Assert.Equal(0, Ler()!.Passos);

        // só as respostas contam, e a linha ainda sendo gravada espera a próxima leitura
        File.AppendAllText(diario, Passo(2) + JsonLinha("api-request-blob", 9) + JsonLinha("user", 3));
        Assert.Equal(2, Ler()!.Passos);
        File.AppendAllText(diario, Passo(1).TrimEnd('\n'));
        Assert.Equal(2, Ler()!.Passos);
        File.AppendAllText(diario, "\n" + Passo(1));
        Assert.Equal(4, Ler()!.Passos);
        Assert.Equal(4, Ler()!.Passos); // relida sem novidade, não conta de novo

        // o aviso de que foi parada, no fim da conversa principal
        var conversa = Path.Combine(ContaDoClaude, "projects", SessoesDoClaude.Chave(Notas), Sessao + ".jsonl");
        File.WriteAllText(conversa, """{"content":"<output-file>tasks\\a0outro.output</output-file>\n<status>killed</status>"}""" + "\n");
        Assert.Equal(EstadoDaRevisao.Parada, Ler()!.Estado);

        // o aviso de outro agente, ou o de que terminou bem, não para esta
        File.WriteAllText(conversa, """{"content":"<output-file>tasks\\a0outro.output</output-file>\n<status>completed</status>"}""" + "\n");
        Assert.Equal(EstadoDaRevisao.Rodando, Ler()!.Estado);

        // calado por muito tempo, sem terminar: o agente morreu com o processo
        Assert.Equal(EstadoDaRevisao.Parada, Ler(DateTime.Now + RevisoesDoClaude.Silencio + TimeSpan.FromMinutes(1))!.Estado);

        // terminou: a duração é até a última escrita, e não cresce mais
        File.WriteAllText(Path.Combine(agentes, "agent-a0outro.meta.json"), """{ "finished": true }""");
        var fim = File.GetLastWriteTime(diario);
        File.SetLastWriteTime(Path.Combine(agentes, "agent-a0outro.forked-skill.json"), fim.AddMinutes(-8));
        var pronta = Ler(fim.AddMinutes(5))!;
        Assert.Equal(EstadoDaRevisao.Concluida, pronta.Estado);
        Assert.Equal(TimeSpan.FromMinutes(8), pronta.Duracao);
        Assert.Equal("8 min", RevisoesDoClaude.Tempo(pronta.Duracao));
        Assert.Equal("40 s", RevisoesDoClaude.Tempo(TimeSpan.FromSeconds(40)));

        // passado o prazo, some da barra
        Assert.Null(Ler(fim + RevisoesDoClaude.Validade + TimeSpan.FromMinutes(1)));

        // a mais nova da conversa é a que vale
        MontarRevisao(4242, Notas, Sessao, "a1nova");
        File.SetLastWriteTime(Path.Combine(agentes, "agent-a1nova.forked-skill.json"), DateTime.Now.AddMinutes(1));
        Assert.Equal("a1nova", Ler()!.Id);

        // arquivo pela metade na hora da leitura: fica o que já se sabia
        File.WriteAllText(Path.Combine(ContaDoClaude, "sessions", "4242.json"), "{ quebrado");
        Assert.Equal("a1nova", Ler()!.Id);
    }

    /// <summary>
    /// O botão e a barra na aba: o botão manda o comando, e a barra aparece pelo que o Claude
    /// grava, também se o comando foi digitado direto. Rodando, ela anda sem percentual e o
    /// texto do botão é o status, legível mesmo sem aceitar clique; no fim fica cheia e verde.
    /// </summary>
    [AvaloniaFact]
    public async Task CodeReviewNaBarraDaAba()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var financeiro = vm.Selecionado!;
            await financeiro.IniciarCommand.ExecuteAsync(null);
            Assert.True(await Esperar(() => financeiro.Sessao is { Pid: not null }));
            var sessao = financeiro.Sessao!;
            Assert.True(await Esperar(() => Tela(sessao).Contains(">")), Tela(sessao));

            var botao = janela.FindControl<Button>("CodeReview")!;
            var barra = janela.FindControl<ProgressBar>("BarraDaRevisao")!;
            var andando = janela.FindControl<ProgressBar>("BarraDaRevisaoAndando")!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(botao.IsEffectivelyEnabled); // o Claude ainda não avisou que subiu
            Assert.False(barra.IsVisible || andando.IsVisible);
            Assert.Equal("Code review", financeiro.RotuloDaRevisao);

            sessao.Modelo.Send("echo pronto a> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => financeiro.RevisarCommand.CanExecute(null)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(botao.IsEffectivelyEnabled);
            Assert.Equal(24, botao.Bounds.Height);
            Assert.Equal(EstadoDoTerminal.Ocioso, sessao.Estado);

            // o clique manda o comando
            await financeiro.RevisarCommand.ExecuteAsync(null);
            Assert.True(await Esperar(() => Tela(sessao).Contains("/code-review")), Tela(sessao));

            // o Claude começou a revisão em segundo plano
            sessao.Modelo.Send("echo concluido b> \"%GTERM_ESTADO%\"\r"); // a conversa principal fica livre
            Assert.True(await Esperar(() => financeiro.RevisarCommand.CanExecute(null)));
            var agentes = MontarRevisao(sessao.Pid!.Value, Financeiro, "22222222-3333-4444-5555-666666666666", "a9revisao");
            File.AppendAllText(Path.Combine(agentes, "agent-a9revisao.jsonl"), Passo(3));
            await vm.LerRevisoesAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.True(financeiro.RevisaoRodando);
            Assert.True(andando.IsVisible);
            Assert.True(andando.IsIndeterminate);
            Assert.False(barra.IsVisible);
            Assert.False(botao.IsEffectivelyEnabled); // uma de cada vez
            Assert.False(financeiro.RevisarCommand.CanExecute(null));
            Assert.StartsWith("Revisando · ", financeiro.RotuloDaRevisao);
            Assert.Contains("3 passos", financeiro.DicaDaRevisao);
            Assert.Equal(1, botao.Opacity); // desabilitado, mas é onde está o status
            Assert.Contains("revisando", botao.Classes);
            Assert.Contains(botao.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Revisando · ") == true);

            // na largura da barra da aba, sem mexer na altura dela nem no terminal embaixo
            var faixa = janela.FindControl<Border>("BarraDaAba")!;
            Assert.True(andando.Bounds.Width > 500, "largura " + andando.Bounds.Width);
            Assert.Equal(janela.FindControl<TextBox>("CaixaDoFiltro")!.Bounds.Height, faixa.Bounds.Height);
            Assert.Equal(4, andando.Bounds.Height);
            Foto(janela, "revisao-rodando.png");

            // o Compactar não depende da revisão: a conversa principal segue livre
            Assert.True(financeiro.CompactarCommand.CanExecute(null));

            // terminou: cheia, verde, e o botão volta para revisar de novo
            File.WriteAllText(Path.Combine(agentes, "agent-a9revisao.meta.json"), """{ "finished": true }""");
            await vm.LerRevisoesAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(financeiro.RevisaoConcluida);
            Assert.False(andando.IsVisible);
            Assert.True(barra.IsVisible);
            var cheia = barra.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            Assert.Equal(barra.Bounds.Width, cheia.Bounds.Width, 0);
            Assert.Equal(100, barra.Value);
            Assert.Contains("concluida", barra.Classes);
            Assert.StartsWith("Revisado · ", financeiro.RotuloDaRevisao);
            Assert.Contains(botao.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Revisado · ") == true);
            Assert.True(financeiro.RevisarCommand.CanExecute(null));
            Assert.True(botao.IsEffectivelyEnabled);
            Foto(janela, "revisao-concluida.png");

            // interrompida: fica vermelha
            File.WriteAllText(Path.Combine(agentes, "agent-a9revisao.meta.json"), "{ }");
            File.WriteAllText(Path.Combine(ContaDoClaude, "projects", SessoesDoClaude.Chave(Financeiro), "22222222-3333-4444-5555-666666666666.jsonl"),
                """{"content":"a9revisao.output</output-file>\n<status>killed</status>"}""" + "\n");
            await vm.LerRevisoesAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(financeiro.RevisaoParada);
            Assert.Contains("parada", barra.Classes);
            Assert.Equal("Revisão parada", financeiro.RotuloDaRevisao);

            // outra aba tem a própria revisão: trocar de aba troca a barra
            var notas = Projeto(vm, "Notas");
            await notas.IniciarCommand.ExecuteAsync(null);
            Assert.True(await Esperar(() => notas.Sessao is { Pid: not null }));
            await vm.LerRevisoesAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(barra.IsVisible || andando.IsVisible);
            financeiro.MostrarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(barra.IsVisible);

            // encerrado o Claude, a revisão sai junto
            vm.PrazoDaSaida = TimeSpan.FromMilliseconds(100);
            sessao.Modelo.Send("exit\r");
            Assert.True(await Esperar(() => sessao.Encerrada));
            await financeiro.EncerrarCommand.ExecuteAsync(null);
            Assert.Null(financeiro.Revisao);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// O botão Compactar só vale com o Claude parado, o que só os hooks dizem: esmaecido na
    /// subida, enquanto ele trabalha e até o aviso do fim da compactação. O que estava escrito
    /// na entrada sai antes do comando, e o clique não interrompe nada que não devia.
    /// </summary>
    [AvaloniaFact]
    public async Task CompactarSoComOClaudeParado()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var financeiro = vm.Selecionado!;
            await financeiro.IniciarCommand.ExecuteAsync(null);
            Assert.True(await Esperar(() => financeiro.Sessao is { Pid: not null }));
            var sessao = financeiro.Sessao!;
            Assert.True(await Esperar(() => Tela(sessao).Contains(">")), Tela(sessao));

            var botao = janela.GetVisualDescendants().OfType<Button>()
                .Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, financeiro.CompactarCommand));
            Assert.Equal(24, botao.Bounds.Height);

            // sem o aviso de que o Claude subiu, nada: o Ctrl+C no meio da subida o derrubaria
            Dispatcher.UIThread.RunJobs();
            Assert.False(botao.IsEffectivelyEnabled);
            Foto(janela, "compactar-esperando.png");

            // parado: o que o hook faria, gravado pelo próprio shell
            sessao.Modelo.Send("echo pronto a> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => financeiro.CompactarCommand.CanExecute(null)), "ficou em " + sessao.Estado);
            Dispatcher.UIThread.RunJobs();
            Assert.True(botao.IsEffectivelyEnabled);
            Foto(janela, "compactar.png");

            // trabalhando, e parado de novo no fim da resposta
            sessao.Modelo.Send("echo rodando b> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => !financeiro.CompactarCommand.CanExecute(null)));
            sessao.Modelo.Send("echo concluido c> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => financeiro.CompactarCommand.CanExecute(null)));

            // perguntando: o Ctrl+C responderia à pergunta
            sessao.Modelo.Send("echo aguardando d> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => !financeiro.CompactarCommand.CanExecute(null)));
            sessao.Modelo.Send("echo concluido e> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => financeiro.CompactarCommand.CanExecute(null)));

            // o clique: o rascunho sai, o comando entra com Enter, e o botão espera o aviso
            sessao.Modelo.Send("rascunho");
            await Task.Delay(100);
            var clique = financeiro.CompactarCommand.ExecuteAsync(null);
            Assert.False(financeiro.Sessao!.EsperaComando);
            Assert.False(financeiro.CompactarCommand.CanExecute(null));
            await clique;
            Assert.True(await Esperar(() => Tela(sessao).Contains("/compact")), Tela(sessao));
            Assert.DoesNotContain("rascunho/compact", Tela(sessao));

            await Task.Delay(1200); // mais de um tique: sem o aviso, continua esperando
            Assert.False(financeiro.CompactarCommand.CanExecute(null));
            Assert.Equal(EstadoDoTerminal.Rodando, sessao.Estado);

            // o SessionStart do fim da compactação libera
            sessao.Modelo.Send("echo pronto f> \"%GTERM_ESTADO%\"\r");
            Assert.True(await Esperar(() => financeiro.CompactarCommand.CanExecute(null)));
            Assert.Equal(EstadoDoTerminal.Ocioso, sessao.Estado);

            // encerrado, não há o que compactar
            sessao.Modelo.Send("exit\r");
            Assert.True(await Esperar(() => sessao.Encerrada));
            Assert.False(financeiro.CompactarCommand.CanExecute(null));
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// Encerrar a aba da frente traz a que ficou no lugar dela, com o teclado no terminal, sem
    /// precisar clicar em nada; a última dá lugar à anterior. Encerrar uma aba de trás não tira
    /// ninguém da frente, e sem outra aba fica o quadro do projeto.
    /// </summary>
    [AvaloniaFact]
    public async Task EncerrarAAbaDaFrenteTrazAProxima()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var financeiro = Projeto(vm, "Financeiro");
            var notas = Projeto(vm, "Notas");
            var gterm = Projeto(vm, "GTerm");

            async Task Abrir(ProjetoViewModel p)
            {
                await p.IniciarCommand.ExecuteAsync(null);
                Assert.True(await Esperar(() => p.Sessao is { Pid: not null }));
            }

            // o Claude já tinha saído: sem a pergunta de confirmação
            async Task Encerrar(ProjetoViewModel p)
            {
                p.Sessao!.Modelo.Send("exit\r");
                Assert.True(await Esperar(() => p.Sessao!.Encerrada));
                await p.EncerrarCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
            }

            void NaFrente(ProjetoViewModel p)
            {
                Assert.Same(p, vm.Selecionado);
                Assert.Same(p, vm.AbaAtiva);
                Assert.True(p.AVista);
                Assert.False(vm.MostrarParado);
                var foco = Assert.IsType<TerminalControl>(janela.FocusManager!.GetFocusedElement());
                Assert.Same(p.Sessao!.Modelo, foco.Model);
            }

            await Abrir(financeiro);
            await Abrir(notas);
            await Abrir(gterm);
            Assert.Equal(new[] { financeiro, notas, gterm }, vm.Abertos);

            // a do meio: vem a que estava à direita, e ela fica no lugar da que saiu
            vm.Ver(notas);
            Dispatcher.UIThread.RunJobs();
            await Encerrar(notas);
            Assert.Equal(new[] { financeiro, gterm }, vm.Abertos);
            NaFrente(gterm);

            // a última da fila: vem a anterior
            await Encerrar(gterm);
            Assert.Equal(new[] { financeiro }, vm.Abertos);
            NaFrente(financeiro);

            // uma aba de trás encerrada não tira a da frente do lugar
            await Abrir(notas);
            NaFrente(notas);
            await Encerrar(financeiro);
            Assert.Equal(new[] { notas }, vm.Abertos);
            NaFrente(notas);

            // com uma conversa à vista nas Sessões, a tela continua nela
            await Abrir(gterm);
            ContaDeTeste.Conversa(ContaDoClaude, Notas, "Boleto", DateTime.Now.AddMinutes(-1));
            await vm.CarregarSessoesAsync();
            vm.VerSessoesCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.MostrarSessao);
            await Encerrar(gterm);
            Assert.True(vm.MostrarSessao);
            vm.VerProjetosCommand.Execute(null);
            vm.Ver(notas);
            Dispatcher.UIThread.RunJobs();

            // sem outra aba: o quadro do projeto que fechou, como sempre foi
            await Encerrar(notas);
            Assert.Empty(vm.Abertos);
            Assert.Same(notas, vm.Selecionado);
            Assert.True(vm.MostrarParado);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// O grupo Favoritos só existe com algum favorito à vista: a estrela do primeiro faz ele
    /// aparecer, aberto mesmo que tenha sido recolhido antes, e a do último faz ele sumir.
    /// </summary>
    [AvaloniaFact]
    public void FavoritosSoAparecemComAlgumFavorito()
    {
        Montar();
        var vm = new MainViewModel(new Dialogos());
        var financeiro = Projeto(vm, "Financeiro");
        var notas = Projeto(vm, "Notas");

        Titulo(vm, "Favoritos").AlternarCommand.Execute(null);
        Assert.True(Titulo(vm, "Favoritos").Recolhido);

        // tirar o último faz o grupo sumir, e o estado do cia fica sem favoritos
        vm.AlternarFavorito(financeiro);
        Assert.Equal(new[] { "Recentes", "DBISAM", "Private" }, Titulos(vm));
        Assert.Empty(Cia.LerEstado(Estado)!.Favoritos);

        // o primeiro traz o grupo de volta, aberto, com o favorito à vista
        vm.AlternarFavorito(notas);
        Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Private" }, Titulos(vm));
        Assert.False(Titulo(vm, "Favoritos").Recolhido);
        Assert.Equal(new[] { "Notas" }, Nomes(vm, MainViewModel.GrupoFavoritos));

        // com o único favorito oculto também não há o que mostrar, e o menu ainda lista o grupo
        vm.AlternarOculto(notas);
        Assert.DoesNotContain("Favoritos", Titulos(vm));
        Assert.Contains(vm.GruposAVista(), g => g.Id == MainViewModel.GrupoFavoritos);
        vm.AlternarOculto(notas);
        Assert.Contains("Favoritos", Titulos(vm));
    }

    /// <summary>
    /// Ocultar tira o projeto de todos os grupos (o favorito guarda o número do cia) e o manda
    /// para Ocultos, no fim e fechado. A pesquisa ainda acha, e voltar devolve ao lugar dele.
    /// </summary>
    [AvaloniaFact]
    public void OcultarProjetoDaArvore()
    {
        Montar();
        var vm = new MainViewModel(new Dialogos());
        var financeiro = Projeto(vm, "Financeiro");
        var notas = Projeto(vm, "Notas");
        var gterm = Projeto(vm, "GTerm");
        vm.AlternarFavorito(notas);
        Titulo(vm, "DBISAM").AlternarCommand.Execute(null);
        Titulo(vm, "Recentes").AlternarCommand.Execute(null);
        var fiscal = vm.CriarGrupo("Fiscal");
        vm.MoverProjeto(gterm, fiscal);

        vm.AlternarOculto(financeiro);
        vm.AlternarOculto(gterm);
        Assert.Equal(new[] { "Favoritos", "Recentes", "Fiscal", "DBISAM", "Private", "Ocultos" }, Titulos(vm));
        Assert.True(Titulo(vm, "Ocultos").Recolhido);
        Assert.Equal(2, Titulo(vm, "Ocultos").Contagem);
        Assert.Equal(0, Titulo(vm, "Fiscal").Contagem);
        Assert.DoesNotContain(vm.Linhas.OfType<LinhaDoProjeto>(), l => l.Projeto == financeiro || l.Projeto == gterm);

        // Notas continua sendo o [2] do cia: o oculto guarda o número
        Assert.Equal("2", vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Grupo == MainViewModel.GrupoFavoritos).Extra);

        // a pesquisa acha, porque procurar pelo nome é pedir para ver
        vm.Filtro = "fin";
        Assert.Equal(new[] { "Financeiro" }, Nomes(vm, MainViewModel.GrupoResultados));
        vm.Filtro = "";

        // abrindo o grupo Ocultos, e abrindo o app de novo, continuam lá
        Titulo(vm, "Ocultos").AlternarCommand.Execute(null);
        Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(vm, MainViewModel.GrupoOcultos));
        Assert.Equal(new[] { "Financeiro", "GTerm" }, Nomes(new MainViewModel(new Dialogos()), MainViewModel.GrupoOcultos));

        // mostrar devolve ao grupo em que ele estava; sem nenhum oculto o grupo some
        vm.AlternarOculto(gterm);
        Assert.Equal(new[] { "GTerm" }, Nomes(vm, fiscal));
        vm.MoverProjeto(financeiro, "base:" + Bmsoft); // arrastar para fora também mostra
        Assert.Equal(new[] { "Financeiro", "Notas" }, Nomes(vm, "base:" + Bmsoft));
        Assert.DoesNotContain("Ocultos", Titulos(vm));
    }

    /// <summary>
    /// O mesmo nas Sessões: grupos e subgrupos das conversas, separados dos grupos dos
    /// projetos. A conversa do grupo sai do dia, fica nas Favoritas também, e o limite do cl
    /// não vale para ela. Ocultas vão para o fim, e nenhuma vira a primeira ao entrar.
    /// </summary>
    [AvaloniaFact]
    public async Task GruposEOcultasNasSessoes()
    {
        Montar();
        var boleto = ContaDeTeste.Conversa(ContaDoClaude, Notas, "Boleto do Sicoob", DateTime.Now.AddMinutes(-30));
        var quadro = ContaDeTeste.Conversa(ContaDoClaude, PastaDoGTerm, "Quadro do cia", DateTime.Now.AddDays(-1));
        var nota = ContaDeTeste.Conversa(ContaDoClaude, Financeiro, "Nota de ajuste", DateTime.Now.AddDays(-2));
        File.WriteAllText(EstadoDoCl, $$"""{ "favoritas": [ "{{quadro}}" ] }""");

        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            vm.VerSessoesCommand.Execute(null);
            Assert.True(await Esperar(() => vm.SessoesLidas));
            Dispatcher.UIThread.RunJobs();

            LinhaDaSessao Linha(string grupo, string id) =>
                vm.Linhas.OfType<LinhaDaSessao>().Single(l => l.Grupo == grupo && l.Sessao.Id == id);
            LinhaDaSessao Qualquer(string id) => vm.Linhas.OfType<LinhaDaSessao>().First(l => l.Sessao.Id == id);
            string[] Ids(string grupo) => vm.Linhas.OfType<LinhaDaSessao>().Where(l => l.Grupo == grupo).Select(l => l.Sessao.Id).ToArray();

            // grupo vazio aparece, para receber conversas, e o subgrupo recua
            var banco = vm.CriarGrupo("Banco", "#1F9D55");
            var sicoob = vm.CriarGrupo("Sicoob", null, banco);
            Assert.Equal(new[] { "Favoritas", "Banco", "Sicoob", "Hoje", "Esta semana" }, Titulos(vm));
            Assert.Equal(1, Titulo(vm, "Sicoob").Nivel);

            // os grupos das conversas não aparecem nos projetos
            vm.VerProjetosCommand.Execute(null);
            Assert.DoesNotContain("Banco", Titulos(vm));
            vm.VerSessoesCommand.Execute(null);

            // a conversa vai para o grupo e sai do dia dela
            vm.MoverConversa(Qualquer(boleto), sicoob);
            Assert.Equal(new[] { boleto }, Ids(sicoob));
            Assert.DoesNotContain("Hoje", Titulos(vm));
            Assert.Equal(1, Titulo(vm, "Banco").Contagem);

            // a favorita fica nas Favoritas e no grupo, com uma linha em cada
            vm.MoverConversa(Qualquer(quadro), banco);
            Assert.Equal(new[] { quadro }, Ids(banco));
            Assert.Equal(new[] { quadro }, Ids("s:favoritas"));

            // ao lado de outra, pela regra do arraste
            vm.MoverConversa(Qualquer(nota), banco);
            var destino = vm.OndeCai(Qualquer(nota), Linha(banco, quadro), depois: false);
            Assert.NotNull(destino);
            vm.Soltar(Linha(banco, nota), destino!);
            Assert.Equal(new[] { nota, quadro }, Ids(banco));

            // soltar num dia é voltar para o dia dela; no mesmo lugar não há o que fazer
            Assert.Null(vm.OndeCai(Linha(banco, nota), Titulo(vm, "Banco"), depois: false));
            vm.MoverConversa(Linha(banco, nota), "s:Esta semana");
            Assert.Contains("Esta semana", Titulos(vm));
            Assert.Equal(new[] { quadro }, Ids(banco));

            // o menu da conversa: mover, tirar do grupo e ocultar
            var menu = janela.MenuDe(Linha(sicoob, boleto));
            Assert.Contains("Mover para", Rotulos(menu));
            Assert.Contains("Tirar do grupo", Rotulos(menu));
            Assert.Contains("Ocultar da árvore", Rotulos(menu));
            var mover = menu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Mover para");
            Assert.Contains("Banco", mover.Items.OfType<MenuItem>().Select(m => m.Header as string));

            // com o mouse de verdade, para o título de outro grupo
            Dispatcher.UIThread.RunJobs();
            Point Meio(Func<LinhaDaArvore, bool> qual)
            {
                var c = janela.GetVisualDescendants().OfType<ListBoxItem>()
                    .Single(i => i.DataContext is LinhaDaArvore l && qual(l));
                return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), janela)!.Value;
            }
            var de = Meio(l => ReferenceEquals(l, Linha(sicoob, boleto)));
            var para = Meio(l => ReferenceEquals(l, Titulo(vm, "Banco")));
            janela.MouseDown(de, MouseButton.Left);
            janela.MouseMove(new Point(de.X, de.Y - 8), RawInputModifiers.LeftMouseButton);
            janela.MouseMove(para, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Titulo(vm, "Banco").AlvoDeSoltar);
            janela.MouseUp(para, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { quadro, boleto }, Ids(banco));
            vm.MoverConversa(Linha(banco, boleto), sicoob);

            // oculta vai para o fim, fechada, e deixa de ser a primeira
            vm.AlternarOculta(Linha("s:favoritas", quadro));
            Assert.Equal("Ocultas", Titulos(vm)[^1]);
            Assert.True(Titulo(vm, "Ocultas").Recolhido);
            Assert.DoesNotContain("Favoritas", Titulos(vm));
            vm.VerProjetosCommand.Execute(null);
            vm.VerSessoesCommand.Execute(null);
            Assert.Equal(boleto, vm.SessaoAVista!.Sessao.Id);
            Assert.Same(vm.SessaoAVista, vm.LinhaSelecionada);

            // a pesquisa abre os grupos e acha as ocultas
            vm.Filtro = "quadro";
            Assert.Equal(new[] { quadro }, vm.Linhas.OfType<LinhaDaSessao>().Select(l => l.Sessao.Id));
            vm.Filtro = "";

            // tudo gravado, e a escolha continua de pé depois de reabrir
            var ws = WorkspaceStore.Load();
            Assert.Equal(new[] { quadro }, ws.SessoesOcultas);
            Assert.Equal(new[] { boleto }, ws.GruposDasSessoes.Single(g => g.Id == sicoob).Itens);
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>O menu da linha é montado na hora, com os grupos e o que vale para ela.</summary>
    [AvaloniaFact]
    public void MenuDaArvoreMontadoNaHora()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var fiscal = vm.CriarGrupo("Fiscal");
            Titulo(vm, "DBISAM").AlternarCommand.Execute(null);

            // nenhuma linha carrega menu pronto: o "Mover para" muda com os grupos
            Assert.DoesNotContain(janela.GetVisualDescendants().OfType<Border>(),
                b => b.Classes.Contains("linha") && b.ContextMenu is not null);

            var linha = vm.Linhas.OfType<LinhaDoProjeto>().First(l => l.Grupo == MainViewModel.GrupoFavoritos);
            var menu = Rotulos(janela.MenuDe(linha));
            Assert.Contains("Pedido do Mantis…", menu);
            Assert.Contains("Mover para cima", menu);
            Assert.Contains("Tirar dos favoritos", menu);
            Assert.Contains("Ocultar da árvore", menu);
            Assert.DoesNotContain("Encerrar o Claude", menu);

            // o projeto da Private não tem pedido, e a base dos outros não é destino
            var gterm = Projeto(vm, "GTerm");
            vm.MoverProjeto(gterm, fiscal);
            var noGrupo = vm.Linhas.OfType<LinhaDoProjeto>().Single(l => l.Projeto == gterm);
            var dele = janela.MenuDe(noGrupo);
            Assert.DoesNotContain("Pedido do Mantis…", Rotulos(dele));
            var destinos = vm.DestinosDe(noGrupo).Select(d => d.Grupo.Nome).ToArray();
            Assert.Equal(new[] { "Private" }, destinos); // Fiscal é onde ele já está

            // o título: o do usuário edita e exclui, a base só cor, o fixo só cria grupo. Todos
            // podem sair da árvore, e fora das linhas só dá para trazer um de volta
            Assert.Equal(new[] { "Novo subgrupo em Fiscal…", "Editar grupo… (nome, cor e onde fica)", "Excluir grupo…", "Ocultar este grupo", "Grupos à vista" },
                Rotulos(janela.MenuDe(Titulo(vm, "Fiscal"))));
            Assert.Equal(new[] { "Novo subgrupo em DBISAM…", "Cor…", "Ocultar este grupo", "Grupos à vista" }, Rotulos(janela.MenuDe(Titulo(vm, "DBISAM"))));
            Assert.Equal(new[] { "Novo grupo…", "Ocultar este grupo", "Grupos à vista" }, Rotulos(janela.MenuDe(Titulo(vm, "Favoritos"))));
            Assert.Equal(new[] { "Novo grupo…", "Grupos à vista" }, Rotulos(janela.MenuDe(null)));

            // Resultados não é um grupo de verdade: não tem o que ocultar
            vm.Filtro = "notas";
            Assert.Equal(new[] { "Novo grupo…", "Grupos à vista" }, Rotulos(janela.MenuDe(Titulo(vm, "Resultados"))));
            vm.Filtro = "";
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// Mostrar ou não um grupo, fixo ou do usuário: sai da árvore com o que está dentro, a
    /// pesquisa continua achando, fica gravado e volta pelo menu "Grupos à vista", marcado
    /// com o que está à vista. Quem sai não recebe mais nada pelo "Mover para".
    /// </summary>
    [AvaloniaFact]
    public void EsconderGruposDaArvore()
    {
        Montar();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var financeiro = Projeto(vm, "Financeiro");
            vm.AlternarOculto(Projeto(vm, "GTerm"));
            var fiscal = vm.CriarGrupo("Fiscal");
            var nfe = vm.CriarGrupo("NFe", null, fiscal);
            vm.MoverProjeto(Projeto(vm, "Notas"), nfe);
            Assert.Equal(new[] { "Favoritos", "Recentes", "Fiscal", "NFe", "DBISAM", "Private", "Ocultos" }, Titulos(vm));

            GrupoAVista Marca(string nome) => vm.GruposAVista().Single(g => g.Nome.Trim() == nome);
            Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Fiscal", "   NFe", "Private", "Ocultos" },
                vm.GruposAVista().Select(g => g.Nome));
            Assert.All(vm.GruposAVista(), g => Assert.True(g.Marcado));

            // o caso que motivou: Recentes e Ocultos fora da árvore
            vm.MostrarOuEsconderGrupo(MainViewModel.GrupoRecentes);
            vm.MostrarOuEsconderGrupo(MainViewModel.GrupoOcultos);
            Assert.Equal(new[] { "Favoritos", "Fiscal", "NFe", "DBISAM", "Private" }, Titulos(vm));
            Assert.False(Marca("Recentes").Marcado);
            Assert.False(Marca("Ocultos").Marcado);
            Assert.True(Marca("Favoritos").Marcado);

            // quem está na base continua contado; os recentes continuam sendo gravados
            Assert.Equal(new[] { MainViewModel.GrupoRecentes, MainViewModel.GrupoOcultos }, WorkspaceStore.Load().GruposEscondidos);
            Assert.Equal(new[] { "Favoritos", "Fiscal", "NFe", "DBISAM", "Private" }, Titulos(new MainViewModel(new Dialogos())));

            // a pesquisa acha o oculto, com o grupo Ocultos fora da árvore
            vm.Filtro = "gterm";
            Assert.Equal(new[] { "GTerm" }, Nomes(vm, MainViewModel.GrupoResultados));
            vm.Filtro = "";

            // o marcado do menu é o estado do grupo, e o clique troca
            var lista = janela.MenuDe(Titulo(vm, "Favoritos")).Items.OfType<MenuItem>().Single(m => m.Header as string == "Grupos à vista");
            var recentes = lista.Items.OfType<MenuItem>().Single(m => m.Header as string == "Recentes");
            Assert.Equal(MenuItemToggleType.CheckBox, recentes.ToggleType);
            Assert.False(recentes.IsChecked);
            Assert.True(lista.Items.OfType<MenuItem>().Single(m => m.Header as string == "Favoritos").IsChecked);

            // grupo do usuário sai com o que está dentro, e a contagem do pai acompanha o filho
            Assert.Equal(1, Titulo(vm, "Fiscal").Contagem);
            vm.MostrarOuEsconderGrupo(nfe);
            Assert.DoesNotContain("NFe", Titulos(vm));
            Assert.Equal(0, Titulo(vm, "Fiscal").Contagem);
            vm.MostrarOuEsconderGrupo(nfe);
            vm.MostrarOuEsconderGrupo(fiscal);
            Assert.Equal(new[] { "Favoritos", "DBISAM", "Private" }, Titulos(vm));

            // o "Mover para" não leva para um grupo fora da árvore
            var linha = vm.Linhas.OfType<LinhaDoProjeto>().First(l => l.Projeto == financeiro);
            Assert.DoesNotContain(vm.DestinosDe(linha), d => d.Grupo.Id == fiscal || d.Grupo.Id == nfe);

            // criar um subgrupo dentro de quem está fora traz o pai de volta
            vm.CriarGrupo("Boleto", null, fiscal);
            Assert.Contains("Fiscal", Titulos(vm));
            Assert.Contains("Boleto", Titulos(vm));

            // tudo fora da árvore: a lista fica vazia, e o clique direito nela ainda traz de volta
            foreach (var g in vm.GruposAVista().Where(g => g.Marcado)) vm.MostrarOuEsconderGrupo(g.Id);
            Assert.Empty(vm.Linhas);
            Dispatcher.UIThread.RunJobs();

            var arvore = janela.FindControl<ListBox>("Lista")!;
            var pedidos = 0;
            arvore.AddHandler(Control.ContextRequestedEvent, (_, _) => pedidos++, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            var meio = arvore.TranslatePoint(new Point(arvore.Bounds.Width / 2, arvore.Bounds.Height / 2), janela)!.Value;
            janela.MouseDown(meio, MouseButton.Right);
            janela.MouseUp(meio, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, pedidos);
            Assert.Contains("Grupos à vista", Rotulos(janela.MenuDe(null)));
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    /// <summary>
    /// Nas Sessões vale o mesmo: um dia, as Favoritas ou as Ocultas fora da árvore. O dia fora
    /// não gasta o limite de conversas, a favorita não vira a primeira com as Favoritas fora, e
    /// os grupos de cada modo são marcados à parte.
    /// </summary>
    [AvaloniaFact]
    public async Task EsconderGruposNasSessoes()
    {
        Montar();
        var boleto = ContaDeTeste.Conversa(ContaDoClaude, Notas, "Boleto do Sicoob", DateTime.Now.AddMinutes(-30));
        var quadro = ContaDeTeste.Conversa(ContaDoClaude, PastaDoGTerm, "Quadro do cia", DateTime.Now.AddDays(-1));
        var nota = ContaDeTeste.Conversa(ContaDoClaude, Financeiro, "Nota de ajuste", DateTime.Now.AddDays(-30));
        File.WriteAllText(EstadoDoCl, $$"""{ "favoritas": [ "{{quadro}}" ] }""");

        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            vm.VerSessoesCommand.Execute(null);
            Assert.True(await Esperar(() => vm.SessoesLidas));
            Dispatcher.UIThread.RunJobs();

            string[] Ids() => vm.Linhas.OfType<LinhaDaSessao>().Select(l => l.Sessao.Id).ToArray();
            Assert.Equal(new[] { "Favoritas", "Hoje", "Mais antigas" }, Titulos(vm));
            Assert.Equal(new[] { "Favoritas", "Hoje", "Ontem", "Esta semana", "Mais antigas", "Ocultas" },
                vm.GruposAVista().Select(g => g.Nome));

            // um dia sai da árvore
            vm.MostrarOuEsconderGrupo("s:Mais antigas");
            Assert.Equal(new[] { "Favoritas", "Hoje" }, Titulos(vm));
            Assert.Equal(new[] { quadro, boleto }, Ids());

            // a pesquisa ainda acha, e fora dela o grupo continua de fora
            vm.Filtro = "ajuste";
            Assert.Equal(new[] { nota }, Ids());
            vm.Filtro = "";
            Assert.Equal(new[] { "Favoritas", "Hoje" }, Titulos(vm));

            // com as Favoritas fora, a primeira é a primeira da árvore
            vm.MostrarOuEsconderGrupo("s:favoritas");
            vm.VerProjetosCommand.Execute(null);
            Assert.Equal(new[] { "Favoritos", "Recentes", "DBISAM", "Private" }, Titulos(vm)); // nada muda nos projetos
            vm.VerSessoesCommand.Execute(null);
            Assert.Equal(boleto, vm.SessaoAVista!.Sessao.Id);
            Assert.Same(vm.SessaoAVista, vm.LinhaSelecionada);

            // a favorita não some junto com as Favoritas: volta para o dia dela
            Assert.Equal(new[] { "Hoje", "Ontem" }, Titulos(vm));
        }
        finally
        {
            vm.EncerrarTudo();
            janela.Close();
        }
    }

    [Fact]
    public void GruposEmArvoreComoNoGRepos()
    {
        Grupo G(string id, string nome, string? pai = null) => new() { Id = id, Nome = nome, PaiId = pai };
        var todos = new List<Grupo> { G("b", "Banco"), G("a", "Arquivo", "z"), G("s", "Sicoob", "b"), G("i", "Itaú", "b"), G("x", "Solto", "sumiu") };

        Assert.Equal(new[] { "Arquivo", "Banco", "Solto" }, GrupoArvore.Filhos(todos, null).Select(g => g.Nome)); // pai que sumiu volta à raiz
        Assert.Equal(new[] { "Itaú", "Sicoob" }, GrupoArvore.Filhos(todos, "b").Select(g => g.Nome));
        Assert.Equal(new[] { "Arquivo", "Banco", "   Itaú", "   Sicoob", "Solto" }, GrupoArvore.EmOrdem(todos).Select(g => g.Recuado));
        Assert.Equal("Banco / Sicoob", GrupoArvore.EmOrdem(todos).Single(g => g.Grupo.Id == "s").Caminho);
        Assert.Equal(new[] { "b", "s", "i" }.OrderBy(x => x), GrupoArvore.ComDescendentes(todos, "b").OrderBy(x => x));
        Assert.Equal(new[] { "s", "b" }, GrupoArvore.Ancestrais(todos, "s").Select(g => g.Id));
        Assert.False(GrupoArvore.PodeFicarDentro(todos, "b", "s"));
        Assert.False(GrupoArvore.PodeFicarDentro(todos, "b", "b"));
        Assert.True(GrupoArvore.PodeFicarDentro(todos, "s", "x"));

        // um ciclo gravado à mão no JSON não some nem trava
        var ciclo = new List<Grupo> { G("1", "Um", "2"), G("2", "Dois", "1") };
        Assert.Equal(2, GrupoArvore.EmOrdem(ciclo).Count);

        Assert.Equal(GroupPalette.Cores[1], GroupPalette.ProximaLivre(new[] { GroupPalette.Cores[0] }));
        Assert.Equal(PaletaDeCores.Cores.Count, PaletaDeCores.Cores.Distinct().Count());
        Assert.Equal(GroupPalette.Cores, PaletaDeCores.Cores.Take(GroupPalette.Cores.Length));
    }
}
