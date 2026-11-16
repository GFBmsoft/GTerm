using System.IO;

namespace GTerm.Services;

/// <summary>
/// Leitura de arquivo que outro programa grava (o Claude, os hooks, o ctop) sem travar quem
/// grava. O File.ReadAllText só compartilha leitura: aberto bem na hora em que o outro grava,
/// a gravação dele é recusada ("being used by another process", o EBUSY do Claude). Foi o que
/// derrubou a esteira da 1.0.0.29, e no app o aviso do hook podia se perder calado. Pego pela
/// metade, o JSON não fecha, e quem lê já trata como leitura que falhou.
/// </summary>
public static class Compartilhado
{
    /// <summary>Um arquivo pequeno inteiro, como o File.ReadAllText.</summary>
    public static string Texto(string arquivo)
    {
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
