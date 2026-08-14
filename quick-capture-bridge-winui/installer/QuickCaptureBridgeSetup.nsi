Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef PAYLOAD
  !error "PAYLOAD must point to the published WinUI application directory."
!endif
!ifndef OUTFILE
  !error "OUTFILE must point to the generated installer executable."
!endif

Name "Moment"
Caption "Moment Setup"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Moment"
InstallDirRegKey HKCU "Software\Moment" "InstallLocation"
RequestExecutionLevel user
BrandingText "Moment"
ShowInstDetails show
ShowUninstDetails show
SetCompressor /SOLID lzma

Icon "${PAYLOAD}\Assets\AppIcon.ico"
UninstallIcon "${PAYLOAD}\Assets\AppIcon.ico"

VIProductVersion "1.2.3.0"
VIAddVersionKey "ProductName" "Moment"
VIAddVersionKey "CompanyName" "neura-neura"
VIAddVersionKey "FileDescription" "Native WinUI 3 global text and voice capture"
VIAddVersionKey "FileVersion" "1.2.3.0"
VIAddVersionKey "ProductVersion" "1.2.3.0"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 neura-neura"

!define MUI_ABORTWARNING
!define MUI_ICON "${PAYLOAD}\Assets\AppIcon.ico"
!define MUI_UNICON "${PAYLOAD}\Assets\AppIcon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\Moment.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch Moment"
!define MUI_FINISHPAGE_RUN_PARAMETERS "--foreground"
!define MUI_FINISHPAGE_RUN_CHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

Section "Moment" InstallSection
  SectionIn RO
  ; Allow an existing bridge installation to be upgraded without leaving a
  ; locked executable behind. User settings live in LocalAppData and remain
  ; untouched by this process stop.
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM Moment.exe'
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM QuickCaptureBridgeWinUI.exe'
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Moment" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "DisplayName" "Moment"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "DisplayVersion" "1.2.3"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "Publisher" "neura-neura"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "DisplayIcon" "$INSTDIR\Moment.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment" "NoRepair" 1
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "QuickCaptureBridgeWinUI"
SectionEnd

Section "Start menu shortcut" StartMenuSection
  CreateDirectory "$SMPROGRAMS\Moment"
  CreateShortCut "$SMPROGRAMS\Moment\Moment.lnk" "$INSTDIR\Moment.exe" "" "$INSTDIR\Assets\AppIcon.ico" 0 SW_SHOWNORMAL
SectionEnd

Section /o "Desktop shortcut" DesktopSection
  CreateShortCut "$DESKTOP\Moment.lnk" "$INSTDIR\Moment.exe" "" "$INSTDIR\Assets\AppIcon.ico" 0 SW_SHOWNORMAL
SectionEnd

Section "Uninstall"
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM Moment.exe'
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM QuickCaptureBridgeWinUI.exe'
  Delete "$SMPROGRAMS\Moment\Moment.lnk"
  RMDir "$SMPROGRAMS\Moment"
  Delete "$DESKTOP\Moment.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Moment"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "QuickCaptureBridgeWinUI"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Moment"
  DeleteRegKey HKCU "Software\QuickCaptureBridgeWinUI"
  DeleteRegKey HKCU "Software\Moment"
  RMDir /r "$INSTDIR"
SectionEnd
