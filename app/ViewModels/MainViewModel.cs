using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTerm.Models;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>Diálogos e seletor de pasta, implementados pela MainWindow.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> PromptAsync(string title, string label);
    Task<string?> PickFolderAsync(string title);

    /// <summary>Devolve os dados editados, ou null quando o usuário cancela.</summary>
    Task<ProjetoEditado?> EditarProjetoAsync(Projeto projeto);

    /// <summary>Devolve as preferências escolhidas, ou null quando o usuário cancela.</summary>
    /// <param name="contas">Pastas de configuração do Claude onde os avisos podem ser instalados.</param>
    Task<Preferencias?> PreferenciasAsync(Preferencias atuais, IReadOnlyList<string> contas);
}

public sealed record ProjetoEditado(string Nome, string Pasta, string Cor, string Shell, string Comando, string Conta);

/// <param name="Fonte">Vazia usa a lista padrão.</param>
/// <param name="PainelComando">Script do painel lateral; vazio, sem painel.</param>
public sealed record Preferencias(string Fonte, double Tamanho, string PainelComando);

/// <summary>Linha da sidebar: o projeto e, se já foi aberto, o terminal dele.</summary>
public sealed partial class ProjetoViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public Projeto Projeto { get; }
    public string Nome => Projeto.Nome;
    public string Pasta => Projeto.Pasta;
    public string NomeDoShell => Shells.Achar(Projeto.Shell).Nome;
    private Color CorBase => Color.Parse(GroupPalette.Normalizar(Projeto.Cor) ?? GroupPalette.Padrao);
    public IBrush Cor => new SolidColorBrush(CorBase);

    // fundo da pílula com o nome, acima do terminal: a cor do projeto esmaecida
    public IBrush Fundo => new SolidColorBrush(CorBase, 0.22);

    /// <summary>A pasta da conta do Claude deste projeto: projetos da mesma conta ficam juntos.</summary>
    public string Conta => ClaudeHooks.PastaDaConta(Projeto.Conta);

    /// <summary>
    /// O título do grupo, só no primeiro projeto de cada conta: o nome de quem está logado
    /// nela. Null nos demais, e em todos quando só a conta padrão está em uso.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemGrupo))]
    private string? _grupo;

    /// <summary>Plano da conta do grupo (MAX, TEAM...), ao lado do título.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemPlano))]
    private string? _plano;

    /// <summary>O e-mail e a pasta da conta, na dica do título do grupo.</summary>
    [ObservableProperty] private string? _dicaDoGrupo;

    /// <summary>O grupo da conta está recolhido: a linha do projeto some, o título fica.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visivel), nameof(Chevron))]
    private bool _recolhido;

    public bool TemGrupo => Grupo is not null;
    public bool TemPlano => Plano is not null;
    public bool Visivel => !Recolhido;
    public string Chevron => Recolhido ? "" : "";

    /// <summary>O comando de abertura chama o cia ou o cim: os modos do Claude se aplicam.</summary>
    public bool TemClaude => Atalho is "cia" or "cim";

    /// <summary>Pedido do Mantis e pull request só existem no cia.</summary>
    public bool EhCia => Atalho == "cia";

    private string Atalho => Projeto.Comando.TrimStart().Split(' ')[0].ToLowerInvariant();

    /// <summary>Null até o terminal do projeto ser iniciado, e depois de encerrado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Parado), nameof(Vivo), nameof(Rodando), nameof(Aguardando), nameof(Concluido), nameof(Erro))]
    private TerminalSessao? _sessao;

    /// <summary>Sem terminal: a linha mostra o botão de iniciar no lugar do ponto de estado.</summary>
    public bool Parado => Sessao is null;

    /// <summary>O painel lateral deste projeto, quando aberto.</summary>
    public TerminalSessao? PainelSessao { get; set; }

    /// <summary>Há um shell aberto: é o que pede confirmação antes de encerrar.</summary>
    public bool Vivo => Sessao is { Encerrada: false };

    // um por cor do ponto na sidebar; nenhum ligado = terminal parado no prompt
    public bool Rodando => Sessao?.Estado == EstadoDoTerminal.Rodando;
    public bool Aguardando => Sessao?.Estado == EstadoDoTerminal.Aguardando;
    public bool Concluido => Sessao?.Estado == EstadoDoTerminal.Concluido;
    public bool Erro => Sessao?.Estado == EstadoDoTerminal.Erro;

    public ProjetoViewModel(Projeto projeto, MainViewModel main)
    {
        Projeto = projeto;
        _main = main;
    }

    partial void OnSessaoChanged(TerminalSessao? oldValue, TerminalSessao? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= SessaoMudou;
        if (newValue is not null) newValue.PropertyChanged += SessaoMudou;
    }

    private void SessaoMudou(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TerminalSessao.Encerrada) or nameof(TerminalSessao.Estado))) return;
        OnPropertyChanged(nameof(Vivo));
        OnPropertyChanged(nameof(Rodando));
        OnPropertyChanged(nameof(Aguardando));
        OnPropertyChanged(nameof(Concluido));
        OnPropertyChanged(nameof(Erro));
    }

    /// <summary>Depois de editar: tudo aqui vem do modelo.</summary>
    public void Atualizar() => OnPropertyChanged(string.Empty);

    // os comandos ficam aqui porque o menu de contexto é um popup: de dentro dele não se
    // alcança o DataContext da janela por $parent
    [RelayCommand] private void Iniciar() => _main.Iniciar(this);
    [RelayCommand] private void AlternarGrupo() => _main.AlternarGrupo(this);
    [RelayCommand] private Task Editar() => _main.EditarAsync(this);
    [RelayCommand] private Task Reiniciar() => _main.ReiniciarAsync(this);
    [RelayCommand] private Task Encerrar() => _main.EncerrarAsync(this);
    [RelayCommand] private Task Remover() => _main.RemoverAsync(this);

    // modos do Claude: o comando de abertura do projeto com um parâmetro a mais do cia/cim
    [RelayCommand] private Task SessaoNova() => _main.AbrirComAsync(this, "-Novo");
    [RelayCommand] private Task SemSkill() => _main.AbrirComAsync(this, "-Limpo");
    [RelayCommand] private Task PullRequest() => _main.AbrirComAsync(this, "-Pr");
    [RelayCommand] private Task Pedido() => _main.AbrirPedidoAsync(this);
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogos;
    private readonly Workspace _workspace;

    public ObservableCollection<ProjetoViewModel> Projetos { get; } = new();

    /// <summary>Terminais abertos, todos vivos; só o do projeto selecionado aparece.</summary>
    public ObservableCollection<TerminalSessao> Sessoes { get; } = new();

    /// <summary>Painéis laterais abertos, um por projeto; também só aparece o do selecionado.</summary>
    public ObservableCollection<TerminalSessao> Paineis { get; } = new();

    [ObservableProperty] private ProjetoViewModel? _selecionado;

    /// <summary>O que aparece no lugar do terminal quando não há um para mostrar.</summary>
    [ObservableProperty] private string? _aviso;

    /// <summary>A escolhida na frente e, atrás, a lista padrão: é o que cobre fonte desinstalada.</summary>
    public string Fonte => _workspace.Fonte is { Length: > 0 } f ? f + "," + FontePadrao : FontePadrao;
    public double TamanhoDaFonte => _workspace.TamanhoDaFonte;

    // Nerd Font na frente: os ícones do prompt (oh-my-posh, starship) e de TUIs vivem na
    // área privada do Unicode, que a Cascadia Mono pura não tem
    public const string FontePadrao = "CaskaydiaCove NFM,CaskaydiaMono Nerd Font,JetBrainsMono NFM,Cascadia Mono,Consolas";

    public bool HaTerminalRodando => Projetos.Any(p => p.Vivo);

    /// <summary>Há um script de painel nas preferências: o botão do painel aparece.</summary>
    public bool TemPainel => !string.IsNullOrWhiteSpace(_workspace.PainelComando);

    public bool PainelVisivel => Selecionado?.PainelSessao is not null;

    public MainViewModel(IDialogService dialogos)
    {
        _dialogos = dialogos;
        _workspace = WorkspaceStore.Load();
        Atualizador.LimparAntigo();
        _rodape = RodapePadrao;

        foreach (var p in _workspace.Projetos) Projetos.Add(new ProjetoViewModel(p, this));
        Agrupar();

        Selecionado = Projetos.FirstOrDefault(p => p.Projeto.Id == _workspace.UltimoId);
        if (Selecionado is null) AtualizarAviso();
    }

    partial void OnSelecionadoChanged(ProjetoViewModel? value)
    {
        // selecionar não sobe o shell: o comando de abertura (o Claude do projeto) só roda
        // quando o usuário manda iniciar
        if (value is not null && _workspace.UltimoId != value.Projeto.Id)
        {
            _workspace.UltimoId = value.Projeto.Id;
            Salvar();
        }

        AtualizarAviso();
        Mostrar();
    }

    /// <summary>
    /// Junta na sidebar os projetos da mesma conta do Claude, a padrão primeiro, e põe o
    /// título no primeiro de cada grupo. Dentro do grupo vale a ordem em que foram criados.
    /// Com todos na conta padrão não há o que separar, e a lista fica sem títulos.
    /// </summary>
    private void Agrupar()
    {
        var padrao = ClaudeHooks.PastaDaConta(null);
        var ordenados = Projetos
            .OrderBy(p => !MesmaConta(p.Conta, padrao))
            .ThenBy(p => p.Conta, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => _workspace.Projetos.IndexOf(p.Projeto))
            .ToList();

        var selecionado = Selecionado;
        for (var i = 0; i < ordenados.Count; i++)
        {
            var atual = Projetos.IndexOf(ordenados[i]);
            if (atual != i) Projetos.Move(atual, i);
        }
        if (Selecionado != selecionado) Selecionado = selecionado; // a lista pode soltar a seleção ao mover

        var comTitulos = ordenados.Any(p => !MesmaConta(p.Conta, padrao));
        string? anterior = null;
        foreach (var p in ordenados)
        {
            var primeiro = comTitulos && !MesmaConta(p.Conta, anterior);
            var quem = primeiro ? Quem(p.Conta) : null;
            p.Grupo = quem?.Rotulo(p.Conta);
            p.Plano = quem?.Plano;
            p.DicaDoGrupo = quem is null ? null : (quem.Email is null ? "" : quem.Email + "\n") + p.Conta;
            p.Recolhido = comTitulos && _workspace.ContasRecolhidas.Any(c => MesmaConta(p.Conta, c));
            anterior = p.Conta;
        }
    }

    // o .claude.json é grande e recolher um grupo refaz os títulos: lido uma vez por conta
    private readonly Dictionary<string, Identidade> _identidades = new(StringComparer.OrdinalIgnoreCase);

    private Identidade Quem(string conta)
    {
        if (!_identidades.TryGetValue(conta, out var quem))
            _identidades[conta] = quem = ClaudeHooks.Identificar(conta);
        return quem;
    }

    /// <summary>O clique no título do grupo: recolhe ou abre os projetos da conta, e lembra.</summary>
    public void AlternarGrupo(ProjetoViewModel p)
    {
        if (_workspace.ContasRecolhidas.RemoveAll(c => MesmaConta(p.Conta, c)) == 0)
            _workspace.ContasRecolhidas.Add(p.Conta);
        Salvar();
        Agrupar();
    }

    private static bool MesmaConta(string a, string? b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Só o terminal e o painel do projeto selecionado ficam à vista.</summary>
    private void Mostrar()
    {
        foreach (var s in Sessoes) s.Ativa = s == Selecionado?.Sessao;
        foreach (var s in Paineis) s.Ativa = s == Selecionado?.PainelSessao;
        OnPropertyChanged(nameof(PainelVisivel));
    }

    /// <summary>Abre o terminal do projeto se ainda não houver um.</summary>
    /// <param name="comando">No lugar do comando de abertura do projeto, só nesta subida do shell.</param>
    private void Abrir(ProjetoViewModel p, string? comando = null)
    {
        if (p.Sessao is null)
        {
            // o shell que o usuário reabrir depois (Enter no terminal encerrado) volta ao
            // comando normal do projeto: o modo pedido aqui vale uma vez
            var daVez = comando;
            var sessao = new TerminalSessao(() =>
            {
                var arranque = Arranque.DoProjeto(p.Projeto, daVez);
                daVez = null;
                return arranque;
            });
            try
            {
                sessao.Iniciar(esperarTela: true);
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                sessao.Dispose();
                Aviso = ex.Message;
                return;
            }

            p.Sessao = sessao;
            Sessoes.Add(sessao);
        }

        if (p.Projeto.Painel) AbrirPainel(p);
        Aviso = null;
    }

    private void AbrirPainel(ProjetoViewModel p)
    {
        if (!TemPainel || p.PainelSessao is not null) return;

        var painel = new TerminalSessao(() => Arranque.DoPainel(p.Projeto, _workspace.PainelComando));
        try
        {
            painel.Iniciar(esperarTela: true);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            painel.Dispose(); // sem a pasta não há painel; o aviso do terminal já diz o porquê
            return;
        }

        p.PainelSessao = painel;
        Paineis.Add(painel);
    }

    private void FecharPainel(ProjetoViewModel p)
    {
        if (p.PainelSessao is not { } painel) return;
        Paineis.Remove(painel);
        p.PainelSessao = null;
        painel.Dispose();
    }

    private void Fechar(ProjetoViewModel p)
    {
        FecharPainel(p);
        if (p.Sessao is not { } sessao) return;
        Sessoes.Remove(sessao);
        p.Sessao = null;
        sessao.Dispose();
    }

    private void AtualizarAviso() => Aviso =
        Projetos.Count == 0 ? "Nenhum projeto ainda. Use “+” para escolher uma pasta."
        : Selecionado is null ? "Selecione um projeto."
        : Selecionado.Sessao is null ? "Terminal parado."
        : null;

    private void Salvar() => WorkspaceStore.Save(_workspace);

    [RelayCommand]
    private async Task Adicionar()
    {
        var pasta = await _dialogos.PickFolderAsync("Pasta do projeto");
        if (string.IsNullOrWhiteSpace(pasta)) return;

        var nome = Path.GetFileName(pasta.TrimEnd('\\', '/'));
        var projeto = new Projeto
        {
            Nome = nome.Length > 0 ? nome : pasta,
            Pasta = pasta,
            Cor = GroupPalette.ProximaLivre(_workspace.Projetos.Select(p => p.Cor)),
        };

        _workspace.Projetos.Add(projeto);
        var vm = new ProjetoViewModel(projeto, this);
        Projetos.Add(vm);
        Agrupar();
        Iniciar(vm); // já salva, com o projeto novo como último
    }

    [RelayCommand]
    private async Task AbrirPreferencias()
    {
        var novas = await _dialogos.PreferenciasAsync(
            new Preferencias(_workspace.Fonte ?? "", _workspace.TamanhoDaFonte, _workspace.PainelComando ?? ""),
            ClaudeHooks.Contas(_workspace.Projetos.Select(p => p.Conta)));
        if (novas is null) return;

        _workspace.Fonte = novas.Fonte.Trim();
        _workspace.TamanhoDaFonte = Math.Clamp(novas.Tamanho, 8, 32);
        _workspace.PainelComando = novas.PainelComando.Trim();
        Salvar();
        OnPropertyChanged(nameof(Fonte));
        OnPropertyChanged(nameof(TamanhoDaFonte));
        OnPropertyChanged(nameof(TemPainel));
    }

    /// <summary>Abre ou fecha o painel lateral do projeto selecionado, e lembra a escolha.</summary>
    [RelayCommand]
    private void AlternarPainel()
    {
        if (Selecionado is not { } p) return;

        p.Projeto.Painel = p.PainelSessao is null;
        Salvar();
        if (p.Projeto.Painel) AbrirPainel(p);
        else FecharPainel(p);
        Mostrar();
    }

    /// <summary>O botão no lugar do terminal parado: inicia o do projeto selecionado.</summary>
    [RelayCommand]
    private void AbrirSelecionado()
    {
        if (Selecionado is { } p) Iniciar(p);
    }

    /// <summary>O botão de iniciar da linha: sobe o terminal do projeto e o traz para a frente.</summary>
    /// <param name="comando">No lugar do comando de abertura do projeto, só nesta subida do shell.</param>
    public void Iniciar(ProjetoViewModel p, string? comando = null)
    {
        Selecionado = p;
        Abrir(p, comando);
        Mostrar();
    }

    /// <summary>Ctrl+Tab e Ctrl+Shift+Tab: dá a volta na lista.</summary>
    public void SelecionarVizinho(int passo)
    {
        var atual = Selecionado is null ? -1 : Projetos.IndexOf(Selecionado);

        // pula os projetos de grupo recolhido: selecionar uma linha que não se vê confunde
        for (var i = 1; i <= Projetos.Count; i++)
        {
            var p = Projetos[((atual + passo * i) % Projetos.Count + Projetos.Count) % Projetos.Count];
            if (!p.Visivel) continue;
            Selecionado = p;
            return;
        }
    }

    public async Task EditarAsync(ProjetoViewModel p)
    {
        if (await _dialogos.EditarProjetoAsync(p.Projeto) is not { } novo) return;

        p.Projeto.Nome = novo.Nome;
        p.Projeto.Pasta = novo.Pasta;
        p.Projeto.Cor = novo.Cor;
        p.Projeto.Shell = novo.Shell;
        p.Projeto.Comando = novo.Comando;
        p.Projeto.Conta = novo.Conta;
        Salvar();
        p.Atualizar();
        Agrupar(); // a conta pode ter mudado
    }

    public async Task ReiniciarAsync(ProjetoViewModel p)
    {
        if (p.Vivo && !await _dialogos.ConfirmAsync("Reiniciar terminal",
                $"Reiniciar o terminal de “{p.Nome}”? O que estiver rodando nele será interrompido."))
            return;

        Reabrir(p, null);
    }

    /// <summary>
    /// Sobe o terminal de novo com o comando de abertura do projeto mais um parâmetro
    /// (sessão nova, sem skill, PR, pedido). É outro Claude no lugar do que está aberto.
    /// </summary>
    public async Task AbrirComAsync(ProjetoViewModel p, string parametro)
    {
        if (p.Vivo && !await _dialogos.ConfirmAsync("Abrir o Claude de outro jeito",
                $"O terminal de “{p.Nome}” será reiniciado com “{parametro}”. O que estiver rodando nele será interrompido."))
            return;

        Reabrir(p, p.Projeto.Comando.Trim() + " " + parametro);
    }

    public async Task AbrirPedidoAsync(ProjetoViewModel p)
    {
        var pedido = (await _dialogos.PromptAsync("Pedido do Mantis", "Número do pedido"))?.Trim();
        if (string.IsNullOrEmpty(pedido)) return;

        // vai para a linha de comando do shell: só número, nada que ele possa interpretar
        if (!pedido.All(char.IsAsciiDigit))
        {
            Aviso = "Número de pedido inválido: " + pedido;
            return;
        }

        await AbrirComAsync(p, pedido);
    }

    private void Reabrir(ProjetoViewModel p, string? comando)
    {
        Fechar(p);
        Iniciar(p, comando);
    }

    public async Task EncerrarAsync(ProjetoViewModel p)
    {
        if (p.Sessao is null) return;
        if (p.Vivo && !await _dialogos.ConfirmAsync("Encerrar terminal",
                $"Encerrar o terminal de “{p.Nome}”? O que estiver rodando nele será interrompido."))
            return;

        Fechar(p);
        Mostrar();
        if (Selecionado == p) AtualizarAviso();
    }

    public async Task RemoverAsync(ProjetoViewModel p)
    {
        if (!await _dialogos.ConfirmAsync("Remover projeto",
                $"Remover “{p.Nome}” da lista? O terminal dele será encerrado. A pasta não é apagada."))
            return;

        Fechar(p);
        _workspace.Projetos.Remove(p.Projeto);
        Projetos.Remove(p); // se era o selecionado, a lista zera a seleção e o aviso se ajusta
        if (Selecionado == p) Selecionado = null;
        Agrupar(); // o título do grupo pode ter saído junto
        Salvar();
        AtualizarAviso();
    }

    /// <summary>Ao fechar a janela: nenhum shell fica órfão.</summary>
    public void EncerrarTudo()
    {
        foreach (var p in Projetos.ToList()) Fechar(p);
    }

    // ------------------------------------------------------------ atualização

    private Release? _release;

    /// <summary>Fecha a janela sem perguntar pelos terminais; a MainWindow é quem sabe fazer.</summary>
    public Action? Sair { get; set; }

    /// <summary>Tag da release mais nova que a versão em uso; vazia quando não há.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAtualizacao))]
    private string _atualizacaoTag = "";

    public bool TemAtualizacao => AtualizacaoTag.Length > 0;

    /// <summary>A linha do pé da sidebar: a versão, o convite para atualizar ou o andamento.</summary>
    [ObservableProperty] private string _rodape;

    private static string RodapePadrao =>
        Atualizador.VersaoEmUso is { Length: > 0 } versao ? "GTerm " + versao : "GTerm (build local)";

    partial void OnAtualizacaoTagChanged(string value)
    {
        if (value.Length > 0) Rodape = $"Atualizar para {value}";
    }

    /// <summary>
    /// Procura release nova. Build local não é avisado (não tem versão para comparar), e a
    /// API só é consultada uma vez por dia — o resultado anterior fica no workspace.
    /// </summary>
    public async Task VerificarAtualizacaoAsync(bool forcar = false)
    {
        var atual = Atualizador.VersaoEmUso;
        if (atual.Length == 0) return;

        try
        {
            var recente =
                DateTime.TryParse(_workspace.UltimaChecagem, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var quando) &&
                DateTime.UtcNow - quando < Atualizador.IntervaloDeChecagem;

            var tag = _workspace.UltimaTagVista ?? "";

            if (forcar || !recente)
            {
                _release = await Atualizador.UltimaReleaseAsync();
                tag = _release?.Tag ?? "";

                _workspace.UltimaChecagem = DateTime.UtcNow.ToString("o");
                _workspace.UltimaTagVista = tag;
                Salvar();
            }

            if (tag.Length > 0 && Atualizador.TemNovidade(atual, tag)) AtualizacaoTag = tag;
        }
        catch (Exception) when (!forcar)
        {
            // atualização é conveniência: sem rede ou fora da cota, o app segue igual.
            // Pedida pelo usuário, a falha sobe para quem chamou dizer o que houve.
        }
    }

    /// <summary>O clique no pé da sidebar: atualiza se há versão nova, senão procura uma.</summary>
    [RelayCommand]
    private async Task Atualizacao()
    {
        if (TemAtualizacao)
        {
            await AtualizarAgoraAsync();
            return;
        }

        if (Atualizador.VersaoEmUso.Length == 0)
        {
            Rodape = "Build local, sem versão";
            return;
        }

        try
        {
            Rodape = "Procurando atualização…";
            await VerificarAtualizacaoAsync(forcar: true);
            // o rodapé tem a largura da sidebar: frase inteira ali sai cortada
            if (!TemAtualizacao) Rodape = RodapePadrao + "  ✓";
        }
        catch (Exception e)
        {
            Rodape = "Não foi possível procurar: " + e.Message;
        }
    }

    /// <summary>
    /// Baixa a release e troca o executável. Só o de arquivo único pode ser trocado por
    /// aqui; no build de pasta resta abrir a página, onde o usuário escolhe o que baixar.
    /// </summary>
    private async Task AtualizarAgoraAsync()
    {
        try
        {
            _release ??= await Atualizador.UltimaReleaseAsync();

            var exe = Atualizador.CaminhoDoExe();
            var arquivo = _release?.Standalone;

            if (arquivo is null || !Atualizador.PodeTrocarSozinho(exe))
            {
                Atualizador.Reabrir(_release?.Url is { Length: > 0 } u
                    ? u
                    : $"https://github.com/{Atualizador.Slug}/releases/latest");
                return;
            }

            var mb = arquivo.Tamanho / 1024d / 1024d;
            if (!await _dialogos.ConfirmAsync("Atualizar o GTerm",
                    $"Baixar a versão {AtualizacaoTag} ({mb:N0} MB) e reiniciar o aplicativo?\n\n" +
                    "Todos os terminais abertos serão encerrados, com o que estiver rodando neles. " +
                    "O executável atual é guardado como cópia e volta sozinho se algo falhar."))
                return;

            var progresso = new Progress<double>(p => Rodape = $"Baixando… {p:P0}");
            var baixado = await Atualizador.BaixarAsync(arquivo, Path.GetDirectoryName(exe!)!, progresso);

            Rodape = "Instalando…";
            Atualizador.Trocar(exe!, baixado);
            Atualizador.Reabrir(exe!);

            // o novo processo já está subindo; este sai para liberar o arquivo
            EncerrarTudo();
            Sair?.Invoke();
        }
        catch (Exception e)
        {
            Rodape = "Não foi possível atualizar: " + e.Message;
        }
    }
}