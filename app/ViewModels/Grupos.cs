using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GTerm.Models;
using GTerm.Services;

namespace GTerm.ViewModels;

/// <summary>Onde cai o que está sendo arrastado na árvore.</summary>
/// <param name="Grupo">O grupo que recebe: o título dele fica em destaque.</param>
/// <param name="Vizinha">A linha ao lado do lugar em que ele cai; null é o fim do grupo, sem marca.</param>
/// <param name="Depois">Cai depois da vizinha, e não antes.</param>
public sealed record DestinoDoArraste(GrupoDaArvore Grupo, LinhaDaArvore? Vizinha, bool Depois);

/// <summary>
/// Organizar a árvore, nos dois modos: grupos e subgrupos como os do GRepos, com a mesma
/// paleta, arrastar projetos e conversas para eles, e ocultar o que não se usa. Tudo fica no
/// workspace: o cia e o cl não sabem de grupo nenhum, e os favoritos continuam sendo os deles.
///
/// Uma base do cia é um grupo fixo: recebe subgrupos, cor e ordem, mas não muda de nome nem
/// sai do lugar, e só aceita os projetos dela. O que não está em grupo nenhum fica na base,
/// e nas Sessões no dia da conversa.
/// </summary>
public sealed partial class MainViewModel
{
    public const string GrupoOcultas = "s:ocultas";
    private const string GrupoFavoritas = "s:favoritas";

    private static readonly StringComparer Chaves = StringComparer.OrdinalIgnoreCase;

    /// <summary>Os grupos da árvore à vista.</summary>
    private List<Grupo> GruposDoModo() => ModoSessoes ? _workspace.GruposDasSessoes : GruposDosProjetos();

    /// <summary>Os grupos do usuário e as bases, que só são gravadas depois de ganhar cor ou ordem.</summary>
    private List<Grupo> GruposDosProjetos()
    {
        var todos = _workspace.Grupos.ToList();
        var i = -1;
        foreach (var b in _workspace.Bases)
        {
            i++;
            if (string.IsNullOrWhiteSpace(b)) continue;
            var id = IdDaBase(b);
            var nome = System.IO.Path.GetFileName(b.Trim().TrimEnd('\\', '/'));
            if (todos.FirstOrDefault(g => g.Id == id) is { } gravada) gravada.Nome = nome;
            else todos.Add(new Grupo { Id = id, Nome = nome, Cor = CoresDasBases[i % CoresDasBases.Length] });
        }
        return todos;
    }

    /// <summary>
    /// Os projetos à vista de cada grupo, na ordem da árvore. Num grupo do usuário a ordem é a
    /// dele; na base, a ordem gravada e, depois, os que ainda não têm lugar, por nome.
    /// </summary>
    private Dictionary<string, List<ProjetoViewModel>> ProjetosPorGrupo(List<Grupo> todos, Func<ProjetoViewModel, bool> visivel)
    {
        var porPasta = new Dictionary<string, ProjetoViewModel>(Chaves);
        foreach (var p in Projetos) porPasta[p.Pasta] = p;

        var porGrupo = new Dictionary<string, List<ProjetoViewModel>>();
        foreach (var g in todos) porGrupo[g.Id] = new();

        var colocados = new HashSet<ProjetoViewModel>();
        foreach (var g in todos.Where(g => !EhBase(g.Id)))
            foreach (var pasta in g.Itens)
                if (porPasta.TryGetValue(pasta, out var p) && visivel(p) && colocados.Add(p))
                    porGrupo[g.Id].Add(p);

        foreach (var g in todos.Where(g => EhBase(g.Id)))
        {
            var ordem = new Dictionary<string, int>(Chaves);
            foreach (var pasta in g.Itens) ordem.TryAdd(pasta, ordem.Count);
            porGrupo[g.Id].AddRange(Projetos
                .Where(p => IdDaBase(p.Info.Base) == g.Id && visivel(p) && !colocados.Contains(p))
                .OrderBy(p => ordem.GetValueOrDefault(p.Pasta, int.MaxValue))); // estável: o resto fica por nome
        }
        return porGrupo;
    }

    // ------------------------------------------------------------ criar, editar, excluir

    /// <summary>Novo grupo no nível principal.</summary>
    [RelayCommand] private Task NovoGrupo() => NovoGrupoAsync(null);

    /// <summary>Novo grupo, ou subgrupo de <paramref name="paiId"/>. Devolve o id, ou null se o usuário desistiu.</summary>
    public async Task<string?> NovoGrupoAsync(string? paiId)
    {
        var todos = GruposDoModo();
        var pai = todos.FirstOrDefault(g => g.Id == paiId);

        // subgrupo nasce com a cor do pai: a pasta inteira fica do mesmo tom, e quem quiser muda
        var sugerida = pai?.Cor ?? GroupPalette.ProximaLivre(todos.Select(g => g.Cor));
        var r = await _dialogos.GrupoAsync(pai is null ? "Novo grupo" : $"Novo subgrupo de {pai.Nome}", "", sugerida,
            pai?.Id, GrupoArvore.EmOrdem(todos));
        return r is null ? null : CriarGrupo(r.Nome, r.Cor, r.PaiId);
    }

    /// <param name="paiId">Grupo em que o novo fica dentro; nulo é o nível principal.</param>
    public string CriarGrupo(string nome, string? cor = null, string? paiId = null)
    {
        var todos = GruposDoModo();
        var g = new Grupo
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Nome = nome,
            Cor = GroupPalette.Normalizar(cor) ?? GroupPalette.ProximaLivre(todos.Select(x => x.Cor)),
            PaiId = todos.Any(x => x.Id == paiId) ? paiId : null,
        };
        (ModoSessoes ? _workspace.GruposDasSessoes : _workspace.Grupos).Add(g);

        // pai recolhido esconderia o subgrupo que acabou de nascer
        AbrirGrupos(g.Id);
        Salvar();
        Remontar();
        return g.Id;
    }

    /// <summary>Edita um grupo: nome, cor e onde fica. Numa base, só a cor.</summary>
    public async Task EditarGrupoAsync(string id)
    {
        var todos = GruposDoModo();
        if (todos.FirstOrDefault(x => x.Id == id) is not { } g) return;

        // ele mesmo e o que está abaixo dele não podem ser o pai
        var abaixo = GrupoArvore.ComDescendentes(todos, id);
        var pais = GrupoArvore.EmOrdem(todos).Where(x => !abaixo.Contains(x.Grupo.Id)).ToList();

        var r = EhBase(id)
            ? await _dialogos.GrupoAsync($"Cor de {g.Nome}", g.Nome, g.Cor, null, null)
            : await _dialogos.GrupoAsync("Editar grupo", g.Nome, g.Cor, g.PaiId, pais);
        if (r is not null) AtualizarGrupo(id, r.Nome, r.Cor, r.PaiId);
    }

    public void AtualizarGrupo(string id, string nome, string cor, string? paiId)
    {
        var g = GrupoGravado(id);
        var todos = GruposDoModo();
        if (g is null) return;

        g.Cor = GroupPalette.Normalizar(cor) ?? g.Cor;
        if (!EhBase(id))
        {
            g.Nome = nome;

            // dentro de si mesmo ou de um descendente viraria um ciclo: fica onde estava
            if (GrupoArvore.PodeFicarDentro(todos, id, paiId))
                g.PaiId = todos.Any(x => x.Id == paiId) ? paiId : null;
        }
        Salvar();
        Remontar();
    }

    public async Task ExcluirGrupoAsync(string id)
    {
        if (GruposDoModo().FirstOrDefault(x => x.Id == id) is not { } g) return;
        if (await _dialogos.ConfirmAsync("Excluir grupo",
                $"Excluir o grupo “{g.Nome}”? O que está nele sobe um nível: nada é apagado.")) RemoverGrupo(id);
    }

    /// <summary>O que estava dentro sobe um nível: subgrupos e itens vão para o pai dele.</summary>
    public void RemoverGrupo(string id)
    {
        var lista = ModoSessoes ? _workspace.GruposDasSessoes : _workspace.Grupos;
        if (EhBase(id) || lista.FirstOrDefault(g => g.Id == id) is not { } grupo) return;

        lista.Remove(grupo);
        foreach (var g in lista.Where(g => g.PaiId == id)) g.PaiId = grupo.PaiId;

        // sem pai, ou com uma base por pai, o item volta para o lugar de quem não tem grupo
        if (lista.FirstOrDefault(g => g.Id == grupo.PaiId) is { } pai && !EhBase(pai.Id)) pai.Itens.AddRange(grupo.Itens);
        Salvar();
        Remontar();
    }

    /// <summary>O registro do grupo no workspace; a base ganha o dela aqui, ao mudar de cor ou de ordem.</summary>
    private Grupo? GrupoGravado(string id)
    {
        var lista = ModoSessoes ? _workspace.GruposDasSessoes : _workspace.Grupos;
        if (lista.FirstOrDefault(g => g.Id == id) is { } gravado) return gravado;
        if (!EhBase(id) || GruposDosProjetos().FirstOrDefault(g => g.Id == id) is not { } baseNova) return null;
        lista.Add(baseNova);
        return baseNova;
    }

    // o grupo e os pais dele: o que acabou de chegar ali fica à vista
    private void AbrirGrupos(string id)
    {
        var lista = _workspace.Recolhidos ??= RecolhidosPadrao();
        foreach (var g in GrupoArvore.Ancestrais(GruposDoModo(), id)) lista.RemoveAll(i => Chaves.Equals(i, g.Id));
    }

    // ------------------------------------------------------------ ocultar

    public bool EstaOculto(ProjetoViewModel p) => _workspace.Ocultos.Contains(p.Pasta, Chaves);
    public bool EstaOculta(LinhaDaSessao l) => _workspace.SessoesOcultas.Contains(l.Sessao.Id, Chaves);

    /// <summary>Tira o projeto dos grupos, sem esquecer onde ele estava, ou devolve.</summary>
    public void AlternarOculto(ProjetoViewModel p)
    {
        Alternar(_workspace.Ocultos, p.Pasta, GrupoOcultos);
        Salvar();
        Remontar();
    }

    public void AlternarOculta(LinhaDaSessao l)
    {
        Alternar(_workspace.SessoesOcultas, l.Sessao.Id, GrupoOcultas);
        Salvar();
        Remontar();
    }

    private void Alternar(List<string> ocultos, string chave, string grupo)
    {
        if (ocultos.RemoveAll(o => Chaves.Equals(o, chave)) > 0) return;

        // o grupo dos ocultos nasce fechado: estão ali para sair da frente
        if (ocultos.Count == 0)
        {
            var recolhidos = _workspace.Recolhidos ??= RecolhidosPadrao();
            if (!recolhidos.Contains(grupo, Chaves)) recolhidos.Add(grupo);
        }
        ocultos.Add(chave);
    }

    // ------------------------------------------------------------ arrastar

    /// <summary>Linha de projeto e de conversa arrastam, e título só o de grupo do usuário.</summary>
    public bool Arrastavel(LinhaDaArvore linha) => linha is not GrupoDaArvore g || EhDoUsuario(g.Id);

    /// <summary>Grupo criado pelo usuário, e não um dos fixos.</summary>
    public bool EhDoUsuario(string id) => !EhBase(id) && GruposDoModo().Any(g => g.Id == id);

    /// <summary>Grupo que se pode editar e que recebe subgrupos: os do usuário e as bases.</summary>
    public bool EhEditavel(string id) => GruposDoModo().Any(g => g.Id == id);

    private static string? GrupoDaLinha(LinhaDaArvore linha) => linha switch
    {
        GrupoDaArvore g => g.Id,
        LinhaDoProjeto p => p.Grupo,
        LinhaDaSessao s => s.Grupo,
        _ => null,
    };

    /// <summary>
    /// Onde cai o que foi arrastado com o ponteiro sobre <paramref name="alvo"/>, se couber.
    /// Título é o fim do grupo; linha é ao lado dela, onde a ordem é do usuário. Null quando
    /// não há o que fazer: o destino é onde ele já está, a base não é a do projeto, ou um grupo
    /// iria para dentro de si mesmo.
    /// </summary>
    /// <param name="depois">O ponteiro está na metade de baixo da linha.</param>
    public DestinoDoArraste? OndeCai(LinhaDaArvore origem, LinhaDaArvore? alvo, bool depois)
    {
        if (alvo is null || ReferenceEquals(origem, alvo) || GrupoDaLinha(alvo) is not { } id ||
            !_cache.TryGetValue("g|" + id, out var titulo))
            return null;

        // o mesmo projeto, ou a mesma conversa, em outro grupo da árvore
        if (origem is LinhaDoProjeto { Projeto: var a } && alvo is LinhaDoProjeto { Projeto: var b } && a == b) return null;
        if (origem is LinhaDaSessao { Sessao.Id: var c } && alvo is LinhaDaSessao { Sessao.Id: var d } && c == d) return null;

        var ordemDoUsuario = id == GrupoFavoritos || EhEditavel(id);
        var vizinha = alvo is GrupoDaArvore || origem is GrupoDaArvore || !ordemDoUsuario ? null : alvo;

        var cabe = origem switch
        {
            GrupoDaArvore g => GrupoCabe(g.Id, id),
            LinhaDoProjeto p => ProjetoCabe(p.Projeto, id, vizinha),
            LinhaDaSessao s => ConversaCabe(s, id, vizinha),
            _ => false,
        };
        return cabe ? new DestinoDoArraste((GrupoDaArvore)titulo, vizinha, depois) : null;
    }

    private bool GrupoCabe(string id, string destino)
    {
        var todos = GruposDoModo();
        return EhEditavel(destino) && todos.First(g => g.Id == id).PaiId != destino &&
               GrupoArvore.PodeFicarDentro(todos, id, destino);
    }

    private bool ProjetoCabe(ProjetoViewModel p, string destino, LinhaDaArvore? vizinha)
    {
        if (destino == GrupoFavoritos) return !p.Favorito;
        if (destino == GrupoOcultos) return !EstaOculto(p);
        if (!EhEditavel(destino)) return false;
        if (EhBase(destino) && IdDaBase(p.Info.Base) != destino) return false;

        var atual = EstaOculto(p) ? GrupoOcultos : GrupoDoItem(p.Pasta) ?? IdDaBase(p.Info.Base);
        return atual != destino || vizinha is not null;
    }

    private bool ConversaCabe(LinhaDaSessao l, string destino, LinhaDaArvore? vizinha)
    {
        var id = l.Sessao.Id;
        if (destino == GrupoFavoritas) return !l.Favorita;
        if (destino == GrupoOcultas) return !EstaOculta(l);

        // um dia não guarda nada: soltar nele é voltar para o dia dela
        var atual = EstaOculta(l) ? GrupoOcultas : GrupoDoItem(id);
        if (!EhEditavel(destino)) return atual is not null;
        return atual != destino || vizinha is not null;
    }

    // o grupo do usuário onde o item está; a base guarda só a ordem dos dela
    private string? GrupoDoItem(string chave) =>
        (ModoSessoes ? _workspace.GruposDasSessoes : _workspace.Grupos)
        .FirstOrDefault(g => !EhBase(g.Id) && g.Itens.Contains(chave, Chaves))?.Id;

    private DestinoDoArraste? _marcado;

    /// <summary>Acende o grupo que recebe e a marca do lugar, e apaga os anteriores.</summary>
    public void MarcarDestino(DestinoDoArraste? destino)
    {
        if (_marcado is { } antes)
        {
            antes.Grupo.AlvoDeSoltar = false;
            if (antes.Vizinha is { } v) v.SoltarAntes = v.SoltarDepois = false;
        }

        _marcado = destino;
        if (destino is null) return;
        destino.Grupo.AlvoDeSoltar = true;
        if (destino.Vizinha is { } n)
        {
            n.SoltarAntes = !destino.Depois;
            n.SoltarDepois = destino.Depois;
        }
    }

    /// <summary>Solta o que foi arrastado. O destino é aberto, para o resultado ficar à vista.</summary>
    public void Soltar(LinhaDaArvore origem, DestinoDoArraste destino)
    {
        DesfazerFavorito();
        var id = destino.Grupo.Id;
        switch (origem)
        {
            case GrupoDaArvore g:
                MudarDePai(g.Id, id);
                break;
            case LinhaDoProjeto p:
                MoverProjeto(p.Projeto, id, (destino.Vizinha as LinhaDoProjeto)?.Projeto, destino.Depois);
                break;
            case LinhaDaSessao s:
                MoverConversa(s, id, (destino.Vizinha as LinhaDaSessao)?.Sessao.Id, destino.Depois);
                break;
        }
    }

    private void MudarDePai(string id, string destino)
    {
        var todos = GruposDoModo();
        if (todos.FirstOrDefault(g => g.Id == id) is not { } g || !GrupoArvore.PodeFicarDentro(todos, id, destino)) return;
        g.PaiId = destino;
        AbrirGrupos(destino);
        Salvar();
        Remontar();
    }

    /// <summary>
    /// Leva o projeto para o grupo, ao lado do vizinho ou no fim. Na base ele sai dos grupos e
    /// volta para o lugar dele. Nos Favoritos fica também onde está; em Ocultos, some.
    /// </summary>
    public void MoverProjeto(ProjetoViewModel p, string destino, ProjetoViewModel? vizinho = null, bool depois = false)
    {
        if (destino == GrupoFavoritos)
        {
            if (!p.Favorito) Favoritar(p, vizinho, depois);
            return;
        }
        if (destino == GrupoOcultos)
        {
            if (!EstaOculto(p)) AlternarOculto(p);
            return;
        }

        var ordem = ProjetosPorGrupo(GruposDosProjetos(), _ => true).GetValueOrDefault(destino);
        _workspace.Ocultos.RemoveAll(o => Chaves.Equals(o, p.Pasta));
        foreach (var g in _workspace.Grupos) g.Itens.RemoveAll(i => Chaves.Equals(i, p.Pasta));

        // na base só há o que gravar quando ele ganha um lugar: a ordem de quem está lá
        // passa a ser explícita. Sem lugar, ele volta para a ordem que ela já tinha
        if (!EhBase(destino) || vizinho is not null)
        {
            var grupo = GrupoGravado(destino);
            if (grupo is null) return;
            if (EhBase(destino)) grupo.Itens = ordem!.Where(x => x != p).Select(x => x.Pasta).ToList();
            Inserir(grupo.Itens, p.Pasta, vizinho?.Pasta, depois);
        }

        AbrirGrupos(destino);
        Salvar();
        Remontar();
    }

    /// <summary>
    /// Leva a conversa para o grupo, ao lado da vizinha ou no fim. Num dia ela sai dos grupos
    /// e volta para o dia dela. Nas Favoritas fica também onde está; em Ocultas, some.
    /// </summary>
    public void MoverConversa(LinhaDaSessao l, string destino, string? vizinha = null, bool depois = false)
    {
        var id = l.Sessao.Id;
        if (destino == GrupoFavoritas)
        {
            if (!l.Favorita) AlternarFavorita(l);
            return;
        }
        if (destino == GrupoOcultas)
        {
            if (!EstaOculta(l)) AlternarOculta(l);
            return;
        }

        _workspace.SessoesOcultas.RemoveAll(o => Chaves.Equals(o, id));
        foreach (var g in _workspace.GruposDasSessoes) g.Itens.RemoveAll(i => Chaves.Equals(i, id));
        if (GrupoGravado(destino) is { } grupo) Inserir(grupo.Itens, id, vizinha, depois);

        AbrirGrupos(destino);
        Salvar();
        Remontar();
    }

    private static void Inserir(List<string> itens, string chave, string? vizinho, bool depois)
    {
        var i = vizinho is null ? -1 : itens.FindIndex(x => Chaves.Equals(x, vizinho));
        itens.Insert(i < 0 ? itens.Count : depois ? i + 1 : i, chave);
    }

    /// <summary>Para onde o item do menu "Mover para" pode ir, em ordem de árvore.</summary>
    public IReadOnlyList<GrupoNaArvore> DestinosDe(LinhaDaArvore linha)
    {
        bool Cabe(string id) => linha switch
        {
            LinhaDoProjeto p => ProjetoCabe(p.Projeto, id, null),
            LinhaDaSessao s => ConversaCabe(s, id, null),
            _ => false,
        };
        return GrupoArvore.EmOrdem(GruposDoModo()).Where(g => Cabe(g.Grupo.Id)).ToList();
    }

    /// <summary>A escolha no menu "Mover para": vai para o fim do grupo.</summary>
    public void MoverPara(LinhaDaArvore linha, string destino)
    {
        if (linha is LinhaDoProjeto p) MoverProjeto(p.Projeto, destino);
        else if (linha is LinhaDaSessao s) MoverConversa(s, destino);
    }

    /// <summary>A conversa saiu do grupo: "Sem grupo" no menu dela.</summary>
    public bool ConversaEmGrupo(LinhaDaSessao l) => GrupoDoItem(l.Sessao.Id) is not null;

    public void TirarDoGrupo(LinhaDaSessao l)
    {
        foreach (var g in _workspace.GruposDasSessoes) g.Itens.RemoveAll(i => Chaves.Equals(i, l.Sessao.Id));
        Salvar();
        Remontar();
    }
}
