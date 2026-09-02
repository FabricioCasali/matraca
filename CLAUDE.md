# Matraca — as leis do projeto

App de bandeja que transcreve voz com Whisper **local** e entrega o texto na janela em
foco. Nasceu para ditar prompts no Claude Code, e serve qualquer lugar com cursor de
texto. Hoje roda no Windows; está virando 2.0, com macOS e uma UI só para os dois
sistemas — o desenho está em [`docs/PLANO-2.0.md`](docs/PLANO-2.0.md), o estado em
[`docs/BOARD.md`](docs/BOARD.md).

Este arquivo é **lei**. Quem julga desvio é o `regente`, e ele julga contra o que está
escrito aqui.

## As leis

**1. O áudio nunca sai da máquina.** Nunca é gravado em disco, nunca é transmitido. A
única coisa que sai é *texto*, e só pelo pós-processamento com Claude — que é opcional,
desligado por padrão, e quando falha devolve a transcrição original em vez de perder o
ditado.

**2. Zero telemetria.** Sem analytics, sem verificação de atualização, sem ping de
inicialização. O app não conta para ninguém que existe.

**3. Config aplica a quente.** Pedir para reiniciar é falha de design — ninguém tem
paciência de reiniciar entre duas tentativas de ajuste. Exceção única e conhecida: a
troca GPU↔CPU, porque o runtime nativo é fixado por processo; só nesse caso o app
pergunta.

**4. Nada rouba foco enquanto o usuário dita.** HUD, moldura de foco e avisos são
não-ativáveis e ignoram clique. Uma janela nossa que recebe foco no meio de um ditado
manda o texto para o lugar errado.

**5. A injeção nunca roda na thread do hook.** Digitar na thread que hospeda o hook de
teclado trava o hook: no Windows estoura o `LowLevelHooksTimeout` e o sistema **descarta
caracteres**; no macOS o sistema **desabilita o event tap** e o app fica mudo. O sintoma
é texto sem espaços e cortado no meio. E o app reconhece os eventos que ele mesmo injeta
pela **marca da fonte** (`dwExtraInfo` / `kCGEventSourceUserData`) — nunca pelo flag de
"é sintético", porque remapeadores como o AutoHotkey também injetam e o atalho precisa
continuar valendo para eles.

**6. Um só `appsettings.json` serve os dois sistemas.** O arquivo guarda o **nome**
canônico da tecla (`F15`, `Ctrl+Alt+X`); traduzir para código nativo é trabalho da
fronteira, não do Core. O mesmo vale para caminhos: o Core resolve, a plataforma
responde onde fica.

**7. Todo controle mostra o efeito do que ele muda.** Um número sem referência não é
controle, é adivinhação — o usuário não tem como saber o que é um bom valor. O padrão
da casa é a barra de nível do microfone: ela fica verde exatamente quando o algoritmo
contaria como fala, e o limiar é uma marca arrastável sobre ela.

**8. Um tipo por arquivo.** Classe, record, struct, enum ou interface — nome do arquivo
igual ao nome do tipo, organização por pasta/namespace. Nada de tipo pequeno pegando
carona no arquivo de outro.

**9. Nenhuma mudança é efetivada sem plano aprovado.** O `arquiteto` desenha, o
`regente` leva ao Fabricio, e só então alguém escreve.

**10. `git push` só com pedido explícito.** Commit local a cada fatia verde, sem
perguntar. Publicar é decisão dele — o repositório é público.

## Como se trabalha aqui

**Build cruzado é obrigatório.** Este Mac compila os dois lados; nenhuma fase pode
quebrar o Windows sem alguém ver:

```bash
dotnet build Matraca.sln -p:EnableWindowsTargeting=true   # os dois lados, a partir do Mac
dotnet test Matraca.sln                                   # a rede do Core
```

**Ninguém aqui roda o Matraca no Windows.** O Mac roda de verdade; o Windows depende do
CI e do teste manual do Fabricio. Não finja cobertura que a mesa não tem.

**O que é estado vai para `docs/BOARD.md`** (cartão `MT-###`, e cartão fechado *sai* do
arquivo). **O que sobrevive ao projeto** vira proposta em
`$AMBIENTE_RAIZ/05-conhecimento/_propostas/` — a equipe não escreve no vault direto.
Fora disso, não escreve.

## A equipe

Definida em [`.claude/agents/`](.claude/agents/). O portão é garantido pela lista de
ferramentas de cada papel, não pela boa vontade: quem desenha não tem como escrever.

| papel | o que faz |
|---|---|
| `regente` | mantém a visão, julga se está pronto, é a única voz que fala com o Fabricio |
| `arquiteto` | diz o que e como; dono do contrato entre as peças |
| `desenvolvedor` | as mãos |
| `provador` | prova que roda |
| `estilista` | o padrão visual, no OpenDesign |
| `escrivão` | registra o estado |
