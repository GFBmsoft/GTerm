using System;
using System.Collections.Generic;
using GTerm.Services;

namespace GTerm.Models;

/// <summary>Um terminal fixo: pasta, cor e shell. É o que substitui a aba.</summary>
public sealed class Projeto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Nome { get; set; } = "";
    public string Pasta { get; set; } = "";
    public string Cor { get; set; } = GroupPalette.Padrao;

    /// <summary>Um dos ids de <see cref="Shells.Todos"/>.</summary>
    public string Shell { get; set; } = Shells.Padrao;

    /// <summary>Digitado no shell assim que ele abre (ex.: <c>cia Financeiro -SemMenu -SemPainel</c>).</summary>
    public string Comando { get; set; } = "";

    /// <summary>
    /// Pasta de configuração da conta do Claude (vira CLAUDE_CONFIG_DIR no terminal); vazia
    /// usa a conta padrão.
    /// </summary>
    public string Conta { get; set; } = "";

    /// <summary>O painel lateral estava aberto: volta aberto.</summary>
    public bool Painel { get; set; }
}

public sealed class Workspace
{
    public List<Projeto> Projetos { get; set; } = new();

    /// <summary>Projeto que estava aberto ao fechar; é o que volta selecionado.</summary>
    public string? UltimoId { get; set; }

    /// <summary>Fonte do terminal; vazia usa a lista padrão.</summary>
    public string Fonte { get; set; } = "";
    public double TamanhoDaFonte { get; set; } = 13;

    /// <summary>
    /// Script de PowerShell do painel lateral (ex.: o ctop de consumo do Claude). Roda na
    /// pasta e na conta do projeto; vazio, o painel não existe.
    /// </summary>
    public string PainelComando { get; set; } = "";

    /// <summary>Contas do Claude (a pasta de cada uma) com o grupo recolhido na sidebar.</summary>
    public List<string> ContasRecolhidas { get; set; } = new();

    /// <summary>Quando a API do GitHub foi consultada pela última vez, e o que ela disse.</summary>
    public string UltimaChecagem { get; set; } = "";
    public string UltimaTagVista { get; set; } = "";
}
