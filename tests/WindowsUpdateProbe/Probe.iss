; Somente prova MT-038. Valores obrigatorios fornecidos pelo harness.
[Setup]
AppId=MT038-Isolated-Probe-{#RunId}
AppName=MT038 Isolated Update Probe
AppVersion={#ProbeVersion}
DefaultDirName={#InstallDir}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
CloseApplications=no
RestartApplications=no
CreateAppDir=yes
OutputDir={#PackageDir}
OutputBaseFilename=MT038-Probe-{#ProbeVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), '{#InstallDir}') <> 0 then
    Result := 'O probe so pode instalar no diretorio isolado compilado.';
end;
