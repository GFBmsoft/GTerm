using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
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

            ClaudeHooks.Remover(conta);
            Assert.False(ClaudeHooks.Instalado(conta));
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

/// <summary>Tudo que grava o workspace fica nesta classe: dentro dela o xunit roda em série.</summary>
public class JanelaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("gterm-proj-").FullName;

    public JanelaTests() => File.Delete(WorkspaceStore.FilePath);

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private Workspace DoisProjetos()
    {
        var ws = new Workspace
        {
            Projetos =
            {
                new Projeto { Nome = "Financeiro", Pasta = _pasta, Cor = "#1F9D55", Shell = "cmd" },
                new Projeto { Nome = "Notas", Pasta = _pasta, Cor = "#E07B00", Shell = "cmd" },
            },
        };
        ws.UltimoId = ws.Projetos[0].Id;
        WorkspaceStore.Save(ws);
        return ws;
    }

    [Fact]
    public void WorkspaceVoltaComoFoiGravado()
    {
        var ws = DoisProjetos();
        var lido = WorkspaceStore.Load();

        Assert.Equal(new[] { "Financeiro", "Notas" }, lido.Projetos.Select(p => p.Nome));
        Assert.Equal("#E07B00", lido.Projetos[1].Cor);
        Assert.Equal("cmd", lido.Projetos[1].Shell);
        Assert.Equal(ws.UltimoId, lido.UltimoId);
        Assert.StartsWith(Path.GetTempPath(), WorkspaceStore.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A janela de verdade, com a lista populada: erro de binding só aparece quando o
    /// ItemTemplate é construído. O último projeto volta selecionado, mas parado: nada
    /// roda (nem o comando de abertura) até o usuário mandar iniciar.
    /// </summary>
    [AvaloniaFact]
    public async Task JanelaAbreNoUltimoProjetoParadoEIniciaNoBotao()
    {
        DoisProjetos();
        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, janela.GetVisualDescendants().OfType<ListBoxItem>().Count());
            Assert.Equal("Financeiro", vm.Selecionado?.Nome);
            Assert.Equal("Terminal parado.", vm.Aviso);
            Assert.False(vm.HaTerminalRodando);
            Assert.Empty(vm.Sessoes);
            Assert.All(vm.Projetos, p => Assert.True(p.Parado));

            vm.Selecionado!.IniciarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.Selecionado.Parado);
            Assert.Null(vm.Aviso);
            Assert.True(vm.HaTerminalRodando);
            Assert.Single(janela.GetVisualDescendants().OfType<TerminalControl>());

            // o prompt do cmd traz a pasta: prova que a saída do shell chega à tela
            var sessao = Assert.Single(vm.Sessoes);
            var relogio = Stopwatch.StartNew();
            bool chegou;
            do
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                chegou = Enumerable.Range(0, 24).Any(i =>
                    sessao.Modelo.Terminal.Engine.GetLine(i).Contains(Path.GetFileName(_pasta)));
            } while (!chegou && relogio.Elapsed < TimeSpan.FromSeconds(20));
            Assert.True(chegou, "o prompt do cmd não apareceu no terminal");

            // só grava PNG com GTERM_SHOTS; serve para conferir o visual sem abrir o app
            if (Environment.GetEnvironmentVariable("GTERM_SHOTS") is { Length: > 0 } shots)
            {
                // ícones de Nerd Font (branch, pasta, separador do powerline): quadrado vazio
                // aqui significa que a fonte do terminal não os tem
                sessao.Modelo.Feed("\r\n main  pasta  \r\n");
                // a sugestão do Claude vem em SGR 2: tem de sair mais apagada que o texto ao lado
                sessao.Modelo.Feed("> texto normal \x1b[2msugestão fosca\x1b[0m \x1b[32mverde \x1b[2mverde fosco\x1b[0m\r\n");
                Dispatcher.UIThread.RunJobs();
                Directory.CreateDirectory(shots);
                using var frame = janela.CaptureRenderedFrame();
                frame?.Save(Path.Combine(shots, "janela.png"));
            }

            // trocar de projeto só mostra o segundo, parado, e mantém o primeiro vivo
            vm.SelecionarVizinho(1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Notas", vm.Selecionado?.Nome);
            Assert.Single(vm.Sessoes);
            Assert.Equal("Terminal parado.", vm.Aviso);
            Assert.False(vm.Sessoes[0].Ativa);

            // o botão de iniciar da linha funciona também sem o projeto estar selecionado
            vm.SelecionarVizinho(1);
            vm.Projetos[1].IniciarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Notas", vm.Selecionado?.Nome);
            Assert.Equal(2, vm.Sessoes.Count);
            Assert.Equal(new[] { false, true }, vm.Sessoes.Select(s => s.Ativa));
            Assert.Equal(vm.Selecionado!.Projeto.Id, WorkspaceStore.Load().UltimoId);
        }
        finally
        {
            vm.EncerrarTudo(); // sem terminal rodando a janela fecha sem perguntar
            janela.Close();
        }
    }

    /// <summary>O motivo de o app existir: nada se encerra sem confirmação.</summary>
    [AvaloniaFact]
    public async Task EncerrarERemoverSoComConfirmacao()
    {
        DoisProjetos();
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos);
        try
        {
            var p = vm.Selecionado!;
            p.IniciarCommand.Execute(null);

            dialogos.Resposta = false;
            await vm.EncerrarAsync(p);
            await vm.RemoverAsync(p);
            Assert.Equal(2, dialogos.Perguntas);
            Assert.True(p.Vivo);
            Assert.Equal(2, vm.Projetos.Count);

            dialogos.Resposta = true;
            await vm.EncerrarAsync(p);
            Assert.False(p.Vivo);
            Assert.Empty(vm.Sessoes);
            Assert.Equal("Terminal parado.", vm.Aviso);

            vm.AbrirSelecionadoCommand.Execute(null);
            Assert.True(p.Vivo);
            Assert.Null(vm.Aviso);

            await vm.RemoverAsync(p);
            Assert.Equal("Notas", Assert.Single(vm.Projetos).Nome);
            Assert.Equal("Notas", Assert.Single(WorkspaceStore.Load().Projetos).Nome);
            Assert.False(vm.HaTerminalRodando);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    /// <summary>
    /// A marca do prompt atravessa o pseudoconsole de verdade: sem isso o ponto da sidebar
    /// nunca sairia do amarelo.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("cmd", '>')]
    [InlineData("gitbash", '$')]
    public async Task ComandoNoShellDeVerdadeTerminaVerde(string shell, char prompt)
    {
        using var sessao = new TerminalSessao(new Projeto { Pasta = _pasta, Shell = shell });
        try
        {
            sessao.Iniciar();
        }
        catch (FileNotFoundException)
        {
            return; // sem Git instalado não há o que testar
        }

        async Task<bool> Esperar(Func<bool> condicao)
        {
            var relogio = Stopwatch.StartNew();
            while (!condicao() && relogio.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
            }
            return condicao();
        }

        Assert.True(await Esperar(() => Enumerable.Range(0, 24).Any(i =>
            sessao.Modelo.Terminal.Engine.GetLine(i).Contains(prompt))), "sem prompt");
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
        var projeto = new Projeto { Pasta = _pasta, Shell = "cmd", Comando = "echo aberto-%GTERM_ESTADO:~-7%" };
        using var sessao = new TerminalSessao(projeto);
        sessao.Iniciar();

        async Task<bool> Esperar(Func<bool> condicao)
        {
            var relogio = Stopwatch.StartNew();
            while (!condicao() && relogio.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
            }
            return condicao();
        }

        string Tela() => string.Join("\n", Enumerable.Range(0, 24).Select(i => sessao.Modelo.Terminal.Engine.GetLine(i)));

        // a saída do echo prova as duas coisas: o comando rodou e a variável existe no shell
        Assert.True(await Esperar(() => Tela().Contains("\naberto-.estado")), Tela());
        Assert.True(await Esperar(() => sessao.Estado == EstadoDoTerminal.Concluido), "ficou em " + sessao.Estado);

        // o que o hook faria: o próprio shell grava o aviso no arquivo dele
        sessao.Modelo.Send("echo aguardando 1> \"%GTERM_ESTADO%\"\r");
        Assert.True(await Esperar(() => sessao.Estado == EstadoDoTerminal.Aguardando), "ficou em " + sessao.Estado);
    }

    [AvaloniaFact]
    public async Task ModoDoClaudeReabreComOParametroSoComConfirmacao()
    {
        var ws = DoisProjetos();
        ws.Projetos[0].Comando = "cia Financeiro -SemMenu";
        WorkspaceStore.Save(ws);
        var dialogos = new Dialogos();
        var vm = new MainViewModel(dialogos);
        try
        {
            var p = vm.Selecionado!;
            Assert.True(p.TemClaude);
            Assert.True(p.EhCia);
            Assert.False(vm.Projetos[1].TemClaude);
            p.IniciarCommand.Execute(null);
            var antes = p.Sessao;

            dialogos.Resposta = false;
            await p.SessaoNovaCommand.ExecuteAsync(null);
            Assert.Same(antes, p.Sessao);

            dialogos.Resposta = true;
            dialogos.Texto = "12a"; // pedido vai para a linha de comando: só número
            await p.PedidoCommand.ExecuteAsync(null);
            Assert.Same(antes, p.Sessao);
            Assert.Contains("inválido", vm.Aviso);

            dialogos.Texto = "4321";
            await p.PedidoCommand.ExecuteAsync(null);
            Assert.NotSame(antes, p.Sessao);
            Assert.True(p.Vivo);
            Assert.Null(vm.Aviso);
        }
        finally
        {
            vm.EncerrarTudo();
        }
    }

    [AvaloniaFact]
    public async Task PreferenciasDeFonteValemNaHoraEFicamGravadas()
    {
        DoisProjetos();
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
    public void PastaQueSumiuViraAvisoEmVezDeDerrubarOApp()
    {
        var ws = DoisProjetos();
        ws.Projetos[0].Pasta = Path.Combine(_pasta, "não-existe");
        WorkspaceStore.Save(ws);

        var vm = new MainViewModel(new Dialogos());
        vm.Selecionado!.IniciarCommand.Execute(null);

        Assert.Contains("Pasta não encontrada", vm.Aviso);
        Assert.Empty(vm.Sessoes);
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
    /// Projetos da mesma conta do Claude ficam juntos, a padrão primeiro, com o e-mail e o
    /// plano da conta no título do grupo. Sem conta à parte a lista não tem títulos.
    /// </summary>
    [AvaloniaFact]
    public void ProjetosFicamJuntosPorContaDoClaude()
    {
        var conta = Path.Combine(_pasta, ".claude-teste");
        Directory.CreateDirectory(conta);
        File.WriteAllText(Path.Combine(conta, ".credentials.json"), """{"claudeAiOauth":{"subscriptionType":"pro"}}""");
        Assert.Equal("PRO", ClaudeHooks.Plano(conta));

        // o plano do perfil vale mais que o das credenciais, que fica velho depois de um upgrade
        File.WriteAllText(Path.Combine(conta, ".claude.json"),
            """{"oauthAccount":{"emailAddress":"equipe@exemplo.com","organizationType":"claude_team"}}""");
        Assert.Equal("equipe@exemplo.com", ClaudeHooks.Identificar(conta).Rotulo(conta));
        File.WriteAllText(Path.Combine(conta, ".claude.json"),
            """{"oauthAccount":{"displayName":"Equipe Exemplo","emailAddress":"equipe@exemplo.com","organizationType":"claude_team"}}""");

        var ws = DoisProjetos();
        Assert.All(new MainViewModel(new Dialogos()).Projetos, p => Assert.False(p.TemGrupo));

        ws.Projetos[0].Conta = conta;
        WorkspaceStore.Save(ws);

        var janela = new MainWindow();
        var vm = (MainViewModel)janela.DataContext!;
        try
        {
            janela.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "Notas", "Financeiro" }, vm.Projetos.Select(p => p.Nome));
            Assert.True(vm.Projetos[0].TemGrupo);
            Assert.Equal("Equipe Exemplo", vm.Projetos[1].Grupo);
            Assert.Contains("equipe@exemplo.com", vm.Projetos[1].DicaDoGrupo);
            Assert.Equal("TEAM", vm.Projetos[1].Plano);
            Assert.Equal("Financeiro", vm.Selecionado?.Nome);

            void Foto(string nome)
            {
                if (Environment.GetEnvironmentVariable("GTERM_SHOTS") is not { Length: > 0 } shots) return;
                Directory.CreateDirectory(shots);
                using var frame = janela.CaptureRenderedFrame();
                frame?.Save(Path.Combine(shots, nome));
            }
            Foto("contas.png");

            // o clique no título recolhe a conta: a linha some, o Ctrl+Tab passa por cima e
            // a escolha fica gravada
            vm.Projetos[1].AlternarGrupoCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.Projetos[1].Visivel);
            Assert.True(vm.Projetos[0].Visivel);
            Assert.Equal("Equipe Exemplo", vm.Projetos[1].Grupo);
            Assert.Single(WorkspaceStore.Load().ContasRecolhidas);
            Foto("contas-recolhida.png");

            vm.SelecionarVizinho(1);
            Assert.Equal("Notas", vm.Selecionado?.Nome);
            vm.SelecionarVizinho(1);
            Assert.Equal("Notas", vm.Selecionado?.Nome);

            vm.Projetos[1].AlternarGrupoCommand.Execute(null);
            Assert.True(vm.Projetos[1].Visivel);
            Assert.Empty(WorkspaceStore.Load().ContasRecolhidas);
        }
        finally
        {
            janela.Close();
        }
    }

    private sealed class Dialogos : IDialogService
    {
        public bool Resposta;
        public int Perguntas;

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Perguntas++;
            return Task.FromResult(Resposta);
        }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<ProjetoEditado?> EditarProjetoAsync(Projeto projeto) => Task.FromResult<ProjetoEditado?>(null);
        public Task<Preferencias?> PreferenciasAsync(Preferencias atuais, System.Collections.Generic.IReadOnlyList<string> contas) =>
            Task.FromResult<Preferencias?>(PreferenciasNovas);
        public Task<string?> PromptAsync(string title, string label) => Task.FromResult(Texto);
        public string? Texto;
        public Preferencias? PreferenciasNovas;
    }
}
