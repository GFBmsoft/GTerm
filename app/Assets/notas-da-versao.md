## 1.0.0.15 — 30/10/2026
- **Grupos e subgrupos** na barra lateral, como no GRepos: o botão de pasta ao lado da pesquisa cria um grupo, e o clique direito num grupo cria um subgrupo, edita o nome, a cor e onde ele fica, ou exclui. Excluir não apaga nada: o que estava dentro sobe um nível. DBISAM e Private também recebem subgrupos e podem mudar de cor.
- **Arrastar**: leve um projeto para um grupo, ou para o meio de outros, e a ordem fica gravada. Um traço mostra onde ele vai cair. Arrastar para a pasta dele o devolve, e arrastar para Favoritos favorita. Um grupo arrastado para outro vira subgrupo. O clique direito tem o mesmo caminho em "Mover para".
- **Ocultar**: o clique direito tira da barra lateral o projeto que você não usa. Ele fica no grupo Ocultos, no fim da lista, e a pesquisa continua achando. Mostrar de novo devolve ao lugar em que estava.
- **Sessões** ganhou o mesmo: grupos e subgrupos de conversas, arrastar e ocultar. A conversa num grupo sai do dia dela, e os grupos das conversas são separados dos grupos dos projetos.
- **Paleta de cores** do GRepos, com 63 cores, nos grupos e no botão Cor da barra das abas.
- O GTerm abre em menos da metade do tempo, e abrir um grupo grande também ficou mais rápido. A primeira abertura depois de atualizar continua mais demorada. A lista de Sessões já vem lida quando você clica nela. O arquivo da atualização ficou cerca de um terço maior.

## 1.0.0.14 — 29/10/2026
- Ao passar de Projetos para Sessões, a primeira favorita já aparece escolhida, com o quadro dela. Sem favoritas, aparece a primeira conversa da lista. Voltar para Projetos traz o projeto que estava à vista.
- Todos os favoritos são numerados, como no cia: a numeração não para mais no 9. O Ctrl+1 a Ctrl+9 continua levando aos nove primeiros.

## 1.0.0.13 — 29/10/2026
- Projetos, Sessões e o + ganharam cor: laranja, roxo e azul. A pílula do modo em uso fica inteira na cor; a outra fica só com o ícone colorido e acende com o mouse em cima.
- Os botões ficaram todos do mesmo tamanho. A barra embaixo das abas agora tem as mesmas pílulas da tela de abrir o Claude, com as mesmas cores e ícones: Retomar em verde e Encerrar o Claude em vermelho. Ela tem a altura da pesquisa ao lado.
- O + ficou redondo, e o botão de retomar a sessão ou a conversa virou uma pílula, como os outros.

## 1.0.0.12 — 28/10/2026
- **Sessões**: o cl agora está no GTerm. Na pílula Sessões, em cima da barra lateral, aparecem as conversas do Claude de todas as pastas: as favoritas em cima e as outras por dia (Hoje, Ontem, Esta semana, Mais antigas). A pesquisa procura pelo título ou pelo projeto.
- Clique numa conversa para ver o branch, quando foi, o tamanho e se está aberta. Retomar (▶, Enter ou duplo clique) abre a conversa na aba do projeto dela, como o cl com o número. Se ela já está numa aba, o GTerm só vai para a aba. Se está aberta em outro terminal, ele pergunta antes.
- A estrela favorita a conversa. As favoritas são as mesmas do cl: favoritar num lado aparece no outro.
- O + ao lado das pílulas abre o Claude sem nenhuma conversa, na sua pasta pessoal, numa aba Claude.
- Escolher um projeto com uma sessão muito grande ficou bem mais rápido: o GTerm lê só o fim do arquivo para saber o que vai retomar.

## 1.0.0.11 — 28/10/2026
- A pesquisa de projetos começa na mesma altura do quadro do terminal, e a barra lateral e o terminal não descem mais quando a primeira aba abre.
- A engrenagem das Preferências foi para o rodapé da barra lateral, ao lado da versão, no azul do ícone do GTerm. A pesquisa ficou com a largura toda.
- Na tela de abrir o Claude, Sessão nova, Pedido…, Pull request e Sem skill viraram pílulas coloridas, cada uma com ícone e cor própria, como no GTest.

## 1.0.0.10 — 28/10/2026
- A estrela dos favoritos fica sempre à vista na barra lateral, cheia e amarela, em qualquer grupo. Nos outros projetos, a estrela vazada continua aparecendo só com o mouse em cima.
- **Mudar a ordem dos favoritos**: arraste o projeto para cima ou para baixo dentro de Favoritos. A ordem nova vale para o Ctrl+1 a Ctrl+9 e para os números do cia. Com o projeto escolhido na barra lateral, Alt+↑ e Alt+↓ fazem o mesmo.
- **Quadro do projeto**: ao escolher um projeto ainda sem o Claude aberto, aparece o mesmo resumo do dashboard do cia: o branch, se há arquivos alterados ou commits a mandar e receber, o último commit, se tem CLAUDE.md, a sessão que vai ser retomada (quando, tamanho e assunto) e a última vez que o projeto foi aberto, com o pedido.

## 1.0.0.9 — 28/10/2026
- **Favoritar pela barra lateral**: passe o mouse sobre um projeto e clique na estrela para pôr nos Favoritos ou tirar de lá, sem abrir o menu. A estrela fica cheia e amarela nos favoritos, em qualquer grupo onde o projeto aparecer.
- O botão do segundo terminal, no canto superior direito, virou uma pílula "Terminal" no estilo das abas. Fica acesa enquanto o segundo terminal está aberto. O botão do painel lateral ficou igual.

## 1.0.0.8 — 28/10/2026
- **O GTerm virou o cia com tela**: a barra lateral é a lista do dashboard do cia, com Favoritos, Recentes e uma pasta por base (DBISAM e Private). Os projetos são as pastas das bases, sem cadastro. Favoritar aqui aparece no cia e vice-versa, e o que o cia abre no Windows Terminal entra nos Recentes sem reabrir o GTerm.
- O Claude abre direto no terminal do projeto, como o cia faria: retoma a última sessão com conversa (com /compact quando ela passa de 300 KB) ou, sem sessão, abre na /delphi-writer (DBISAM) ou no /retomar (Private). O projeto parado mostra o que vai acontecer, e os botões Sessão nova, Pedido…, Pull request e Sem skill.
- Os projetos abertos viram **abas** em cima do terminal, cada uma na cor do projeto, a mesma do GTest. **Ctrl+Tab** passa por elas, e **Ctrl+1** a **Ctrl+9** vão aos favoritos, como os números do cia.
- Embaixo das abas, uma barra com os botões do projeto da frente: Retomar, Sessão nova, Pedido…, Pull request, Sem skill, Cor e Encerrar o Claude. Trocar de aba troca os botões.
- Encerrar o Claude, ou abrir de novo de outro jeito, manda o **/exit** antes, como você faria no terminal. Se ele não sair em alguns segundos, é encerrado à força. A confirmação continua.
- Depois de sair do Claude, Enter no terminal abre de novo retomando a sessão.
- O cursor é sempre a barra, também dentro do Claude, que antes desenhava um bloco.
- Por enquanto todos os projetos abrem na conta padrão do Claude: os grupos e as cores por conta saíram, e o projeto da outra conta (cim) fica para uma próxima versão.

## 1.0.0.7 — 07/10/2026
- **Aviso de atenção**: quando um projeto que não está à vista termina ou para para perguntar algo, o nome dele fica em destaque na barra lateral, o título da janela diz quem espera por você e, com o GTerm atrás de outra janela, o botão dele pisca na barra de tarefas. Dá para desligar em Preferências.
- **Segundo terminal**: o botão novo no canto superior direito abre um shell simples embaixo do terminal do projeto, na mesma pasta, para rodar um build ou um git sem interromper o Claude. Ele é encerrado junto com o terminal do projeto.
- Atalhos: **Ctrl+1** a **Ctrl+9** vão direto ao projeto naquela posição, e **Ctrl+Shift+P** procura o projeto pelo nome.
- Clique direito no título de uma conta: iniciar todos os projetos dela ou encerrar todos os terminais (com confirmação). No menu do projeto, "Mover para cima" e "Mover para baixo" mudam a ordem da lista.
- Avisos do Claude Code: se o GTerm mudou de pasta, os avisos instalados passam a apontar para o lugar novo sozinhos ao abrir. Em Preferências, a conta nessa situação mostra o botão "Corrigir".
- Procurar atualização deixou de falhar quando o GitHub recusa a consulta por excesso de pedidos.
- Projeto aberto com `cim` sem conta informada aparece no grupo da outra conta, quando há só uma além da padrão.

## 1.0.0.6 — 07/10/2026
- Cor por conta do Claude: o título do grupo, o plano e os projetos da conta aparecem na mesma cor, e a pílula com o nome acima do terminal acompanha. A cor de cada conta é escolhida em Preferências, no círculo ao lado dela.
- A cor deixou de ser escolhida projeto a projeto: o campo saiu da tela de editar projeto.

## 1.0.0.5 — 07/10/2026
- Cores no terminal: quando o aplicativo era aberto de dentro de um terminal com o Claude Code, o prompt (oh-my-posh), os scripts de abertura e o próprio Claude saíam sem cor, e isso continuava depois de uma atualização automática. Agora os terminais nascem com as cores normais em qualquer caso. Vale a partir da próxima abertura: se ainda estiver sem cor logo depois de atualizar, feche e abra o aplicativo uma vez.

## 1.0.0.4 — 07/10/2026
- Sem mudanças no aplicativo: versão publicada para conferir a atualização automática corrigida na 1.0.0.3.

## 1.0.0.3 — 07/10/2026
- Atualização: depois de baixar, o aplicativo abre a versão nova e fecha a antiga sozinho. Antes ficava parado em "Baixando… 100%" e era preciso fechar e abrir à mão. Vale a partir da próxima atualização.

## 1.0.0.2 — 07/10/2026
- Barra lateral: o grupo de cada conta do Claude mostra o nome de quem está logado (o e-mail fica na dica) e o plano em destaque. Clicar no título recolhe ou abre os projetos da conta, e a escolha fica guardada.
- Preferências: cada conta aparece com nome, e-mail e plano, além da pasta.
- Rodapé: depois de procurar atualização, um ✓ ao lado da versão diz que você já está na mais recente, no lugar da frase que saía cortada.

## 1.0.0.1 — 07/10/2026
- Primeira versão publicada: vários terminais numa janela só, um por projeto, na barra lateral em vez de abas. Encerrar um terminal, remover um projeto ou fechar a janela com algo rodando sempre pede confirmação.
- Nada roda sozinho ao abrir o aplicativo: cada projeto fica parado até você clicar no **▶** da linha dele.
- Cada projeto tem cor, shell (PowerShell, Prompt de Comando ou Git Bash) e um **comando ao abrir**, digitado sozinho assim que o terminal sobe.
- O ponto ao lado do projeto mostra o estado do terminal: amarelo rodando, piscando quando espera por você, verde quando terminou bem e vermelho quando terminou com erro.
- **Claude Code**: com os avisos instalados em Preferências, o estado passa a ser o que o próprio Claude informa. O menu do projeto abre o Claude em sessão nova, sem skill, num pedido do Mantis ou em pull request, e cada projeto pode usar outra conta.
- Com mais de uma conta do Claude, a barra lateral junta os projetos por conta, com o e-mail e o plano de cada uma.
- **Painel lateral** opcional por projeto, para acompanhar o consumo do Claude ao lado do terminal.
- Preferências: fonte e tamanho do terminal. A fonte padrão é uma Nerd Font, para os ícones do prompt aparecerem.
- O aplicativo procura versão nova uma vez por dia e se atualiza sozinho pelo rodapé da barra lateral.