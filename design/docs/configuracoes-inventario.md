# Matraca: inventário e cobertura das configurações

## Fonte e limites

Inventário por leitura de `C:\Desenv\particular\ditador`, sem ler configurações pessoais, credenciais, histórico ou logs. Não houve execução do aplicativo nativo.

Fontes principais:

- `Matraca.Core/RawConfig.cs`, linhas 5–42: os 38 campos persistíveis.
- `Matraca.Core/Config.cs`, linhas 177–305: defaults, normalização e limites.
- `Matraca.Web/wwwroot/index.html`, linhas 94–151, e `app.js`: controles e dependências existentes.
- `Matraca.Windows/WindowsWebBridge.cs` e `Matraca.Mac/Platform/Web/MacWebBridge.cs`: capacidades, modelos, permissões e mensagens.
- `Matraca.Core/ModelDownloader.cs`: catálogo de modelos.

Os defaults são do código, não da configuração instalada nem do JSON distribuído. O protótipo contém 39 definições em `CONFIG_FIELDS`, verificadas contra a lista de campos inventariada nos testes.

## Correspondência completa

| Campo existente | Local no protótipo | Observação |
| --- | --- | --- |
| `hotkey` | Ditado e atalhos | String; default F15. Captura global real permanece no nativo. |
| `pinHotkey` | Ditado e atalhos | Vazio desliga; não pode repetir o atalho de ditado. |
| `mode` | Ditado e atalhos | toggle, hold, live, push; default do código live. |
| `language` | Ditado e atalhos | Texto livre; pt, en, es e auto são os exemplos da UI existente. |
| `uiLanguage` | Aparência | `system`, `pt-BR` ou `en-US`; instalação nova usa `system`. Ausência em configuração legada preserva `pt-BR`. Não altera `language`. |
| `autoEnter` | Ditado e atalhos | Desligado por padrão; aviso sobre envio antes de revisão. |
| `modelPath` | Reconhecimento | Caminho local livre, diferente do catálogo de download. |
| `gpu` | Reconhecimento | auto, gpu, cpu; reinício necessário para aplicar no processo. |
| `idleUnloadMinutes` | Reconhecimento | 0–240 min; default 5; zero mantém carregado. |
| `vocabulary` | Reconhecimento | Lista sem duplicados; prompt gerado limitado a 800 caracteres no código. |
| `inputDevice` | Áudio / Microfone | Seletor compartilhado; o host deve resolver o nome real da entrada, inclusive quando usa o padrão do sistema. |
| `vadThreshold` | Interno / legado | Sem controle genérico na nova interface. O campo permanece inventariado porque existe no backend. |
| `micSensitivity` | Áudio / Microfone | Espectro com barra de ajuste; mapa gerenciado automaticamente por dispositivo, sem editor textual. |
| `silenceMs` | Áudio | 200–5000 ms; default 450. |
| `phraseMaxSeconds` | Áudio | 2–20 s; default 6. |
| `beep` | Áudio | Ligado por padrão. |
| `beepVolume` | Áudio | 0–1; default 0.8. |
| `startSound` | Áudio | Caminho; vazio usa padrão. |
| `stopSound` | Áudio | Caminho; vazio usa padrão. |
| `pasteMethod` | Entrega | unicode ou clipboard. Não governa toda entrega com alvo fixado. |
| `pinDelivery` | Entrega | focus ou nofocus; fallback com foco quando necessário. |
| `focusBorder` | Entrega | Ligado por padrão. |
| `focusBorderThickness` | Entrega | 1–40 px; default 4. |
| `focusBorderOpacity` | Entrega | 0.1–1; default 0.9. |
| `focusBorderColor` | Entrega | Cor existente para pronto; independente do tema. |
| `focusBorderColorBusy` | Entrega | Cor existente para ocupado; independente do tema. |
| `focusBorderColorPinned` | Entrega | Cor existente para fixado; independente do tema. |
| `history` | Histórico | Ligado por padrão; a prévia guarda apenas durante a sessão. |
| `historyMaxItems` | Histórico | 1–5000; default 100. |
| `postProcess` | Revisão por IA | Desligado por padrão. Ativação simulada exige preparação. |
| `postProcessProvider` | Revisão por IA | Anthropic, DeepSeek e API compatível com OpenAI. |
| `postProcessEndpoint` | Revisão por IA / API compatível | HTTPS remoto ou HTTP local; sem credenciais embutidas na URL. |
| `postProcessModel` | Revisão por IA | Nome livre; não há catálogo OpenAI no código. |
| `postProcessReasoning` | Revisão por IA / DeepSeek | off, low, high, max; escolha explícita. |
| `postProcessApiKey` | Revisão por IA / Anthropic | Campo visual desabilitado; somente credencial fictícia. |
| `postProcessOpenAiApiKey` | Revisão por IA / API compatível | Mesmo controle, chave específica do provedor. |
| `postProcessDeepSeekApiKey` | Revisão por IA / DeepSeek | Mesmo controle, chave específica do provedor. |
| `postProcessPrompt` | Revisão por IA | Vazio usa a instrução padrão do código. |
| `postProcessTimeoutMs` | Revisão por IA | 1000–60000 ms; default 8000; fallback para original. |

## Operações além dos campos

Reconhecimento inclui download simulado de Base, Small e Large v3 turbo e cancelamento. Os arquivos existentes no catálogo são `ggml-base.bin`, `ggml-small.bin` e `ggml-large-v3-turbo.bin`. Cancelar download já possui handler no nativo, embora não estivesse exposto no frontend examinado.

Procurar arquivo e ouvir som são operações da bridge, não campos de configuração. O protótipo usa caminhos fictícios e avisos, sem ler arquivos ou reproduzir som.

A captura de atalho e permissões reais continua sendo responsabilidade do aplicativo. O primeiro uso já simula esse processo; o formulário completo permite inspecionar os parâmetros. O protótipo não reproduz o parser de teclado integral nem o mecanismo de captura global.

## Revisão: diferenças por provedor

- Anthropic: SDK próprio, sem endpoint customizável; o código usa `claude-opus-5` como default.
- DeepSeek: endpoint fixo; sugestões existentes `deepseek-v4-flash` e `deepseek-v4-pro`; modelo e raciocínio precisam ser escolhidos.
- API compatível: endpoint completo configurável; vazio representa o endpoint padrão da OpenAI; nenhum modelo novo foi inventado.
- Esses nomes foram encontrados no repositório, não validados em serviços externos.
- Trocar de provedor desliga a revisão, redefine modelo/raciocínio e remove a credencial anterior no fluxo atual. A prévia replica esse efeito somente em referências fictícias e avisa antes da seleção.
- Fallback por variáveis de ambiente no código: `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `DEEPSEEK_API_KEY`. Não são novos campos da interface.

## Windows e Mac

| Capacidade | Windows | Mac |
| --- | --- | --- |
| Editar campos | Sim | Sim, condicionado ao runtime disponível |
| Procurar arquivo | Existe; operação simulada aqui | Ainda sem suporte na bridge |
| Ouvir prévia de som | Existe; operação simulada aqui | Ainda sem suporte na bridge |
| Sons durante ditado | Existe | Existe, apesar da ausência do botão de prévia |
| Aceleração | Vulkan/CPU | Backend nativo GPU/CPU |
| Permissão de acessibilidade | Não é a mesma barreira de inicialização | Pode exigir conceder e reiniciar |
| Entrega sem foco | Depende do controle de destino | Depende de acessibilidade e pode cair em entrega com foco |

O Mac usa um subconjunto das teclas do parser compartilhado. `Win` representa Command na configuração existente; não foi inventado um alias `Cmd`. Cor compartilhada deve usar `#RRGGBB`.

## O que não virou configuração inventada

Iniciar com Windows e uiAccess são opções de instalação, não campos de `RawConfig`. Flags de teste da linha de comando, estados de permissão, destino atual, consumo de IA, lista de dispositivos e progresso do download também não são preferências.

Tema e família de cor são propostas novas já aprovadas no protótipo. `uiLanguage` é a preferência aprovada para o idioma da interface e fica separada de `language`, que continua no grupo de reconhecimento. A interface oferece pt-BR e en-US; em `system`, locale `pt-*` resolve pt-BR e qualquer outro resolve en-US.

## Internacionalização da interface (MT-038)

`uiLanguage` é persistido junto das preferências de aparência e aceita somente os valores canônicos `system`, `pt-BR` e `en-US`. O valor efetivo é resolvido assim:

| Preferência | Locale do sistema | Idioma efetivo |
| --- | --- | --- |
| `pt-BR` | qualquer | `pt-BR` |
| `en-US` | qualquer | `en-US` |
| `system` | começa por `pt` | `pt-BR` |
| `system` | qualquer outro, inclusive não suportado | `en-US` |

Em instalação nova, a ausência de configuração inicial significa `system`. Na leitura de configuração legada sem `uiLanguage`, o fallback compatível é `pt-BR`, sem regravar ou reinterpretar `language`. A troca é a quente, permanece em Configurações > Aparência, conserva a seção e o foco, e não modifica tema, família de cor, reconhecimento, histórico, consumo ou ditados.

## Diferenças deliberadas e riscos para a integração

- O protótipo cobre os campos e suas interações de configuração; não executa todos os comportamentos nativos. O ditado demonstrativo permanece por alternância, mesmo ao inspecionar outro modo no formulário.
- Configurações completas ficam somente em memória. Aparência e atalho da prévia mantêm a persistência anterior. Nenhuma chave real pode ser digitada ou armazenada.
- Áudio e Microfone agora reutilizam o mesmo espectro de 48 bandas e barra de ajuste. A barra usa o intervalo nativo 0.001–0.5, mas explica sensibilidade em linguagem comum. Os dados espectrais permanecem simulados.
- O editor textual de `micSensitivity` foi removido por orientação do usuário. O protótipo cria um registro próprio para cada dispositivo, restaura seu valor ao retornar e redefine somente o dispositivo ativo. Um dispositivo ainda não ajustado começa em 0.012; isso não representa calibração automática por áudio.
- A associação deve usar a identidade real resolvida do dispositivo, nunca um registro genérico chamado “padrão do sistema”. A prévia usa nomes de dispositivos fictícios. Registros ficam em memória nesta sessão; a persistência nativa ainda será integrada.
- A remoção explícita de credencial fictícia continua sendo uma melhoria proposta sobre o controle atual.
- O backend devolve valores brutos e limita números na aplicação. A proposta valida intervalos antes de aceitar a edição, preservando o último valor válido.
- Para API compatível, o código normaliza modelo vazio para um nome Anthropic. A proposta exige modelo explícito antes de ativar, em vez de inventar um modelo OpenAI. Essa divergência precisa de correção ou decisão na integração.
- Não há restauração global para null nem editor de flags de diagnóstico. Não são requisitos novos inferidos do inventário.

## Verificação

`tests/matraca-prototype.test.cjs` compara os 39 nomes inventariados com `CONFIG_FIELDS`; verifica 38 correspondências visuais e a exclusão deliberada de `vadThreshold`, agora interno. Também testa a resolução e a migração de `uiLanguage`, a independência de `language`, a independência e restauração por dispositivo, espectro de 48 bandas, provedores, limites e marcação em dez temas. É um teste com DOM simulado, não prova de integração nativa nem auditoria completa de acessibilidade.
