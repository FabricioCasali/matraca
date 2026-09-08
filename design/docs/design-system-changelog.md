# Histórico de versões do Matraca Design System

## Nao lancado — Navegacao e janela MT-037

- CMP-004: navegacao principal fixa com rolagem independente em viewport baixo; somente conteudo acompanha a rolagem principal. Composicao compacta existente preservada.
- FND-003: tokens `window` registram referencia inicial 1200 x 820 logica, limitada pela area util nativa; nao altera tamanho escolhido na sessao.
- Mock compartilhado e regressao de layout reconciliados. Abas continuam podendo quebrar linha, conforme escopo aprovado; nenhuma mudanca de tema, paleta ou movimento.

## Nao lancado — Barra de titulo MT-033/034/035

- CMP-004 explicita barra fora da rolagem e duplo clique separado do arraste no Windows.
- Acesso a Aparencia somente em Configuracoes, conforme pedido; mock e testes reconciliados.
- Tokens 1.0.1 preservados: nenhuma nova cor, medida visual ou animacao.

## Não lançado — Assets da marca 03A

- Confirmação explícita da variante 03A, “m que fala”.
- Exportação de 24 SVGs: símbolos, dez ícones de tema, estados de bandeja e animações aprovadas.
- Gerador reproduzível, conferência de arquivos e inclusão dos assets no manifesto.
- Sem alteração da aplicação nativa; commit e push não executados por esta exportação.

## 1.0.1 — Adoção no repositório

- Destino oficial definido como `design/` no repositório do Matraca, sem mudança nas regras visuais.
- README de entrada, referências relativas e instruções no AGENTS.md da raiz.
- Mocks e testes preservados; estudos antigos em `mt-005/` não alterados.
- Manifesto inclui o README e identifica o repositório como local de manutenção.
- Atributos Git locais preservam LF para verificação de integridade entre plataformas.
- Sem integração nativa ou push nesta entrega. A cópia no OpenDesign não sincroniza automaticamente.

## 1.0.0 — Consolidação inicial

- Especificação passa a ser a referência principal; mocks são exemplos com lacunas explícitas.
- Regras estáveis DS, FND, BRD, CMP, MOT, A11Y, REF, GAP, VER e ADOPT para referência nas tarefas.
- Tokens de cores, famílias, geometria, tipografia e movimento em JSON.
- Estado mais recente da animação da marca: escuta de 1400 ms, sem lápis e sem prolongamento extra; carregamento preservado em 2400 ms.
- Contratos de microfone, histórico, consumo, revisão e frame ligados aos documentos existentes.
- Manifesto de integridade e teste de referências/pacote.

A versão 1.0.0 foi consolidada antes da escolha do destino. Essa decisão foi registrada na versão 1.0.1 acima. Nenhuma versão deste documento implica publicação remota ou integração automática no aplicativo.
