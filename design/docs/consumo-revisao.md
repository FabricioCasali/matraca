# Histórico e consumo da revisão

## Fonte conferida

Leitura do código atual em `C:\Desenv\particular\ditador`, sem consultar históricos reais, credenciais ou contas.

- `Matraca.Core/AiCostEstimator.cs`: tabela e cálculo de custo, versão 2026-09-06.
- `Matraca.Core/TextReviewUsage.cs`: consumo da chamada.
- `Matraca.Core/AiUsageLedger.cs`: acumulador independente do histórico.
- `Matraca.Windows/WindowsWebBridge.cs`: `history.list`, `ai.usage.get` e `deepseek.balance.get`.
- `Matraca.Mac/Platform/Web/MacWebBridge.cs`: contratos equivalentes.
- `Matraca.Web/wwwroot/app.js`: painel por ditado, acumulado e consulta de saldo.

## O que foi trazido para o protótipo

Cada ditado pode conter `reviewUsage`, com provider, model, promptTokens, promptCacheHitTokens, promptCacheMissTokens, completionTokens, reasoningTokens, totalTokens, estimatedCostUsd e pricingVersion. O detalhe mostra esses contadores, provedor, modelo e estimativa em dólares. Raciocínio está incluído na saída; não é cobrado ou somado novamente na apresentação.

O custo por chamada usa o valor disponível no registro. Para gerar exemplos, a função `estimateReviewCost()` reproduz a fórmula encontrada no código, incluindo faixa de pico UTC, cache e entrada sem categoria. Não consulta tabela externa nem atualiza preços.

Somente os dois modelos DeepSeek previstos no estimador recebem preço nos exemplos. Outros provedores/modelos mostram “Não calculado”. Ausência de `reviewUsage` mostra “Consumo não informado”, sem concluir que a revisão foi gratuita ou sequer que não aconteceu.

O acumulado usa uma coleção separada. Apagar um ditado, limpar o histórico, limitar a lista ou desligar o armazenamento de textos não apaga consumo já registrado. Copiar, reinserir texto e repetir somente a entrega não criam uma nova chamada de IA.

## Melhorias propostas, além da UI atual

- Resumo de consumo também no histórico, filtrável por provedor.
- Detalhe e seleção acompanham os filtros, evitando mostrar um ditado escondido pela busca.
- Custos ausentes não aparecem como zero. Um acumulado incompleto é um “subtotal estimado conhecido”.
- A entrada sem cache aparece explicitamente no detalhe.
- Formatação de números em pt-BR, mantendo moeda USD.

Essas melhorias não devem ser confundidas com capacidades adicionais já disponíveis no endpoint nativo. O acumulado atual soma custo ausente como zero e não informa a quantidade de chamadas sem preço. Para reproduzir a indicação de cobertura no aplicativo, o contrato agregado precisa preservar essa informação. Não é seguro inferir cobertura completa usando somente os ditados restantes, porque podem ter sido apagados.

O caminho Anthropic atual devolve consumo nulo. Timeout ou falha também podem não trazer consumo, mesmo que tenha ocorrido custo remoto. “Chamadas com consumo informado” não significa total de tentativas nem número de revisões bem-sucedidas.

## Exemplos e privacidade

`matraca-historico.html` abre uma amostra de quatro textos com três registros de consumo ilustrativos. Nenhum valor é uma cobrança real. As duas estimativas DeepSeek usam a faixa fora de pico do domingo 2026-09-06; o terceiro exemplo tem tokens, mas não preço.

Durante a navegação comum, consumo sintético é associado aos exemplos de revisão suportados. Tudo fica em memória nesta sessão. Nenhuma requisição, leitura de conta, gravação de credencial ou captura de áudio ocorre.

O saldo oficial DeepSeek foi identificado no código, mas não foi reproduzido nesta alteração: saldo da conta é diferente de custo local estimado e exigiria uma consulta real. Não exibimos saldo fictício como oficial.

## Verificação

Testes verificam precificação em pico/fora de pico, custo nulo versus zero, seleção por provedor, ausência de dupla contagem de raciocínio, consumo independente de apagar textos e preservação dos temas. São testes de lógica e marcação; não exercitam os serviços nativos.
