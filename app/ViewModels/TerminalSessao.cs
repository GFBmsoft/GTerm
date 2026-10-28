using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaTerminal;
using CommunityToolkit.Mvvm.ComponentModel;
using GTerm.Models;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>O que é preciso para subir um shell (ou o Claude direto).</summary>
/// <param name="Comando">Digitado assim que o shell mostra o primeiro prompt; vazio, nada.</param>
/// <param name="AoSair">O aviso quando o processo termina; null é o de shell.</param>
public sealed record Arranque(string Linha, string Pasta, Dictionary<string, string?>? Ambiente, string? Comando,
    string? AoSair = null)
{
    /// <summary>
    /// O terminal do projeto: o Claude Code direto no pseudoconsole, sem shell no meio, na
    /// pasta e na conta do projeto. Os argumentos vêm de <see cref="Cia.Decidir"/>.
    /// </summary>
    /// <param name="claude">O executável, já entre aspas, ou o que os testes põem no lugar.</param>
    public static Arranque DoClaude(Projeto projeto, string claude, string argumentos)
    {
        // por padrão o Claude esconde o cursor do terminal e desenha o dele, um bloco em
        // vídeo invertido; com esta variável ele usa o do terminal, a barra do GTerm
        var ambiente = ComConta(null, projeto) ?? new Dictionary<string, string?>();
        ambiente["CLAUDE_CODE_NATIVE_CURSOR"] = "1";
        return new((claude + " " + argumentos).Trim(), projeto.Pasta, ambiente, null,
            "Claude encerrado — Enter retoma a sessão");
    }

    /// <summary>O terminal do projeto: o shell dele, na pasta e na conta do Claude dele.</summary>
    /// <param name="comando">No lugar do comando de abertura do projeto, só desta vez.</param>
    public static Arranque DoProjeto(Projeto projeto, string? comando = null)
    {
        var (linha, ambiente) = Shells.Comando(projeto.Shell);
        return new Arranque(linha, projeto.Pasta, ComConta(ambiente, projeto), comando ?? projeto.Comando);
    }

    /// <summary>
    /// O segundo terminal do projeto: o mesmo shell, pasta e conta, sem o comando de
    /// abertura. É onde se roda um build ou um git sem interromper o Claude.
    /// </summary>
    public static Arranque DoAuxiliar(Projeto projeto) => DoProjeto(projeto, "");

    /// <summary>O painel lateral: o script das preferências, na mesma pasta e conta.</summary>
    public static Arranque DoPainel(Projeto projeto, string script) =>
        new(Shells.Painel(script), projeto.Pasta, ComConta(null, projeto), null);

    private static Dictionary<string, string?>? ComConta(Dictionary<string, string?>? ambiente, Projeto projeto)
    {
        var conta = ClaudeHooks.ContaDoProjeto(projeto.Conta, projeto.Comando);
        if (!ClaudeHooks.ContaPropria(conta)) return ambiente;
        ambiente ??= new Dictionary<string, string?>();
        ambiente["CLAUDE_CONFIG_DIR"] = conta.Trim();
        return ambiente;
    }
}

/// <summary>
/// Um shell dentro de um terminal. Continua vivo ao trocar de projeto: voltar a ele devolve
/// o mesmo shell, com o histórico e o que estiver rodando.
/// </summary>
public sealed partial class TerminalSessao : ObservableObject, IDisposable
{
    private readonly Func<Arranque> _arranque;
    private ConPty? _pty;
    private int _colunas = 100;
    private int _linhas = 24;
    private bool _tamanhoConhecido;
    private Arranque? _esperandoTela;
    private string? _comando;
    private readonly Atividade _atividade = new();

    // guardado na criação: as threads de leitura e de espera do processo só publicam aqui.
    // Pedindo Dispatcher.UIThread de lá, uma thread de fundo que chegasse primeiro (nos
    // testes, entre um teste e outro) criaria o dispatcher do processo no lugar errado
    private readonly Dispatcher _ui = Dispatcher.UIThread;
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromMilliseconds(500) };

    // por onde os hooks do Claude Code avisam o estado, um arquivo por terminal
    private readonly string _arquivoDeEstado =
        Path.Combine(Path.GetTempPath(), "gterm", Guid.NewGuid().ToString("n") + ".estado");
    private string _ultimoAviso = "";

    public TerminalControlModel Modelo { get; }

    /// <summary>A sessão do projeto selecionado — a única visível.</summary>
    [ObservableProperty] private bool _ativa;

    /// <summary>O shell saiu (exit, Ctrl+D): Enter abre outro na mesma pasta.</summary>
    [ObservableProperty] private bool _encerrada;

    /// <summary>Rodando, aguardando, concluído ou erro: a cor do ponto na sidebar.</summary>
    [ObservableProperty] private EstadoDoTerminal _estado;

    /// <summary>O estado atual é dos que pedem aviso: terminou ou está perguntando algo.</summary>
    public bool ChamaAtencao => !Encerrada && _atividade.ChamaAtencao;

    /// <summary>O processo vivo no terminal; é por ele que se sabe qual conversa do Claude a aba tem.</summary>
    public int? Pid => _pty is { } pty && !Encerrada ? pty.Pid : null;


    /// <summary>O terminal foi medido na tela, com estas colunas e linhas.</summary>
    public event Action<int, int>? Mediu;

    /// <summary>
    /// Empresta a medida de outro terminal a um que espera a tela para subir o shell. Os
    /// terminais dos projetos ocupam todos o mesmo espaço, e um que nunca foi mostrado não
    /// tem como se medir: sem isto, o shell dele só subiria ao ser selecionado.
    /// </summary>
    public void Medir(int colunas, int linhas)
    {
        if (_tamanhoConhecido || _esperandoTela is null) return;
        Modelo.Resize(colunas, linhas, 1, 1); // célula de 1x1: vira colunas x linhas, e avisa
    }

    public TerminalSessao(Projeto projeto) : this(() => Arranque.DoProjeto(projeto))
    {
    }

    /// <param name="arranque">
    /// Chamado a cada vez que o shell sobe: é assim que uma edição do projeto passa a valer.
    /// </param>
    public TerminalSessao(Func<Arranque> arranque)
    {
        _arranque = arranque;

        // sem reflow: TUIs de tela cheia (vim, less, git log) se desmancham ao redimensionar
        Modelo = new TerminalControlModel(new TerminalOptions
        {
            Cols = _colunas,
            Rows = _linhas,
            Scrollback = 5000,
            ReflowOnResize = false,
        });

        Modelo.UserInput += bytes =>
        {
            if (!Encerrada)
            {
                _atividade.Entrada(bytes, DateTime.UtcNow);
                Estado = _atividade.Estado;
                _pty?.Escrever(bytes);
            }
            else if (Array.IndexOf(bytes, (byte)'\r') >= 0)
                Reabrir(_arranque);
        };
        Modelo.SizeChanged += (colunas, linhas, _, _) =>
        {
            _colunas = colunas;
            _linhas = linhas;
            _tamanhoConhecido = true;

            if (_esperandoTela is { } pendente)
            {
                _esperandoTela = null;
                Reabrir(() => pendente);
            }
            else
            {
                _pty?.Redimensionar(colunas, linhas);
            }

            Mediu?.Invoke(colunas, linhas);
        };

        _relogio.Tick += (_, _) =>
        {
            LerAvisoDoClaude();
            _atividade.Tique(DateTime.UtcNow);
            Estado = _atividade.Estado;
        };
    }

    /// <param name="esperarTela">
    /// Só sobe o shell quando o terminal souber quantas colunas e linhas tem na tela. Sem
    /// isso ele nasce em 100x24 e é redimensionado no meio do que o perfil desenha ao
    /// abrir (o pokémon, o prompt), que sai quebrado. Pasta e shell são conferidos já.
    /// </param>
    public void Iniciar(bool esperarTela = false)
    {
        var arranque = _arranque();
        if (!Directory.Exists(arranque.Pasta))
            throw new DirectoryNotFoundException("Pasta não encontrada: " + arranque.Pasta);

        if (esperarTela && !_tamanhoConhecido)
        {
            _esperandoTela = arranque;
            Encerrada = false;
            return;
        }

        Subir(arranque);
    }

    private void Subir(Arranque arranque)
    {
        var ambiente = arranque.Ambiente is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(arranque.Ambiente);
        Directory.CreateDirectory(Path.GetDirectoryName(_arquivoDeEstado)!);
        ambiente[ClaudeHooks.VarDoArquivo] = _arquivoDeEstado;

        _pty?.Dispose();
        var pty = ConPty.Iniciar(arranque.Linha, arranque.Pasta, _colunas, _linhas, ambiente);
        _pty = pty;
        _comando = string.IsNullOrWhiteSpace(arranque.Comando) ? null : arranque.Comando.Trim();
        Encerrada = false;
        _atividade.Reiniciar();
        Estado = _atividade.Estado;
        _relogio.Start();
        var aoSair = arranque.AoSair ?? "processo encerrado — Enter abre outro";

        pty.Encerrou += () => _ui.Post(() =>
        {
            if (_pty != pty) return; // já foi trocado por um novo
            Encerrada = true;
            _relogio.Stop();
            _atividade.Reiniciar();
            Estado = _atividade.Estado;
            Modelo.Feed("\r\n\x1b[2m[" + aoSair + "]\x1b[0m\r\n");
        });

        Task.Run(() => LerSaida(pty));
    }

    private void Reabrir(Func<Arranque> arranque)
    {
        try
        {
            var a = arranque();
            if (!Directory.Exists(a.Pasta))
                throw new DirectoryNotFoundException("Pasta não encontrada: " + a.Pasta);
            Subir(a);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            // pasta removida ou shell desinstalado com o app aberto
            Modelo.Feed("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n");
        }
    }

    private void LerSaida(ConPty pty)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var lidos = pty.Saida.Read(buffer, 0, buffer.Length);
                if (lidos <= 0) break;

                var copia = new byte[lidos];
                Buffer.BlockCopy(buffer, 0, copia, 0, lidos);
                _ui.Post(() =>
                {
                    if (_pty != pty) return; // resto de um shell já trocado
                    _atividade.Saida(copia, DateTime.UtcNow);
                    Estado = _atividade.Estado;
                    Modelo.Feed(copia, copia.Length);
                    EnviarComandoDeAbertura();
                });
            }
        }
        catch (IOException)
        {
            // pipe fechado: o processo saiu ou a sessão foi descartada
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // atalho: espera a marca do primeiro prompt; num shell que não a emite (bash com
    // PROMPT_COMMAND próprio) o comando de abertura não é enviado. Evoluir quando isso
    // aparecer, mandando depois de um prazo
    private void EnviarComandoDeAbertura()
    {
        if (_comando is not { } comando || !_atividade.ViuPrompt) return;
        _comando = null;
        Modelo.Send(comando + "\r"); // passa pela entrada: o estado já vira "rodando"
    }

    private Task<bool>? _saida;

    /// <summary>
    /// Pede ao programa que saia sozinho, digitando como o usuário faria: Ctrl+C larga o que
    /// estiver escrito na entrada (ou interrompe o que estiver rodando) e o comando de saída
    /// vai em seguida. Devolve se ele saiu no prazo; se não, quem chamou encerra à força.
    /// </summary>
    public Task<bool> SairAsync(string comando, TimeSpan prazo)
    {
        if (_saida is not { IsCompleted: false }) _saida = Sair(comando, prazo);
        return _saida;
    }

    private async Task<bool> Sair(string comando, TimeSpan prazo)
    {
        if (Encerrada) return true;
        if (_pty is not { } pty) return false; // ainda esperando a tela: não há o que pedir

        var saiu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Mudou(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Encerrada) && Encerrada) saiu.TrySetResult();
        }

        // entre um passo e outro o processo pode ter saído, ou a sessão ter sido descartada
        async Task<bool> Teclar(string texto, int pausa)
        {
            if (Encerrada || _pty != pty) return false;
            pty.Escrever(Encoding.UTF8.GetBytes(texto));
            await Task.Delay(pausa);
            return true;
        }

        PropertyChanged += Mudou;
        try
        {
            // interrompido logo depois do envio, o Claude pode devolver a mensagem à entrada,
            // e o segundo Ctrl+C a limpa. A pausa entre os dois passa da janela do "Ctrl+C de
            // novo para sair". O Enter vai depois de outra: junto com o texto, chegaria como
            // parte dele
            var ocupado = Estado is EstadoDoTerminal.Rodando or EstadoDoTerminal.Aguardando;
            if ((!ocupado || await Teclar("\x03", 1500)) &&
                await Teclar("\x03", 300) && await Teclar(comando, 200) && await Teclar("\r", 0))
                await Task.WhenAny(saiu.Task, Task.Delay(prazo));
            return Encerrada;
        }
        finally
        {
            PropertyChanged -= Mudou;
        }
    }

    private void LerAvisoDoClaude()
    {
        try
        {
            if (!File.Exists(_arquivoDeEstado)) return;
            var aviso = File.ReadAllText(_arquivoDeEstado);
            if (aviso == _ultimoAviso) return;
            _ultimoAviso = aviso;
            _atividade.Claude(ClaudeHooks.Evento(aviso));
        }
        catch (IOException)
        {
            // o hook está gravando neste instante: o próximo tique lê
        }
    }

    public void Dispose()
    {
        _relogio.Stop();
        _esperandoTela = null;
        var pty = _pty;
        _pty = null;
        pty?.Dispose();
        try { File.Delete(_arquivoDeEstado); } catch (IOException) { }
    }
}
