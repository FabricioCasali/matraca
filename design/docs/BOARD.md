# Quadro do protótipo Matraca

Referência de adoção importada do OpenDesign. A execução e o estado operacional do projeto são mantidos no `docs/BOARD.md` da raiz, conforme AGENTS.md. Não usar esta lista como um segundo backlog operacional.

## A fazer

- **MA-003** **Validar o design no aplicativo nativo**: conferir Windows/macOS, permissões, microfone, atalho, HUD e escala de tela antes de portar. Importante, G.
- **MA-004** **Aplicar o design system consolidado**: especificação 1.0.0 pronta para orientar implementação; migrar literais/aliases, propagar marca escolhida e fechar lacunas GAP-001 dos mocks. Referência: `design-system.md`. Melhoria, M.
- **MA-005** **Validar integração das configurações**: protótipo cobre os 38 campos; resolver divergências de modelo por provedor, valores brutos/efetivos, sensibilidade simples/limiar e operações indisponíveis no Mac antes de portar. Referência: `configuracoes-inventario.md`. Importante, G.
- **MA-006** **Integrar consumo de revisão**: preservar custos ausentes no acumulado, origem dos dados e independência do histórico; avaliar contrato necessário para indicar cobertura parcial. Referência: `consumo-revisao.md`. Importante, M.
- **MA-007** **Portar frame e movimento**: validar tamanho/DPI, posição acima de Dock/barra, saída antes de ocultar janela, cancelamento de timers e erro persistente no Windows/Mac. Mock em `../matraca-gravacao.html`; contrato em `movimento-e-gravacao.md`. Importante, M.
