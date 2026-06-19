; =============================================================================
;  IfcLite Importer for Rhino - Inno Setup installer script
; =============================================================================
;
;  This installer is a thin convenience wrapper around the Rhino Yak package
;  (.yak) produced by the project's build. It copies the .yak next to a bundled
;  copy of Rhino's `yak.exe` and then asks `yak` to install the package into the
;  current user's Rhino 8 package folder.
;
;  NOTE FOR END USERS
;  ------------------
;  You do NOT need this installer to use the plug-in. You can instead:
;    * drag the .yak file onto an open Rhino 8 window, or
;    * open Rhino's Package Manager (the `PackageManager` command) and search
;      for "ifcliteimporter".
;  This installer simply automates the same `yak install` step for users who
;  prefer a classic Windows setup .exe.
;
;  BUILDING THIS INSTALLER
;  -----------------------
;  Compile with the Inno Setup command-line compiler (ISCC.exe), passing the
;  version and the path to the freshly built .yak, e.g.:
;
;    ISCC.exe installer\IfcLiteImporter.iss ^
;        /DAppVersion=0.1.0 ^
;        /DYakFile="..\dist\pkg\ifcliteimporter-0.1.0-rh8-win.yak"
;
;  Both defines are optional; sane defaults are provided below so the script
;  still compiles for local experimentation.
; =============================================================================

; ---------------------------------------------------------------------------
;  Preprocessor defines (overridable from the ISCC.exe command line).
; ---------------------------------------------------------------------------

; Version string shown in Add/Remove Programs and the wizard.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

; Path to the .yak package to bundle. Relative paths are resolved against this
; .iss file's directory. The default matches the staged package the Release
; workflow produces under dist\pkg, but you can point it anywhere.
#ifndef YakFile
  #define YakFile "..\dist\pkg\ifcliteimporter-" + AppVersion + "-rh8-win.yak"
#endif

; Derive just the file name of the .yak so we can reference it after install.
#define YakFileName ExtractFileName(YakFile)

; Optional: a copy of Rhino's yak.exe to bundle. If present next to this script
; (or staged there by CI) it is shipped so the install step works even when
; Rhino is installed in a non-standard location. This is resolved at compile
; time; if the file is missing it is simply omitted from the package.
#define LocalYakExe "..\dist\pkg\yak.exe"

[Setup]
; Human-readable application identity.
AppName=IfcLite Importer for Rhino
AppPublisher=LINK Arkitektur
AppVersion={#AppVersion}
AppPublisherURL=https://github.com/linkarkitektur/Rhino-IfcLiteImporter

; Install per-user under %LOCALAPPDATA% so no administrator rights are needed.
DefaultDirName={localappdata}\IfcLiteImporter
DisableProgramGroupPage=yes

; Output: a single self-contained setup .exe.
OutputBaseFilename=IfcLiteImporter-Setup
OutputDir=Output

; Compression.
Compression=lzma2
SolidCompression=yes

; This installer only touches the per-user Rhino package folder, so it must not
; require elevation.
PrivilegesRequired=lowest

; Cosmetic.
WizardStyle=modern

[Files]
; Bundle the Yak package itself into the install directory.
Source: "{#YakFile}"; DestDir: "{app}"; Flags: ignoreversion

; Optionally bundle a copy of yak.exe. `skipifsourcedoesntexist` keeps the
; compile working when no local yak.exe has been staged; in that case the
; install step below falls back to the yak.exe shipped with Rhino 8.
Source: "{#LocalYakExe}"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Run]
; ---------------------------------------------------------------------------
;  After copying files, install the package with Rhino's Yak CLI.
;
;  We pass the bundled .yak file directly to `yak install`, which installs a
;  package straight from a local archive (no package server round-trip). Run
;  hidden so the user is not shown a console window.
;
;  Two candidate yak.exe locations are tried, in order:
;    1. The copy we bundled (if any) in {app}.
;    2. The yak.exe that ships inside the Rhino 8 installation.
;  Each entry is guarded with a Check so only the available one runs. As an
;  alternative to installing from the file, you could install by name from the
;  public server with:  yak install ifcliteimporter
; ---------------------------------------------------------------------------

; 1) Preferred: use the yak.exe we bundled next to the package.
Filename: "{app}\yak.exe"; \
  Parameters: "install ""{app}\{#YakFileName}"""; \
  WorkingDir: "{app}"; \
  Flags: runhidden; \
  Check: FileExists(ExpandConstant('{app}\yak.exe')); \
  StatusMsg: "Installing the IfcLite Importer package into Rhino 8..."

; 2) Fallback: use the yak.exe that ships with Rhino 8.
Filename: "{code:GetRhinoYakPath}"; \
  Parameters: "install ""{app}\{#YakFileName}"""; \
  WorkingDir: "{app}"; \
  Flags: runhidden; \
  Check: UseRhinoYak; \
  StatusMsg: "Installing the IfcLite Importer package into Rhino 8..."

[Code]
{ Returns the full path to the yak.exe that ships with Rhino 8, or '' if Rhino
  is not found in the usual location. }
function GetRhinoYakPath(Param: string): string;
var
  Candidate: string;
begin
  Result := '';
  { Standard Rhino 8 install location. Adjust here if you support other paths. }
  Candidate := ExpandConstant('{commonpf}\Rhino 8\System\Yak.exe');
  if FileExists(Candidate) then
    Result := Candidate;
end;

{ Use the Rhino-shipped yak.exe only when we did NOT bundle our own copy and
  Rhino's yak.exe actually exists. This prevents the package being installed
  twice when both are present. }
function UseRhinoYak: Boolean;
begin
  Result := (not FileExists(ExpandConstant('{app}\yak.exe')))
            and (GetRhinoYakPath('') <> '');
end;
