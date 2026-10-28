# CLAUDE.md

Orientações para trabalhar neste repositório.

## O que é

GTerm: o `cia` do usuário (o atalho do perfil que abre o Claude num projeto) com tela,
vários terminais numa janela só (C# / .NET 8 + Avalonia), um por projeto. A árvore à
esquerda é a lista do dashboard do cia (`C:\Users\GF\Scripts\cia\cia-dash.ps1`): Favoritos,
Recentes e uma pasta por base (`D:\Projetos\DBISAM`, `D:\Projetos\Private`). Os projetos
com o Claude aberto viram abas em cima do terminal, cada uma na cor do projeto. Divisão do
usuário: GTerm para desenvolver, GTest para testar, GRepos para o git. O cia no terminal
continua existindo; os dois dividem o mesmo `estado.json`.

Nasceu para um problema que continua valendo: fechar por engano a aba de um projeto no
Windows Terminal. Por isso as abas não têm botão de fechar, e encerrar o Claude ou fechar a
janela com terminal vivo **sempre confirma antes**.

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
`tests/IsolamentoDoWorkspace.cs` (um `ModuleInitializer`); não remova. Ele também trava o
`Cia.PastaDeTeste`: sem isso um `Workspace` novo apontaria para as bases e o `estado.json`
reais, e favoritar num teste mexeria nos favoritos do usuário.

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
aparece no rodapé da árvore. O Windows não deixa sobrescrever um .exe em execução, mas
deixa renomear: a troca é `atual → .old`, `novo → atual`, reabre, e o `.old` some na
abertura seguinte. Só vale no arquivo único (`Assembly.GetEntryAssembly()?.Location` vazio);
no `dist` de pasta o clique abre a página da release. Atualizar encerra todos os terminais,
e por isso confirma antes.

## Arquitetura

- **Services** — `ConPty` roda o shell num pseudoconsole do Windows; `Shells` resolve a
  linha de comando de cada shell e faz cada um emitir `OSC 133;D;<código>` a cada prompt;
  `Atividade` lê entrada e saída e deduz o estado (rodando, aguardando, concluído, erro)
  que colore o ponto das abas e da árvore; `WorkspaceStore` persiste em JSON (grava em `.tmp` e
  renomeia).
- **cia** — `Services/Cia.cs` faz o que o cia faria, sem passar por ele: os projetos são
  as subpastas das `Workspace.Bases` (tipo pelo arquivo da raiz, ícone Nerd Font na cor do
  Terminal-Icons, como no dashboard); favoritos e recentes vêm do `Workspace.EstadoDoCia` (o
  `estado.json` do dashboard), onde o GTerm grava os favoritos e, a cada abertura, o
  recente, preservando o resto. O terminal do projeto é o **Claude direto no
  pseudoconsole, sem shell** (`Arranque.DoClaude`, `claude.exe` do PATH ou de
  `~\.local\bin`): `Cia.Decidir` repete o `Open-CIA` do perfil (`Modules\aliases.ps1`) —
  retomar a última sessão com conversa (`UltimaSessao`, o `Get-UltimaSessao` do cia-dash)
  com `/compact` a partir de 300 KB, ou abrir na skill do modo (`/delphi-writer` na
  primeira base, `/retomar` nas outras, `/pedido N`, `/delphi-pr`, nenhuma). **Mudou lá,
  muda aqui.** Com um Claude já aberto, outro modo só depois de confirmar. Ao sair do
  Claude o terminal fica com o aviso, e o Enter sobe outro retomando a sessão. O segundo
  terminal continua sendo um shell. Vigias de pasta (`FileSystemWatcher`) relêem o estado
  quando o cia grava um recente e a base quando aparece pasta nova.
- **Árvore** — achatada numa `ListBox` (`MainViewModel.Linhas`), no estilo minimalista do
  GTest: `GrupoDaArvore` (seta, ponto e nome na cor, contagem; o clique recolhe, e
  `Workspace.Recolhidos` lembra; null = como no cia, Recentes e as bases fechados) e
  `LinhaDoProjeto` (o mesmo projeto aparece em mais de um grupo; as linhas ficam num cache
  por grupo e pasta para a lista não perder a seleção). A estrela e o ▶ da linha só
  aparecem com o mouse em cima e na escolhida (escondidos, guardam o lugar): a estrela
  favorita ou tira. Cheia, nos favoritos, fica sempre à vista. O filtro em cima vira o grupo Resultados. `Selecionado` é o projeto à vista; a linha marcada é a primeira dele.
  Ctrl+1..9 são os favoritos (o `[n]` do cia). A ordem deles muda arrastando a linha nos
  Favoritos (`MainWindow.ArrastarFavoritos`: a linha acompanha o mouse e a ordem só é
  gravada ao soltar), com Alt+↑/↓ ou pelo menu. Os abertos não entram na árvore.
- **Projeto parado** — no lugar do terminal, o nome, a pasta e o tipo, o quadro do
  dashboard do cia (git, CLAUDE.md, a sessão que o Enter retoma e a última abertura, lidos
  em `MainViewModel.CarregarQuadroAsync` a cada escolha; o git por `Cia.Git`, sem travar o
  índice) e os botões de abrir o Claude.
- **Abas** — `MainViewModel.Abertos`, na ordem em que foram abertos (Ctrl+Tab passa por
  elas). A da frente (`ProjetoViewModel.AVista`) ganha fundo e contorno na cor do projeto;
  as outras ficam com o ponto da cor, o nome apagado e o ponto de estado. A aba não tem
  menu: os botões dela (retomar, sessão nova, pedido, pull request, sem skill, cor,
  encerrar) ficam na barra embaixo das abas, no tom da aba da frente, e trocam com ela
  (`MainViewModel.AbaAtiva`). A cor é do projeto (`Projeto.Cor`, escolhida na barra e
  gravada); vazia, `GroupPalette.DoNome`, a mesma conta do GTest, para o projeto ter a
  mesma cor nos dois.
- **Encerrar o Claude** — encerrar, ou abrir de novo de outro jeito, manda o `/exit` antes
  de fechar o terminal (`TerminalSessao.SairAsync`: Ctrl+C para largar o que estiver
  digitado ou rodando, o comando, e o Enter depois de uma pausa). Não saindo em
  `MainViewModel.PrazoDaSaida`, o processo é encerrado à força. Fechar a janela não passa
  por isso: mata direto.
- **Claude Code** — `ClaudeHooks` instala hooks no `settings.json` de cada conta (botão em
  Preferências); o hook chama o próprio `GTerm.exe --estado <nome>`, que grava o nome no
  arquivo apontado por `GTERM_ESTADO` (um por terminal) e sai sem abrir janela. A sessão lê
  o arquivo a cada meio segundo e, enquanto o Claude está aberto, é isso que manda no
  estado. Os projetos do cia usam todos a conta padrão (o `cim`, da outra conta, fica de
  fora por enquanto); `Workspace.Projetos` guarda só o que um projeto tem a lembrar (cor,
  painel, segundo terminal).
- **Atenção** — um projeto que termina ou para para perguntar sem estar à vista (outro
  selecionado, ou a janela atrás de outra) fica com o nome marcado, entra na conta do
  título da janela e faz o botão piscar na barra de tarefas (`MainViewModel.EstadoMudou`,
  `BarraDeTarefas`). O "aguardando" por silêncio não conta: é palpite.
- **Segundo terminal** — cada projeto pode ter um shell simples embaixo do principal
  (`AuxiliarSessao`), sem o comando de abertura; só existe com o terminal do projeto
  iniciado e é encerrado junto com ele. Abre e fecha na pílula "Terminal" do canto (acesa
  enquanto aberto); a do painel lateral, "Painel", é igual.
- **ViewModels** — CommunityToolkit.Mvvm. `MainViewModel` tem os projetos, a árvore, a
  seleção e as sessões abertas; `TerminalSessao` é o shell de um projeto, criado quando o
  usuário manda abrir (o ▶ da linha, Enter ou duplo clique na árvore, o botão do projeto
  parado; selecionar não sobe nada) e vivo até ser encerrado.
- **Views** — `MainWindow` implementa `IDialogService`; diálogos montados em código em
  `Dialogs.cs`.

## Regras

- Aberto de dentro de uma sessão do Claude Code, o app herdaria `NO_COLOR=1` e as
  `CLAUDE_*` dela, e os shells herdariam do app: prompt e Claude sem cor (parece que o
  oh-my-posh não carregou) e um `claude` aberto lá dentro se achando sessão filha. Por
  isso `Program.Main` chama `ClaudeHooks.LimparHeranca()` antes de subir a janela. Ainda
  assim, para o usuário testar, prefira abrir pelo Explorer:
  `explorer.exe app\bin\Debug\net8.0\GTerm.exe`.
- `Program.Main` trata `--estado` antes de tocar no Avalonia: é chamado a cada evento do
  Claude e precisa sair em ~100 ms. Nada de inicialização antes desse `if`.
- Os hooks apontam para o caminho do executável que os instalou. Uma versão publicada
  corrige isso sozinha ao abrir (`MainViewModel.CorrigirHooks`); build local não, para não
  tomar os hooks do GTerm instalado, e mostra "Corrigir" em Preferências.
- O cursor é sempre a barra (`CaretStyle="Bar"`; sem o foco, parada e esmaecida). O
  Claude, por padrão, esconde o cursor do terminal e desenha um bloco invertido dele, que
  nada aqui alcança: por isso `Arranque.DoClaude` sobe com `CLAUDE_CODE_NATIVE_CURSOR=1`,
  e ele passa a usar o do terminal.
- Terminal que não está à vista não se mede, e o shell espera a medida para subir: por
  isso as sessões dos projetos emprestam a medida umas às outras (`TerminalSessao.Medir`).
  Painel e segundo terminal têm outro tamanho e ficam de fora.
- A versão nova é procurada na API do GitHub e, se ela recusar (cota de 60 consultas por
  hora por IP), no redirecionamento de `releases/latest` do site. Não espere workflow
  consultando a API em laço: gasta a cota do app instalado na mesma máquina.
- Não rode o `cia`/`cim` nem o `claude` do usuário em testes: gastam a cota dele. Os
  testes da janela montam bases e `estado.json` temporários e trocam o Claude por
  `cmd.exe /k echo` (`Workspace.Claude`), que mostra os argumentos e fica aberto como ele.
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
