# Icones nativos Windows

Implementacao de DS 1.0.1: BRD-001, FND-001, CMP-003/005, MOT-001 e A11Y-001.
Nenhum desenho novo: resvg 2.6.2 rasteriza os SVGs estaticos oficiais de
`design/assets/brand/`, sem alterar geometria, cores ou marcadores.

```powershell
npm ci --prefix tools/icons --ignore-scripts
pwsh -File tools/make-icons.ps1
pwsh -File tools/make-icons.ps1 -Check
node tests/windows-icons.test.cjs
pwsh -File tests/windows-icons-native.ps1 -PreviewPath <caminho-absoluto.png>
```

- `Matraca.Windows/app.ico`: olive-light oficial, identidade fixa do EXE,
  instalador, desinstalador e atalhos. Nao depende da preferencia da maquina de build.
- `Matraca.Windows/Icons/app-*.ico`: cinco familias e dois temas para janela/Alt+Tab/taskbar;
  atualizacao a quente por config, mudanca de tema do sistema e DPI.
- `Matraca.Windows/Icons/{idle|recording|busy|error}-{light|dark}.ico`: bandeja
  monocromatica, simbolo/circulo/ampulheta/exclamacao e tooltip textual existente.
  Usa o tema da barra do Windows (`SystemUsesLightTheme`), nao o tema do painel:
  o Windows permite fundo de barra diferente do fundo dos aplicativos.
- Tema `system` do painel consulta `AppsUseLightTheme`; claro/escuro explicitos
  e familia persistida permanecem intactos. Nao altera preferencias do Windows.
- Estados sao estaticos, inclusive com movimento reduzido. Nao usa animacoes SVG no shell.
- Cache de HICON por arquivo/tamanho pertence a `WindowsIconSet`. Janela e bandeja
  emprestam handles; sao destruidas antes de `DestroyIcon`, sem `LR_SHARED`.

ICO tem PNGs RGBA em 16/20/24/32/40/48/64/128/256 px, formato suportado desde Vista.
Build/publish .NET nao precisa de Node: os ICOs derivados sao versionados.
O instalador ja aponta para `app.ico` e os atalhos para o EXE, sem caminhos alternativos.

Referencias: [resvg](https://github.com/thx/resvg-js),
[ICO com PNG](https://devblogs.microsoft.com/oldnewthing/20101022-00/?p=12473),
[WM_SETICON](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-seticon).

Limites: os discos dos marcadores conservam a superficie dos SVGs oficiais;
contraste sobre transparencia/alto contraste personalizado, Explorer cache,
trocas reais de tema/DPI e acessibilidade precisam de prova assistida.
ICNS e barra de menus macOS nao foram alterados. Estado executavel em `docs/BOARD.md`.
