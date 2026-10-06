## 1.0.0.1 — 06/10/2026
- Primeira versão publicada: vários terminais numa janela só, um por projeto, na barra lateral em vez de abas. Encerrar um terminal, remover um projeto ou fechar a janela com algo rodando sempre pede confirmação.
- Cada projeto tem cor, shell (PowerShell, Prompt de Comando ou Git Bash) e um **comando ao abrir**, digitado sozinho assim que o terminal sobe.
- O ponto ao lado do projeto mostra o estado do terminal: amarelo rodando, piscando quando espera por você, verde quando terminou bem e vermelho quando terminou com erro.
- **Claude Code**: com os avisos instalados em Preferências, o estado passa a ser o que o próprio Claude informa. O menu do projeto abre o Claude em sessão nova, sem skill, num pedido do Mantis ou em pull request, e cada projeto pode usar outra conta.
- **Painel lateral** opcional por projeto, para acompanhar o consumo do Claude ao lado do terminal.
- Preferências: fonte e tamanho do terminal. A fonte padrão é uma Nerd Font, para os ícones do prompt aparecerem.
- O aplicativo procura versão nova uma vez por dia e se atualiza sozinho pelo rodapé da barra lateral.