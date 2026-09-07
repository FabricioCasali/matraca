# Matraca: frame de gravação e movimento

## Duas superfícies, não uma

O frame (HUD) mostra o estado da operação. A moldura identifica a janela de destino. Na aplicação atual, são implementações independentes. A prévia `matraca-gravacao.html` mostra ambos sem alterar janelas reais.

O frame nativo ignora cliques e não toma foco no Windows nem no Mac. O mock não coloca botões dentro dele. Os controles de reprodução ficam fora do cenário, apenas na página de demonstração. A variante já existente no protótipo de ditado mantém seus botões de teste; não deve ser portada como se o overlay nativo recebesse cliques.

## Contrato existente

Mensagem recebida: `{ version: 1, type: "hud.state", payload: { state, title, detail } }`.

Estados: `ready`, `listening`, `thinking`, `writing`, `done`, `error`. `ui.hudReady` é confirmação de inicialização, não estado visual. O estado ocioso normalmente oculta o frame.

Fontes consultadas em `C:\Desenv\particular\ditador`:

- `Matraca.Web/wwwroot/hud.html`, `hud.css`, `hud.js` e `bridge.js`.
- `Matraca.Windows/WindowsHudWindow.cs`.
- `Matraca.Mac/Platform/Web/MacHudWindowController.cs`.
- `WindowsFocusIndicator.cs` e `MacBorderOverlay.cs` para a moldura.

O contrato não contém amostras de áudio ou cronômetro. Barras animadas são um sinal de atividade, não um medidor. A prévia não inventa amplitude real nem tempo de captura. Revisão pode usar `thinking` com título específico; não requer inventar um estado nativo adicional.

## Tokens e eventos de movimento

| Evento | Resposta visual | Duração |
| --- | --- | --- |
| Hover de botão ou seletor | Fundo e borda, sem clarear o texto | `--motion-fast`: 120 ms |
| Pressionar botão | Deslocamento de 1 px, resposta imediata | Enquanto pressionado |
| Abrir tela, painel ou diálogo | Opacidade e deslocamento máximo de 4 px | `--motion-enter`: 180 ms |
| Mostrar frame | Opacidade, 6 px de deslocamento, escala 0.985 → 1 | 180 ms |
| Alterar mensagem do frame | Troca curta de conteúdo, 2 px | `--motion-state`: 160 ms |
| Escutar | Barras decorativas, sem mudança de layout | Ciclo de 700 ms |
| Processar | Rotação contínua, rótulo sempre presente | Ciclo de 1 s |
| Inserir | Seta com movimento único de 2 px | 420 ms |
| Concluir | Símbolo de confirmação discreto | 180 ms |
| Ocultar frame | Opacidade e 4 px para baixo | `--motion-exit`: 140 ms |
| Alternar switch | Deslocamento do indicador | 120 ms |
| Alterar moldura do destino | Borda muda sem pulsar ou piscar | 160 ms |

Curva principal: `--ease-out`, definida no protótipo. Não há animação que impeça um comando de ser processado. Durações visuais não são atrasos artificiais de resposta do aplicativo.

## Sequências da prévia

- Sucesso: ouvindo → processando → inserindo → concluído → oculto.
- Contínuo: três trechos são transcritos e inseridos progressivamente; a sessão permanece ouvindo entre eles e depois do último. “Trecho entregue” não significa fim da sessão.
- Falha: ouvindo → processando → inserindo → erro, que permanece até uma ação.

Os tempos de processamento da demonstração são ilustrativos. A confirmação mantém 1.600 ms antes de ocultar e 900 ms antes de retomar escuta contínua, como os timers encontrados no código.

Diferença proposta: erro de entrega não desaparece automaticamente após 1.600 ms. Fica legível até recuperação ou encerramento explícito. A mensagem remete ao histórico; não promete um botão clicável no frame nativo.

Pronto oculta o frame; a moldura permanece se o destino estiver fixado. A borda tracejada é uma proposta de diferenciação não baseada apenas em cor. Não há pulsação de toda a janela.

## Foco, interrupções e redução de movimento

- Eventos do frame alteram apenas frame, moldura e controles de demonstração; não substituem o editor de exemplo.
- Uma geração de execução invalida timers antigos ao recomeçar, parar ou sair da demonstração.
- O frame comum do protótipo não reinicia sua entrada quando recebe uma renderização sem mudança de estado.
- Trocas de tema mantêm os dados e a seleção do campo em edição quando possível.
- `prefers-reduced-motion: reduce` remove movimentos, rotações e transições. Ícone, texto e cor continuam distinguindo estados.
- O botão de teste “movimento reduzido” apenas reduz movimentos na prévia; não força animação quando o sistema já pede redução.

## Integração nativa pendente

A janela atual mede 430 × 92; o mock mantém essa referência, com título em uma linha e prévia em até duas linhas. O restante é truncado somente visualmente. Fonte e padding da variante nativa acomodam duas linhas no cartão de 72 px. Confirmar DPI e fontes reais antes de portar.

Windows usa área útil e DPI. Mac usa o frame completo da tela; revisar espaço do Dock. O Mac também usa a janela ativa como referência do HUD, que pode divergir do destino fixado. Validar posicionamento nas duas plataformas.

Para animar a saída, o host precisa manter a janela visível pelos 140 ms correspondentes; CSS não anima uma janela nativa já escondida. Também é necessário invalidar timers antigos no início de novas entregas, especialmente no Mac. Nada disso foi alterado nos fontes nativos nesta etapa.

## Textos longos e entrega incremental

O campo de texto da demonstração aceita prompts longos e o botão de exemplo preenche um texto extenso. Não há corte na string original, na inserção ou na cópia do histórico. O frame limita apenas a apresentação usando reticências e duas linhas; o texto completo permanece no destino e no histórico, quando habilitado.

No cenário contínuo, a amostra é particionada em três blocos de caracteres Unicode exclusivamente para demonstrar entregas. Na aplicação, os blocos vêm das pausas e do limite de frase, não desse particionamento visual. Não se promete transcrição palavra a palavra antes da confirmação do motor.

Cada trecho confirmado é acrescentado uma única vez. A entrada do histórico dessa demonstração é atualizada com o acumulado. Parar invalida entregas pendentes, mas mantém o que já foi inserido. O editor de exemplo não é recriado durante a entrega; seu conteúdo e seleção são preservados.

Mensagens “Ouvindo · transcrevendo trecho”, “Ouvindo · inserindo trecho” e “Trecho entregue · ainda ouvindo” deixam claro que o processamento de um trecho não encerrou a captura contínua. As durações ainda são ilustrativas, e nenhum áudio real é capturado.
