using System;
using System.Text;

namespace GTerm.Services;

public enum EstadoDoTerminal
{
    /// <summary>No prompt, sem nada a contar: terminal recém-aberto.</summary>
    Ocioso,
    Rodando,
    /// <summary>Rodando, mas parado à espera de quem está no teclado.</summary>
    Aguardando,
    Concluido,
    Erro,
}

/// <summary>
/// Deduz o estado do terminal pelo que passa por ele, sem olhar a tela:
/// Enter no prompt começa um comando; a marca OSC 133;D que os shells emitem a cada prompt
/// (ver <see cref="Shells"/>) termina, com o código de saída; campainha ou notificação
/// (OSC 9 / 777) no meio do comando é pedido de atenção.
///
/// Com o Claude Code rodando dentro do terminal quem manda são os avisos dos hooks dele
/// (<see cref="Claude"/>): aí o estado é o que o Claude diz, não um palpite.
/// </summary>
public sealed class Atividade
{
    // atalho: comando que roda calado por mais que isto (um sleep, um link demorado) também
    // aparece como "aguardando"; evoluir quando isso incomodar, tornando o prazo configurável
    // ou exigindo a campainha
    public static readonly TimeSpan Silencio = TimeSpan.FromSeconds(5);

    private DateTime _ultimaSaida;
    private bool _porSilencio;
    private bool _claude;

    // 0 texto, 1 depois de ESC, 2 dentro de OSC, 3 ESC dentro de OSC
    private int _fase;
    private readonly StringBuilder _osc = new();

    public EstadoDoTerminal Estado { get; private set; }

    /// <summary>O shell já desenhou um prompt: está pronto para receber o comando de abertura.</summary>
    public bool ViuPrompt { get; private set; }

    public void Reiniciar()
    {
        Estado = EstadoDoTerminal.Ocioso;
        ViuPrompt = false;
        _claude = false;
        _fase = 0;
    }

    /// <summary>O que o usuário digitou.</summary>
    public void Entrada(ReadOnlySpan<byte> bytes, DateTime agora)
    {
        // respondeu a quem esperava: volta a rodar já, sem aguardar o próximo aviso
        var respondeu = Estado == EstadoDoTerminal.Aguardando;

        // dentro do Claude o Enter também escolhe item de menu e quebra linha: quem diz que
        // um pedido começou é o hook, não a tecla
        var comecou = !_claude && Estado != EstadoDoTerminal.Rodando && bytes.IndexOf((byte)'\r') >= 0;
        if (!respondeu && !comecou) return;

        Estado = EstadoDoTerminal.Rodando;
        _ultimaSaida = agora;
    }

    /// <summary>O que o shell escreveu. Os pedaços chegam cortados em qualquer ponto.</summary>
    public void Saida(ReadOnlySpan<byte> bytes, DateTime agora)
    {
        _ultimaSaida = agora;

        // voltou a escrever sozinho: não estava esperando ninguém
        if (Estado == EstadoDoTerminal.Aguardando && _porSilencio) Estado = EstadoDoTerminal.Rodando;

        foreach (var b in bytes)
        {
            switch (_fase)
            {
                case 0:
                    if (b == 0x1b) _fase = 1;
                    else if (b == 0x07) Atencao();
                    break;

                case 2:
                    if (b == 0x07) FecharOsc();
                    else if (b == 0x1b) _fase = 3;
                    else if (_osc.Length < 64) _osc.Append((char)b);
                    break;

                default: // 1 e 3: o byte depois de um ESC
                    if (_fase == 3 && b == (byte)'\\') FecharOsc();
                    else if (b == (byte)']')
                    {
                        _osc.Clear();
                        _fase = 2;
                    }
                    else _fase = b == 0x1b ? 1 : 0;
                    break;
            }
        }
    }

    /// <summary>Chamado de tempos em tempos: é como o silêncio vira "aguardando".</summary>
    public void Tique(DateTime agora)
    {
        // o Claude pensa calado por muito mais que o prazo; lá o hook avisa quando ele espera
        if (_claude || Estado != EstadoDoTerminal.Rodando || agora - _ultimaSaida < Silencio) return;
        Estado = EstadoDoTerminal.Aguardando;
        _porSilencio = true;
    }

    /// <summary>
    /// Aviso de um hook do Claude Code (ver <see cref="ClaudeHooks"/>): pronto (abriu),
    /// rodando, aguardando (pediu permissão ou fez uma pergunta), concluido (terminou a
    /// resposta) e fim (saiu).
    /// </summary>
    public void Claude(string evento)
    {
        switch (evento)
        {
            case "pronto":
                _claude = true;
                Estado = EstadoDoTerminal.Ocioso;
                break;
            case "rodando":
                _claude = true;
                Estado = EstadoDoTerminal.Rodando;
                break;
            case "aguardando":
                _claude = true;
                Estado = EstadoDoTerminal.Aguardando;
                _porSilencio = false;
                break;
            case "concluido":
                _claude = true;
                Estado = EstadoDoTerminal.Concluido;
                break;
            case "fim":
                _claude = false;
                break;
        }
    }

    private void Atencao()
    {
        if (Estado != EstadoDoTerminal.Rodando && Estado != EstadoDoTerminal.Aguardando) return;
        Estado = EstadoDoTerminal.Aguardando;
        _porSilencio = false; // pediu de propósito: só o usuário tira deste estado
    }

    private void FecharOsc()
    {
        _fase = 0;
        var osc = _osc.ToString();

        if (osc.StartsWith("133;D", StringComparison.Ordinal))
        {
            ViuPrompt = true;

            // o prompt do shell voltou: se o Claude não avisou que saiu (morreu), saiu agora
            var saiuDoClaude = _claude;
            _claude = false;

            // prompt redesenhado sem comando no meio (Ctrl+L, primeiro prompt) não conta
            if (!saiuDoClaude && Estado != EstadoDoTerminal.Rodando && Estado != EstadoDoTerminal.Aguardando) return;

            // sem código (o cmd não informa) vale como sucesso
            var codigo = osc.Length > 6 && int.TryParse(osc.AsSpan(6), out var n) ? n : 0;
            Estado = codigo == 0 ? EstadoDoTerminal.Concluido : EstadoDoTerminal.Erro;
        }
        else if ((osc.StartsWith("9;", StringComparison.Ordinal) && !osc.StartsWith("9;4;", StringComparison.Ordinal)) ||
                 osc.StartsWith("777;", StringComparison.Ordinal))
        {
            // 9;4 é barra de progresso, não pedido de atenção
            Atencao();
        }
    }
}
