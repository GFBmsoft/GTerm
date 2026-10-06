using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using GTerm.Services;
using Xunit;

namespace GTerm.Tests;

/// <summary>
/// Comparação de versões, escolha do arquivo da release e a troca do executável.
/// A troca é feita com arquivos de verdade numa pasta temporária: é renomeação, e
/// o que interessa é que o antigo sobreviva para poder voltar.
/// </summary>
public class AtualizadorTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "1.0.0.2",
      "html_url": "https://github.com/GFBmsoft/GTerm/releases/tag/1.0.0.2",
      "assets": [
        {
          "name": "GTerm-1.0.0.2.exe",
          "size": 26214400,
          "browser_download_url": "https://github.com/GFBmsoft/GTerm/releases/download/1.0.0.2/GTerm-1.0.0.2.exe"
        },
        {
          "name": "GTerm-1.0.0.2-standalone.exe",
          "size": 93323264,
          "browser_download_url": "https://github.com/GFBmsoft/GTerm/releases/download/1.0.0.2/GTerm-1.0.0.2-standalone.exe"
        }
      ]
    }
    """;

    [Theory]
    [InlineData("1.0.0.1", "1.0.0.2", true)]
    [InlineData("1.0.0.1", "1.0.1.0", true)]
    [InlineData("1.0.0.2", "1.0.0.2", false)]
    [InlineData("1.0.0.3", "1.0.0.2", false)]
    // build feito depois da tag: está à frente da release, não atrás
    [InlineData("1.0.0.2-dev.5", "1.0.0.2", false)]
    [InlineData("1.0.0.1-dev.5", "1.0.0.2", true)]
    // tag escrita com "v" na frente ainda assim é comparada
    [InlineData("1.0.0.1", "v1.0.0.2", true)]
    // sem versão carimbada (build local) nunca há o que oferecer
    [InlineData("", "1.0.0.2", false)]
    [InlineData("1.0.0", "1.0.0.2", false)]
    public void NovidadeSoQuandoAReleaseEMaior(string atual, string tag, bool esperado)
    {
        Assert.Equal(esperado, Atualizador.TemNovidade(atual, tag));
    }

    [Theory]
    [InlineData("1.0.0.3+abc123", "1.0.0.3")]
    [InlineData("1.0.0.3-dev.5+abc123", "1.0.0.3-dev.5")]
    [InlineData("1.0.0+abc123", "")] // o padrão do SDK num build local não é versão lançada
    [InlineData(null, "")]
    public void SoAVersaoCarimbadaPeloWorkflowAparece(string? informacional, string esperado)
    {
        Assert.Equal(esperado, Atualizador.VersaoPublicada(informacional));
    }

    [Fact]
    public void AReleaseEntregaOStandaloneEntreOsAnexos()
    {
        var release = Atualizador.LerRelease(ReleaseJson);

        Assert.Equal("1.0.0.2", release!.Tag);
        Assert.Equal(2, release.Arquivos.Count);

        // o que troca sozinho é o standalone: o outro depende do .NET instalado
        Assert.Equal("GTerm-1.0.0.2-standalone.exe", release.Standalone!.Nome);
        Assert.Equal(93323264, release.Standalone.Tamanho);
        Assert.StartsWith("https://", release.Standalone.Url);

        Assert.Null(Atualizador.LerRelease("""{"message":"Not Found"}"""));
        Assert.Null(Atualizador.LerRelease("[]"));
    }

    [Fact]
    public void TrocaGuardaOAntigoEPoeONovoNoLugar()
    {
        var dir = Directory.CreateTempSubdirectory("gterm-troca-").FullName;
        try
        {
            var atual = Path.Combine(dir, "GTerm.exe");
            var novo = Path.Combine(dir, "GTerm-1.0.0.2-standalone.exe.baixando");
            File.WriteAllText(atual, "versao antiga");
            File.WriteAllText(atual + Atualizador.SufixoAntigo, "sobra de uma troca anterior");
            File.WriteAllText(novo, "versao nova");

            Atualizador.Trocar(atual, novo);

            Assert.Equal("versao nova", File.ReadAllText(atual));
            Assert.False(File.Exists(novo));

            // o antigo fica como caminho de volta, e some na abertura seguinte
            var antigo = atual + Atualizador.SufixoAntigo;
            Assert.Equal("versao antiga", File.ReadAllText(antigo));
            Atualizador.LimparAntigo(atual);
            Assert.False(File.Exists(antigo));

            // arquivo único: o assembly de entrada não tem .dll em disco para apontar
            Assert.True(Atualizador.PodeTrocarSozinho(atual, ""));
            // build de pasta: trocar só o .exe deixaria as DLLs ao lado desencontradas
            Assert.False(Atualizador.PodeTrocarSozinho(atual, Path.Combine(dir, "GTerm.dll")));
            Assert.False(Atualizador.PodeTrocarSozinho("", ""));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>O arquivo de verdade: é o que trava uma versão esquecida, repetida ou fora de ordem.</summary>
    [AvaloniaFact]
    public void NotasEmbutidasEstaoEmOrdemECompletas()
    {
        var notas = NotasDaVersao.Carregar();
        Assert.NotEmpty(notas);

        var numeros = notas.Select(n => Version.Parse(n.Versao)).ToList();
        Assert.Equal(numeros.OrderByDescending(v => v), numeros);
        Assert.Equal(numeros.Count, numeros.Distinct().Count());

        foreach (var n in notas)
        {
            Assert.True(n.Notas.StartsWith("- "), $"{n.Versao}: a nota deve ser uma lista de itens");
            Assert.True(DateTime.TryParseExact(n.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _), $"{n.Versao}: data inválida \"{n.Data}\"");
        }
    }
}