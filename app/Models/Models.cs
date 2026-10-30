using System;
using System.Collections.Generic;
using GTerm.Services;

namespace GTerm.Models;

/// <summary>
/// O que um projeto lembra entre uma abertura e outra, e de onde sai o arranque do shell
/// dele. Os projetos em si vêm das pastas das bases do cia.
/// </summary>
public sealed class Projeto
{
    public string Nome { get; set; } = "";
    public string Pasta { get; set; } = "";

    /// <summary>A cor da aba, escolhida na paleta dela; vazia, uma da paleta tirada do nome.</summary>
    public string Cor { get; set; } = "";

    /// <summary>Um dos ids de <see cref="Shells.Todos"/>.</summary>
    public string Shell { get; set; } = Shells.Padrao;

    /// <summary>
    /// Digitado no shell assim que ele abre. Vazio nos projetos do cia: o terminal deles é o
    /// Claude direto, e o segundo terminal é um shell limpo.
    /// </summary>
    public string Comando { get; set; } = "";

    /// <summary>
    /// Pasta de configuração da conta do Claude (vira CLAUDE_CONFIG_DIR no terminal); vazia
    /// usa a conta padrão.
    /// </summary>
    public string Conta { get; set; } = "";

    /// <summary>O painel lateral estava aberto: volta aberto.</summary>
    public bool Painel { get; set; }

    /// <summary>O segundo terminal estava aberto: volta aberto.</summary>
    public bool Auxiliar { get; set; }
}

/// <summary>
/// Um grupo da árvore, como os do GRepos: pasta e subpasta, com nome e cor. A lista é plana,
/// cada grupo com o pai; quem a lê como árvore é <see cref="GrupoArvore"/>. Uma base do cia
/// entra aqui, com o id dela, só quando ganha cor ou ordem próprias.
/// </summary>
public sealed class Grupo
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Cor { get; set; } = GroupPalette.Padrao;

    /// <summary>Grupo em que este fica dentro. Nulo é o nível principal.</summary>
    public string? PaiId { get; set; }

    /// <summary>O que está direto nele, na ordem da árvore: pastas de projeto ou ids de conversa.</summary>
    public List<string> Itens { get; set; } = new();
}

public sealed class Workspace
{
    /// <summary>Só os projetos que têm algo a lembrar (painel, segundo terminal).</summary>
    public List<Projeto> Projetos { get; set; } = new();

    /// <summary>Grupos e subgrupos da árvore dos projetos.</summary>
    public List<Grupo> Grupos { get; set; } = new();

    /// <summary>Grupos e subgrupos da árvore das conversas, no modo Sessões.</summary>
    public List<Grupo> GruposDasSessoes { get; set; } = new();

    /// <summary>
    /// Ids dos grupos fora da árvore, nos dois modos: fixos (Recentes, Ocultos, um dia, uma
    /// base) ou do usuário. Levam junto o que está dentro, e a pesquisa continua achando.
    /// </summary>
    public List<string> GruposEscondidos { get; set; } = new();

    /// <summary>Pastas dos projetos ocultos: saem dos grupos e ficam só em Ocultos e na pesquisa.</summary>
    public List<string> Ocultos { get; set; } = new();

    /// <summary>Ids das conversas ocultas: ficam só em Ocultas.</summary>
    public List<string> SessoesOcultas { get; set; } = new();

    /// <summary>A pasta do projeto que estava à vista ao fechar: é o que volta selecionado.</summary>
    public string? UltimaPasta { get; set; }

    /// <summary>Pastas cujas subpastas são os projetos: as mesmas do cia ($global:CiaBases).</summary>
    public List<string> Bases { get; set; } = Cia.BasesPadrao();

    /// <summary>Favoritos e recentes. É o arquivo do dashboard do cia: os dois mostram o mesmo.</summary>
    public string EstadoDoCia { get; set; } = Cia.EstadoPadrao();

    /// <summary>As conversas favoritas. É o arquivo do cl (Modules\claude-sessoes.ps1): os dois mostram as mesmas.</summary>
    public string EstadoDoCl { get; set; } = Cia.EstadoDoClPadrao();

    /// <summary>
    /// A conta do Claude de onde vêm as conversas do modo Sessões (projects, sessions, jobs);
    /// vazia é a padrão, ~\.claude.
    /// </summary>
    public string ContaDasSessoes { get; set; } = Cia.ContaDasSessoesPadrao();

    /// <summary>
    /// Como o Claude Code é chamado no terminal do projeto, sem shell. Vazio acha o
    /// claude.exe sozinho; os testes põem um eco no lugar.
    /// </summary>
    public string Claude { get; set; } = "";

    /// <summary>Um dos ids de <see cref="Shells.Todos"/>: o shell do segundo terminal.</summary>
    public string Shell { get; set; } = Shells.Padrao;

    /// <summary>Grupos da árvore recolhidos; null é o jeito do cia: Recentes e as bases fechados.</summary>
    public List<string>? Recolhidos { get; set; }

    /// <summary>Fonte do terminal; vazia usa a lista padrão.</summary>
    public string Fonte { get; set; } = "";
    public double TamanhoDaFonte { get; set; } = 13;

    /// <summary>
    /// Script de PowerShell do painel lateral (ex.: o ctop de consumo do Claude). Roda na
    /// pasta e na conta do projeto; vazio, o painel não existe.
    /// </summary>
    public string PainelComando { get; set; } = "";

    /// <summary>
    /// Piscar na barra de tarefas e contar no título quando um projeto que não está à vista
    /// termina ou fica esperando por você.
    /// </summary>
    public bool AvisarAtencao { get; set; } = true;

    /// <summary>Quando a API do GitHub foi consultada pela última vez, e o que ela disse.</summary>
    public string UltimaChecagem { get; set; } = "";
    public string UltimaTagVista { get; set; } = "";
}
