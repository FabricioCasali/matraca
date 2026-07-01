; Instalador do Matraca (Inno Setup 6+).
; Build: installer\build-installer.ps1 (publica o app e compila este script).
;
; O exe existe em DUAS variantes publicadas (o manifest e' embutido no .exe):
;   publish\standard\Matraca.exe  -> app.manifest (sem uiAccess)
;   publish\uiaccess\Matraca.exe  -> app.uiaccess.manifest (uiAccess=true)
; A task "uiaccess" decide qual instalar; com ela marcada, enable-uiaccess.ps1
; cria/confia um certificado local e assina o exe (requisito do Windows p/ uiAccess).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Matraca"
#define MyAppExeName "Matraca.exe"

[Setup]
AppId={{8B1F4C7A-3D92-4E5B-9A67-2C0F51D3B8E4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Fabricio Casali
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=matraca-setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; mesmo nome criado em Program.Main: o setup pede p/ fechar o app se estiver rodando
AppMutex=Global\MatracaAppMutex
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o {#MyAppName} junto com o Windows"; Flags: unchecked
Name: "uiaccess"; Description: "Ditar tambem em janelas elevadas (Admin) — cria e confia um certificado local nesta maquina p/ assinar o app"; Flags: unchecked

[Files]
Source: "publish\standard\*"; DestDir: "{app}"; Excludes: "{#MyAppExeName}"; Flags: recursesubdirs ignoreversion
Source: "publish\standard\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion; Tasks: not uiaccess
Source: "publish\uiaccess\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion; Tasks: uiaccess
Source: "enable-uiaccess.ps1"; DestDir: "{app}"; Flags: ignoreversion; Tasks: uiaccess

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\enable-uiaccess.ps1"" -ExePath ""{app}\{#MyAppExeName}"""; \
  Flags: runhidden waituntilterminated; StatusMsg: "Assinando o executavel (uiAccess)..."; Tasks: uiaccess
; shellexec obrigatorio: exe com uiAccess=true nao inicia via CreateProcess (erro 740)
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o {#MyAppName} agora"; Flags: shellexec nowait postinstall skipifsilent
