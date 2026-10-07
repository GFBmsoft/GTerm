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