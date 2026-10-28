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

    /// <summary>A cor da aba, escolhida no menu dela; vazia, uma da paleta tirada do nome.</summary>
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

public sealed class Workspace
{
    /// <summary>Só os projetos que têm algo a lembrar (painel, segundo terminal).</summary>
    public List<Projeto> Projetos { get; set; } = new();

    /// <summary>A pasta do projeto que estava à vista ao fechar: é o que volta selecionado.</summary>
    public string? UltimaPasta { get; set; }

    /// <summary>Pastas cujas subpastas são os projetos: as mesmas do cia ($global:CiaBases).</summary>
    public List<string> Bases { get; set; } = Cia.BasesPadrao();

    /// <summary>Favoritos e recentes. É o arquivo do dashboard do cia: os dois mostram o mesmo.</summary>
    public string EstadoDoCia { get; set; } = Cia.EstadoPadrao();

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
