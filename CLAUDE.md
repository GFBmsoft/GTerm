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
`BarraDeTitulo`, `GroupPalette`, `GrupoArvore`, `SeletorDeCor`, `ItemDeMenu`, o tema e o
`vendor/AvaloniaTerminal` são cópias de lá. Correção feita em um desses arquivos
provavelmente vale para o outro projeto também.

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
`Cia.PastaDeTeste`: sem isso um `Workspace` novo apontaria para as bases, o `estado.json`,
o `estado-cl.json` e as conversas do `~\.claude` reais, e favoritar num teste mexeria nos
favoritos do usuário.

`GTERM_SHOTS=<pasta>` faz os testes da janela gravarem as fotos dela (`abas.png`,
`quadro.png`, `sessoes.png`, `revisao-rodando.png`…), para conferir o visual.

## Publicação

Mesmo esquema do GRepos. O workflow `.github/workflows/build.yml` compila e testa a cada
push na main (artefatos de 30 dias) e, num push de tag `1.0.0.N`, publica a Release com
dois executáveis de arquivo único: `GTerm-<tag>.exe` (precisa do .NET 8 Desktop Runtime) e
`GTerm-<tag>-standalone.exe`. A versão do executável vem da tag; build local não tem
versão e mostra "build local" no rodapé. Os dois saem com `PublishReadyToRun`: sem ele o
app leva o dobro para abrir e para montar a árvore, compilando na hora.

Para medir a abertura, publique **sem** versão (`-p:Version` ou `VersionPrefix`): um
executável com versão, ao abrir, aponta os hooks do Claude do usuário para ele
(`MainViewModel.CorrigirHooks`), mesmo com `GTERM_HOME` em outra pasta.

**Antes de criar a tag**, acrescente a versão no topo de `app/Assets/notas-da-versao.md`
(`## 1.0.0.N — dd/mm/aaaa` e os itens), escrita para quem usa o app, não como assunto de
commit. É o que aparece em Preferências → Notas da versão; `AtualizadorTests` falha se o
arquivo sair do formato ou da ordem.

**Atualização do app** (`Atualizador`): uma consulta por dia à release mais nova; o convite
aparece no rodapé da árvore. O clique ali procura de novo e, com versão nova, já pergunta:
um só. O Windows não deixa sobrescrever um .exe em execução, mas
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
  **só se ela for de hoje e tiver contexto abaixo de 150 mil tokens** (`Cia.Retomavel`,
  `$global:CiaRetomarTokens` no cia, desde 09/11/2026), senão abrir na skill do modo
  (`/delphi-writer` na primeira base, `/retomar` nas outras, `/pedido N`, `/delphi-pr`,
  nenhuma). O contexto é o uso da última resposta, lido do fim do .jsonl
  (`Cia.ContextoDaSessao`, o `Get-ContextoDaSessao` do cia-dash); o tamanho do arquivo não
  serve, porque nunca diminui. O `-Continuar` (botão Continuar a última e o Retomar da barra
  da aba) retoma mesmo antiga ou grande. Motivo: cada
  turno relê o contexto inteiro, e as sessões retomadas dia após dia chegavam perto de 1M.
  **Mudou lá, muda aqui.** A exceção de propósito é o `/compact` que o cia manda ao
  continuar uma sessão grande (mediana de 49 s até o Claude responder): aqui ele é o botão
  Compactar da barra da aba. Com um Claude já aberto, outro modo só depois de confirmar. Ao sair do
  Claude o terminal fica com o aviso, e o Enter sobe outro na sessão que estava ali, grande
  ou não (`Cia.ModoDoEnter`: a última com conversa, se gravada depois da subida; senão a que
  subiu retomando). Sem nenhuma (aberto sem skill e fechado), abre como o cia: a última do
  projeto pode ser antiga e enorme, e não era a que estava no terminal. O segundo
  terminal continua sendo um shell. Vigias de pasta (`FileSystemWatcher`) relêem o estado
  quando o cia grava um recente e a base quando aparece pasta nova.
- **Visual** — repaginado em 13/11/2026 a partir de dois layouts do Herdr que o usuário
  trouxe (bettr e over9k). Dois planos: a janela e a árvore no tom mais escuro (`BgPanel`),
  separadas por um fio, e em cima dele os cartões um tom acima (`Bg`), de contorno fino e
  canto 10: o terminal, o segundo terminal e o painel, cada um no seu (`Border.cartao`, com
  `Border.cabecalho` dizendo o que é). O cartão do terminal leva no cabeçalho os botões da
  aba e, por cima, a moldura na cor do projeto (`MolduraDaAba`,
  `ProjetoViewModel.BordaDaBarra`), a mesma do contorno da ficha da aba. O contorno é um
  `Border` por cima, sem espessura no cartão: assim o cabeçalho começa na altura da pesquisa.
  No Windows 11 a barra de título dos diálogos leva a cor do fundo da janela
  (`BarraDeTitulo.Pintar`, `DWMWA_CAPTION_COLOR`); isso só se vê no app de verdade.
- **Barra de título** — na janela principal não há barra do sistema: a faixa das abas é a
  barra (`ExtendClientAreaToDecorationsHint`, com os 40 da faixa em
  `ExtendClientAreaTitleBarHeightHint`), como no Windows Terminal e nos layouts do Herdr. O
  Avalonia desenha só os três botões da janela no canto, que o Windows enxerga como
  minimizar, maximizar e fechar (os layouts de encaixe do Windows 11 continuam valendo);
  `MainWindow.AlinharBotoesDaJanela` os põe na altura das pílulas. Na faixa, o que é botão
  recebe o clique e todo o resto é o `FundoDaBarra`
  (`Win32Properties.NonClientHitTestResult="Caption"`): arrasta a janela e maximiza no duplo
  clique. Por isso nada por cima dele ganha `Background` sem ser para clicar, nem
  transparente (a rolagem das abas tem `Background="{x:Null}"`), e sempre sobra um vão de 36
  antes das pílulas do canto. `naBarra` abre o lugar dos botões da janela (3 de 45), e
  `Padding = OffScreenMargin` traz o conteúdo de volta com a janela maximizada, que passa da
  tela pela moldura. O título continua na barra de tarefas e no Alt+Tab. **Os testes sem tela
  não veem nada disso**: `IsExtendedIntoWindowDecorations` é falso neles. Para conferir, abra
  o app e mande `WM_NCHITTEST` aos pontos da faixa (2 = arrasta, 1 = app, 8/9/20 = botões da
  janela), normal e maximizada.
- **Árvore** — achatada numa `ListBox` (`MainViewModel.Linhas`), minimalista:
  `GrupoDaArvore` (seta, a contagem numa etiqueta na cor do grupo, no lugar do ponto do
  GTest, e o nome na mesma cor; o clique recolhe, e
  `Workspace.Recolhidos` lembra; null = como no cia, Recentes e as bases fechados) e
  `LinhaDoProjeto` (o mesmo projeto aparece em mais de um grupo; as linhas ficam num cache
  por grupo e pasta para a lista não perder a seleção). A estrela e o ▶ da linha só
  aparecem com o mouse em cima e na escolhida (escondidos, guardam o lugar): a estrela
  favorita ou tira. Cheia, nos favoritos, fica sempre à vista. O filtro em cima vira o grupo Resultados. `Selecionado` é o projeto à vista; a linha marcada é a primeira dele.
  A lista não rola sozinha até ela (`AutoScrollToSelectedItem` desligado): o Avalonia agenda
  uma rolagem a cada vez que a linha muda de lugar, com o lugar daquele instante, e soltar
  um projeto num subgrupo levava a lista até o fim e de volta. Quem rola é a janela, uma
  vez, quando a escolha muda.
  Os favoritos são todos numerados, como o `[n]` do cia; Ctrl+1..9 vão aos nove primeiros, e o grupo só aparece com algum à vista. A ordem deles muda arrastando a linha nos
  Favoritos (a linha acompanha o mouse e a ordem só é gravada ao soltar), com Alt+↑/↓ ou
  pelo menu. O menu do clique direito é montado na hora (`MainWindow.MenuDe`), porque o
  "Mover para" depende dos grupos daquele momento: não há `ContextMenu` nos templates. Cada
  item leva o ícone na cor da ação (`ItemDeMenu`, `Glifos`), como no GRepos e no GRemote: a
  ação que é pílula tem o glifo e a cor da pílula, e o grupo, a bolinha da cor dele.
- **Grupos** — grupos e subgrupos como os do GRepos, nos dois modos (`ViewModels/Grupos.cs`,
  `GrupoArvore`): `Workspace.Grupos` e `GruposDasSessoes`, listas planas com o pai, e
  `Grupo.Itens` com o conteúdo (pastas ou ids de conversa). Ficam no workspace: o cia e o cl
  não sabem deles. O projeto fica sempre pelo nome, no grupo e na base, como no GRepos
  (pedido do usuário: soltar no fim do grupo desfazia a ordem alfabética); a ordem é do
  usuário só nos Favoritos (os números do cia) e nas conversas dos grupos, na ordem de
  `Itens`. Uma base é um grupo fixo (`GruposDosProjetos`): recebe subgrupos e cor, mas não
  muda de nome nem de lugar, e só aceita os projetos dela; entra em `Workspace.Grupos` só ao
  ganhar cor. Projeto sem grupo fica na base, conversa sem grupo fica no dia dela. Arrastar
  (`Views/ArrasteNaArvore.cs`, com captura do ponteiro, não o drag do sistema) acende o
  título do grupo que recebe e, onde a ordem é do usuário, um traço marca o lugar; quem
  decide o que cabe é `MainViewModel.OndeCai`. Soltar nos Favoritos favorita,
  em Ocultos oculta. Grupo dentro de grupo também arrasta; para a raiz, só pela edição.
  Ocultar (`Workspace.Ocultos`, `SessoesOcultas`) tira de todos os grupos sem esquecer
  onde estava; o favorito oculto guarda o número, para continuar igual ao do cia. A
  pesquisa acha os ocultos. Qualquer grupo, fixo ou do usuário, também pode sair da árvore
  (`Workspace.GruposEscondidos`, em "Ocultar este grupo" e "Grupos à vista", no clique
  direito nos títulos e no fundo da lista, por onde ele volta): leva o que está dentro, e
  a pesquisa continua achando. Com as Favoritas fora, a conversa favorita volta para o dia.
- **Sessões** — o `cl` do perfil (`Modules\claude-sessoes.ps1`, as conversas do `/resume`)
  com tela: as pílulas Projetos/Sessões em cima da árvore trocam o que ela mostra
  (`MainViewModel.ModoSessoes`, em `ViewModels/Sessoes.cs`). As conversas de todas as
  pastas, as favoritas em cima (o `estado-cl.json` do cl, `Workspace.EstadoDoCl`) e as
  outras por dia, 30 sem pesquisa (o `-Max` do cl); a pesquisa filtra pelo título e pelo
  projeto. `SessoesDoClaude` lê como o cl: o título e o branch só pelo fim do .jsonl (há
  transcript de GB), a pasta de origem pelo começo, aberta pelo `sessions\<pid>.json` e
  `bg` pelo `jobs\`, e a primeira leitura sai ao abrir a janela, fora da tela, para a
  pílula não abrir vazia. Escolher mostra o quadro da conversa no lugar do terminal
  (`SessaoAVista`), e entrar nas Sessões já mostra a primeira favorita (sem favorita, a
  primeira conversa); voltar aos Projetos traz o projeto que estava à vista. Retomar abre a aba do projeto com `--resume <id>`
  (`Cia.ModoConversa`), e pasta fora das bases vira projeto avulso, com aba e sem linha na
  árvore. O + é o Claude sem conversa nenhuma, na pasta pessoal (aba "Claude"). **Mudou
  lá, muda aqui.** Excluir (no menu e no quadro) não vem do cl: depois de confirmar, manda
  para a Lixeira do Windows (`Lixeira`) o transcript e o que o Claude guarda da conversa ao
  lado (`SessoesDoClaude.Rastros`), e tira das favoritas, dos grupos e das ocultas. Aberta não
  se exclui: o Claude continuaria gravando nela. O `history.jsonl` fica, porque é um
  arquivo só, em que todo Claude aberto escreve. A marca de code review sai junto, se a
  revisão foi feita naquela conversa.
- **Projeto parado** — no lugar do terminal, o nome, a pasta e o tipo, o quadro do
  dashboard do cia (git, CLAUDE.md, a sessão que o Enter retoma e a última abertura, lidos
  em `MainViewModel.CarregarQuadroAsync` a cada escolha; o git por `Cia.Git`, sem travar o
  índice) e os botões de abrir o Claude. A sessão é relida a cada minuto enquanto o projeto
  parado está à vista (`LerSessaoAsync`), e `ProjetoViewModel.MostrarSessao` avisa a tela
  mesmo sem ela mudar: o "de hoje" da regra anda com o relógio, e o botão tem de dizer o
  que o clique vai fazer.
- **Abas** — `MainViewModel.Abertos`, na ordem em que foram abertos. Ctrl+Tab passa por
  elas, e também Ctrl+← e Ctrl+→ (pedido do usuário), dando a volta. As setas são as teclas
  que no terminal pulam palavra: por isso só trocam de aba com outra para onde ir
  (`PodeTrocarDeAba`; com uma aba só, seguem para o terminal), nunca dentro da pesquisa, e com
  Shift continuam do terminal. Encerrar a da frente traz a que ficou no lugar dela (a última dá lugar à anterior);
  sem outra aba, fica o quadro do projeto. A aba é uma ficha de canto suave (a pílula é do
  botão). A da frente (`ProjetoViewModel.AVista`) ganha fundo e contorno na cor do projeto;
  as outras ficam com o contorno neutro, o ponto da cor, o nome apagado e o estado. O estado,
  aqui e na árvore, é um anel (`Ellipse.estado`): vazado com o Claude parado no prompt,
  aberto e girando enquanto ele trabalha, cheio quando há o que ver (terminou, erro, espera
  por você). A aba não tem
  menu: os botões dela (retomar, sessão nova, pedido, pull request, sem skill, compactar,
  code review, cor, encerrar) ficam no cabeçalho do cartão do terminal, no tom da aba da
  frente, e trocam com ela (`MainViewModel.AbaAtiva`). Ali as pílulas são `quieta`: só o
  ícone na cor e o nome apagado, e a pílula inteira com o mouse em cima; nove acesas brigavam
  com o terminal. O Code review rodando continua aceso. Compactar digita o `/compact` no Claude
  (`TerminalSessao.ComandoAsync`) e só vale com ele parado, o que só os hooks sabem dizer
  (`EsperaComando`): o Ctrl+C antes do comando interromperia um trabalho, e no meio da
  subida derrubaria o Claude. A cor é do projeto (`Projeto.Cor`, escolhida na barra e
  gravada); vazia, `GroupPalette.DoNome`, a mesma conta do GTest, para o projeto ter a
  mesma cor nos dois.
- **Code review** — o `/code-review` roda em segundo plano, como um agente da conversa, e
  `RevisoesDoClaude` o acompanha pelos arquivos dele (`projects\<chave>\<sessão>\subagents`:
  `.forked-skill.json`, `.jsonl` e `.meta.json` com `finished`; interrompido, o aviso
  `killed` na conversa principal), a cada 2 s, em `MainViewModel.LerRevisoesAsync`. Vale
  também o digitado no terminal. A linha fica em cima do fio que separa o cabeçalho do
  cartão do terminal, onde não mexe na altura dele; entre os botões, na janela padrão, não
  sobra lugar. O percentual é estimado: o
  agente não informa a fase da receita, então a conta é o tempo corrido contra a mediana
  das revisões terminadas, do mesmo esforço se houver (`RevisoesDoClaude.Percentual`). Por
  isso ele só sobe, para em 99 e leva `~` no botão. Os passos não servem: vêm em rajadas.
  A última revisão terminada de cada projeto (`RevisoesDoClaude.UltimaDoProjeto`, de
  qualquer conversa dele) vira a marca da árvore e a linha do quadro, relidas a cada minuto
  e ao fim de uma revisão. A marca fica amarela com commit depois dela, contado pelo reflog
  (`Cia.CommitsDepois`), para não subir o git em dezenas de projetos; os arquivos alterados
  só o quadro vê. O resultado é texto livre: não há como contar os achados com segurança.
- **Botões** — uma medida só: pílula de 24 (`Button.pilula` com a classe da cor: fundo
  diluído, contorno e texto na cor de cada um, com ícone), no cabeçalho do cartão do terminal
  (lá, `quieta`), no projeto
  parado, na conversa e em Projetos/Sessões/+ (a escolha que não está ativa fica `apagada`,
  só com o ícone na cor, como as abas). O mesmo botão tem a mesma cor e o mesmo ícone em
  qualquer lugar. O principal (retomar) é a pílula verde de 28 em negrito (`principal`); o
  cabeçalho do cartão tem os 32 da pesquisa ao lado. Painel e Terminal, no canto, ficam
  `destacado` enquanto abertos. Na linha da árvore, o ▶ é a bolinha verde do Retomar e a
  estrela é só o símbolo (`simbolo`), sem pílula.
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
- **Consumo** — o cartão ao lado do terminal (a pílula "Consumo" do canto; é da janela,
  `Workspace.Consumo`, e acompanha o projeto à vista) é o `ctop -Slim` com tela, no lugar do
  painel lateral que o cia perdeu em 28/10/2026: o contexto da conversa, os limites do plano,
  os tokens da sessão e o projeto dia a dia (`ViewModels/Consumo.cs`, `ConsumoViewModel`).
  Com o Claude aberto é a conversa da aba (`sessions\<pid>.json`); parado, a última do
  projeto. `ConsumoDoClaude` lê os transcripts como o ctop (`Scripts\claude-stats\claude-stats.ps1`):
  o `usage` de cada resposta, uma vez por `message.id` (a resposta sai em várias linhas, e a
  sessão bifurcada copia o histórico: vale o arquivo mais antigo), com os subagentes. **Mudou
  lá, muda aqui.** A leitura é incremental (cada arquivo guarda até onde foi contado) e todo
  arquivo abre com `FileShare.ReadWrite | Delete`: reler a sessão inteira a cada 3 s, só
  compartilhando leitura, foi o que deixou o cia lento e fez o Claude avisar "Transcript
  writes are failing (EBUSY)". A cada 5 s com o cartão à vista, os dias do projeto a cada
  minuto, e arquivo sem gravação no período nem é aberto. A janela de contexto é 200 mil ou
  1 milhão (`ConsumoDoClaude.Janela`, o `Get-CtxLimit` do ctop, que perdeu em 13/11/2026 um
  degrau de 500 mil que não existe); aqui ela sai do maior contexto que a sessão já teve, lá
  do de agora. `LimitesDoPlano` é o Get-Limits do ctop: o endpoint do /usage
  (`api.anthropic.com/api/oauth/usage`, não é API pública estável) com o login de
  `.credentials.json`, que nunca é renovado daqui. O estado fica no arquivo do ctop
  (`%LOCALAPPDATA%\claude-stats\limits-cache.json`), para os dois respeitarem a mesma conta:
  uma consulta a cada 2 minutos, 5 de espera depois de um 429. Com o cartão aberto e pouco
  lugar, os botões do cabeçalho do terminal ficam só com o ícone (`MainWindow.CaberBotoes`).
- **Segundo terminal** — cada projeto pode ter um shell simples embaixo do principal, num
  cartão próprio com a pasta no cabeçalho
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
- A saída do processo chega à tela pela `FilaDeSaida` (`TerminalSessao`): a leitura junta,
  e a tela escoa na prioridade de fundo, 64 KB por vez, sem cortar caractere UTF-8. Antes
  ia um pedaço por vez na prioridade normal, acima do desenho e do teclado: ao retomar uma
  conversa longa o Claude despeja a conversa inteira, e a janela parava até o fim, com o
  foco (também de fundo) esperando atrás. O XTerm.NET fica na 1.0.16 ou mais (o porquê
  está no csproj do vendor): a 1.0.12 do AvaloniaTerminal original não passava de
  500 KB/s. Despejo de 2 MB pelo pseudoconsole: 8,9 s antes, 1,5 s depois.
- Terminal que não está à vista não se mede, e o shell espera a medida para subir: por
  isso as sessões dos projetos emprestam a medida umas às outras (`TerminalSessao.Medir`).
  Painel e segundo terminal têm outro tamanho e ficam de fora.
- A versão nova é procurada na API do GitHub e, se ela recusar (cota de 60 consultas por
  hora por IP), no redirecionamento de `releases/latest` do site. Não espere workflow
  consultando a API em laço: gasta a cota do app instalado na mesma máquina.
- Nenhum teste consulta a conta do Claude: os limites do plano recebem uma conta de mentira
  (`new LimitesDoPlano(consultar, pasta)`), e sem ela, com `Cia.PastaDeTeste` posta, a
  consulta de verdade volta sem resposta. O estado deles nos testes fica na pasta do teste,
  não no arquivo do ctop.
- Não rode o `cia`/`cim` nem o `claude` do usuário em testes: gastam a cota dele. Os
  testes da janela montam bases e `estado.json` temporários e trocam o Claude por
  `cmd.exe /k echo` (`Workspace.Claude`), que mostra os argumentos e fica aberto como ele.
- Com o app aberto o `dotnet build` falha (exe em uso). Para testar sem fechar:
  `dotnet test --artifacts-path <pasta temporária>`.
- Esta máquina tem o SDK 8, e a esteira compila num mais novo, com `LangVersion` em
  `latest`: o que compila aqui pode quebrar lá. Foi o que tirou a 1.0.0.19 do ar: num SDK
  mais novo, `.Reverse()` num array é o do `Span`, que não devolve nada. Use
  `Enumerable.Reverse(...)`.
- Tudo que encerra um shell confirma antes. É a razão de o app existir.
- Bindings reflexivos (`AvaloniaUseCompiledBindingsByDefault` é `false`). Comando usado em
  menu de contexto fica no view model da linha (`ProjetoViewModel`): o menu é um popup e
  não alcança o `DataContext` da janela por `$parent`.
- Tela nova ganha teste com as listas **populadas**: erro de binding só aparece quando o
  `ItemTemplate` é construído.
- **Botão é pílula, como no GRepos, no GRemote, no GTest e no GConv** (pedido do usuário:
  "padronizar todas as ferramentas com o mesmo estilo de ícones, formatos tipo pílula e a
  paleta de cores"). A cor é uma classe (`azul`, `verde`, `verdeAgua`, `roxo`, `laranja`,
  `amarelo`, `vermelho`, `cinza`; a tabela de qual ação leva qual cor está no Theme.axaml),
  nunca Foreground/Background/BorderBrush fixos no botão. Tamanho `pilula` (24, a medida do
  GTerm) ou `pequena` (= `tiny`); só ícone, `redonda`; botão sem classe já é a pílula cinza;
  `primary` = azul em negrito. Desligado fica cinza, e o estilo vem depois das cores (senão
  a cor vence); o Code review rodando (`revisando`) e a versão do rodapé ficam legíveis de
  propósito. `quieta` (só no cabeçalho do cartão do terminal, e só no GTerm) não troca a cor
  nem o ícone da ação: esconde o fundo e o contorno até o mouse chegar. **Glifo sai de `Controls\Glifos`** (no XAML `{x:Static c:Glifos.Nome}`), nunca
  escrito direto; os da Nerd Font (tipo do projeto, git do quadro, classe `nerd`) são de
  outra fonte e ficam onde estão. Diálogos: o rótulo passa por `DialogWindow.Rotular` (a
  tabela `Acoes`). Estilo do conteúdo do botão com `>` (o menu e a paleta da Cor são
  descendentes lógicos do botão: com o seletor de descendente, o que estava dentro deles
  pegava a cor dele). Botão que não é pílula (título de grupo, aba, amostra de cor) precisa
  do próprio `:pointerover`: o da pílula pinta o fundo com o contorno.
- Comentário XML/XAML não pode conter dois hifens seguidos (quebra o build do Avalonia).
- Teste que grava o workspace fica na classe `JanelaTests`: o xunit roda classes em
  paralelo e todas dividem a mesma pasta temporária.
- Textos de interface em pt-br com acentuação correta.
