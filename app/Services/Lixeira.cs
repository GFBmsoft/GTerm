using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace GTerm.Services;

/// <summary>A Lixeira do Windows, para o que o usuário ainda pode querer de volta.</summary>
public static class Lixeira
{
    /// <summary>Manda o arquivo ou a pasta. Sem pergunta: quem chama já confirmou.</summary>
    public static void Mandar(string caminho)
    {
        if (Directory.Exists(caminho))
            FileSystem.DeleteDirectory(caminho, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteFile(caminho, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
