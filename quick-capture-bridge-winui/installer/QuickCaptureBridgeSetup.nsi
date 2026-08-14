Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef PAYLOAD
  !error "PAYLOAD must point to the published WinUI application directory."
!endif
!ifndef OUTFILE
  !error "OUTFILE must point to the generated installer executable."
!endif

Name "Quick Capture Bridge"
Caption "Quick Capture Bridge Setup"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Quick Capture Bridge"
InstallDirRegKey HKCU "Software\QuickCaptureBridgeWinUI" "InstallLocation"
RequestExecutionLevel user
BrandingText "Quick Capture Bridge"
ShowInstDetails show
ShowUninstDetails show
SetCompressor /SOLID lzma

Icon "${PAYLOAD}\Assets\AppIcon.ico"
UninstallIcon "${PAYLOAD}\Assets\AppIcon.ico"

VIProductVersion "1.1.0.0"
VIAddVersionKey "ProductName" "Quick Capture Bridge"
VIAddVersionKey "CompanyName" "Quick Capture Plugins"
VIAddVersionKey "FileDescription" "Native WinUI 3 global capture bridge for Obsidian"
VIAddVersionKey "FileVersion" "1.1.0.0"
VIAddVersionKey "ProductVersion" "1.1.0.0"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 Quick Capture Plugins"

!define MUI_ABORTWARNING
!define MUI_ICON "${PAYLOAD}\Assets\AppIcon.ico"
!define MUI_UNICON "${PAYLOAD}\Assets\AppIcon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\QuickCaptureBridgeWinUI.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch Quick Capture Bridge"
!define MUI_FINISHPAGE_RUN_CHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

Section "Quick Capture Bridge" InstallSection
  SectionIn RO
  ; Allow an existing bridge installation to be upgraded without leaving a
  ; locked executable behind. User settings live in LocalAppData and remain
  ; untouched by this process stop.
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM QuickCaptureBridgeWinUI.exe'
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"

  CreateDirectory "$SMPROGRAMS\Quick Capture Bridge"
  CreateShortCut "$SMPROGRAMS\Quick Capture Bridge\Quick Capture Bridge.lnk" "$INSTDIR\QuickCaptureBridgeWinUI.exe" "" "$INSTDIR\Assets\AppIcon.ico" 0 SW_SHOWNORMAL

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\QuickCaptureBridgeWinUI" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "DisplayName" "Quick Capture Bridge"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "DisplayVersion" "1.1.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "Publisher" "Quick Capture Plugins"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "DisplayIcon" "$INSTDIR\QuickCaptureBridgeWinUI.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI" "NoRepair" 1
SectionEnd

Section "Uninstall"
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM QuickCaptureBridgeWinUI.exe'
  Delete "$SMPROGRAMS\Quick Capture Bridge\Quick Capture Bridge.lnk"
  RMDir "$SMPROGRAMS\Quick Capture Bridge"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "QuickCaptureBridgeWinUI"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickCaptureBridgeWinUI"
  DeleteRegKey HKCU "Software\QuickCaptureBridgeWinUI"
  RMDir /r "$INSTDIR"
SectionEnd
