# CLAUDE.md

Orientações para trabalhar neste repositório.

## O que é

GTerm: vários terminais numa janela só (C# / .NET 8 + Avalonia), um por projeto, na
sidebar em vez de abas. Existe para um problema: fechar por engano a aba de um projeto no
Windows Terminal. Por isso não há botão de fechar na linha do projeto, e encerrar
terminal, remover projeto ou fechar a janela com shell vivo **sempre confirma antes**.

Nasceu do terminal embutido do GRepos (`D:\Projetos\Private\GRepos`): `ConPty`,
`BarraDeTitulo`, `GroupPalette`, o tema e o `vendor/AvaloniaTerminal` são cópias de lá.
Correção feita em um desses arquivos provavelmente vale para o outro projeto também.

## Comandos

```bash
dotnet run --project app   # abre o aplicativo
dotnet build               # compila
dotnet test                # xunit (shells, ConPTY e a janela headless)
dotnet publish app -c Release -r win-x64 --self-contained false -o dist   # gera dist/GTerm.exe
```

`GTERM_HOME` aponta o workspace para outra pasta — use ao rodar o app em teste, para não
escrever no `%APPDATA%\GTerm\workspace.json` real. Nos testes quem trava isso é
`tests/IsolamentoDoWorkspace.cs` (um `ModuleInitializer`); não remova.

`GTERM_SHOTS=<pasta>` faz o teste da janela gravar `janela.png`, para conferir o visual.

## Publicação

Mesmo esquema do GRepos. O workflow `.github/workflows/build.yml` compila e testa a cada
push na main (artefatos de 30 dias) e, num push de tag `1.0.0.N`, publica a Release com
dois executáveis de arquivo único: `GTerm-<tag>.exe` (precisa do .NET 8 Desktop Runtime) e
`GTerm-<tag>-standalone.exe`. A versão do executável vem da tag; build local não tem
versão e mostra "build local" no rodapé.

**Antes de criar a tag**, acrescente a versão no topo de `app/Assets/notas-da-versao.md`
(`## 1.0.0.N — dd/mm/aaaa` e os itens), escrita para quem usa o app, não como assunto de
commit. É o que aparece em Preferências → Notas da versão; `AtualizadorTests` falha se o
arquivo sair do formato ou da ordem.

**Atualização do app** (`Atualizador`): uma consulta por dia à release mais nova; o convite
aparece no rodapé da sidebar. O Windows não deixa sobrescrever um .exe em execução, mas
deixa renomear: a troca é `atual → .old`, `novo → atual`, reabre, e o `.old` some na
abertura seguinte. Só vale no arquivo único (`Assembly.GetEntryAssembly()?.Location` vazio);
no `dist` de pasta o clique abre a página da release. Atualizar encerra todos os terminais,
e por isso confirma antes.

## Arquitetura

- **Services** — `ConPty` roda o shell num pseudoconsole do Windows; `Shells` resolve a
  linha de comando de cada shell e faz cada um emitir `OSC 133;D;<código>` a cada prompt;
  `Atividade` lê entrada e saída e deduz o estado (rodando, aguardando, concluído, erro)
  que colore o ponto da sidebar; `WorkspaceStore` persiste em JSON (grava em `.tmp` e
  renomeia).
- **Claude Code** — o app existe para tocar vários projetos com o Claude ao mesmo tempo.
  `ClaudeHooks` instala hooks no `settings.json` de cada conta (botão em Preferências); o
  hook chama o próprio `GTerm.exe --estado <nome>`, que grava o nome no arquivo apontado
  por `GTERM_ESTADO` (um por terminal) e sai sem abrir janela. A sessão lê o arquivo a
  cada meio segundo e, enquanto o Claude está aberto, é isso que manda no estado. Cada
  projeto tem ainda um comando de abertura (o `cia`/`cim` do usuário), uma conta
  (`CLAUDE_CONFIG_DIR`) e um painel lateral que roda o script das preferências. A sidebar
  junta os projetos por conta (`MainViewModel.Agrupar`), com o e-mail e o plano no título
  do grupo; a conta vem do campo do projeto, e vazio é a padrão.
- **ViewModels** — CommunityToolkit.Mvvm. `MainViewModel` tem os projetos, a seleção e as
  sessões abertas; `TerminalSessao` é o shell de um projeto, criado quando o usuário manda
  iniciar (o ▶ da linha; selecionar não sobe nada) e vivo até ser encerrado.
- **Views** — `MainWindow` implementa `IDialogService`; diálogos montados em código em
  `Dialogs.cs`.

## Regras

- **Não abra o app para o usuário testar com `dotnet run` de dentro do Claude Code**: os
  shells herdam `NO_COLOR=1` e as variáveis `CLAUDE_*` da sessão, o PowerShell passa a
  tirar as cores do prompt (parece que o oh-my-posh não carregou) e um `claude` aberto lá
  dentro se acha sessão filha. Compile e abra pelo Explorer:
  `explorer.exe app\bin\Debug\net8.0\GTerm.exe`.
- `Program.Main` trata `--estado` antes de tocar no Avalonia: é chamado a cada evento do
  Claude e precisa sair em ~100 ms. Nada de inicialização antes desse `if`.
- Os hooks apontam para o caminho do executável que os instalou. Mudou o app de pasta:
  reinstalar em Preferências.
- Não rode o `cia`/`cim` nem o `claude` do usuário em testes: gastam a cota dele.
- Com o app aberto o `dotnet build` falha (exe em uso). Para testar sem fechar:
  `dotnet test --artifacts-path <pasta temporária>`.
- Tudo que encerra um shell confirma antes. É a razão de o app existir.
- Bindings reflexivos (`AvaloniaUseCompiledBindingsByDefault` é `false`). Comando usado em
  menu de contexto fica no view model da linha (`ProjetoViewModel`): o menu é um popup e
  não alcança o `DataContext` da janela por `$parent`.
- Tela nova ganha teste com as listas **populadas**: erro de binding só aparece quando o
  `ItemTemplate` é construído.
- Comentário XML/XAML não pode conter dois hifens seguidos (quebra o build do Avalonia).
- Teste que grava o workspace fica na classe `JanelaTests`: o xunit roda classes em
  paralelo e todas dividem a mesma pasta temporária.
- Textos de interface em pt-br com acentuação correta.
