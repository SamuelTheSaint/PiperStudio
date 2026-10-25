; Piper Studio installer (NSIS 3). Build:  makensis /DVERSION=1.2 /DSRC=..\PiperStudio-Share\PiperStudio PiperStudio.nsi
; SRC = the package folder made by make-package (PiperStudio.exe, piper.exe, voices\ ...).
; Installs for the current user only (no admin rights needed), like most modern apps.

Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.2"
!endif
!ifndef SRC
  !define SRC "..\PiperStudio-Share\PiperStudio"
!endif
!ifndef OUTFILE
  !define OUTFILE "PiperStudio-v${VERSION}-Setup.exe"
!endif

!define APP "Piper Studio"
!define EXE "PiperStudio.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PiperStudio"

Name "${APP} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Piper Studio"
InstallDirRegKey HKCU "Software\PiperStudio" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${APP} ${VERSION} — free text-to-speech"
VIProductVersion "${VERSION}.0.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileDescription" "${APP} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Samuel — MIT License"

!define MUI_ICON "..\app.ico"
!define MUI_UNICON "..\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\README.txt"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SRC}\THIRD-PARTY-NOTICES.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Farsi"

LangString SecApp ${LANG_ENGLISH} "Piper Studio (program, engine and voices)"
LangString SecApp ${LANG_FARSI} "Piper Studio (برنامه، موتور و صداها)"
LangString SecDesk ${LANG_ENGLISH} "Desktop shortcut"
LangString SecDesk ${LANG_FARSI} "میان‌بر روی دسکتاپ"
LangString SecAssoc ${LANG_ENGLISH} "Open .pstudio projects with Piper Studio"
LangString SecAssoc ${LANG_FARSI} "باز کردن پروژه‌های ‎.pstudio‎ با Piper Studio"
LangString Running ${LANG_ENGLISH} "Piper Studio is running. Close it (also from the tray icon near the clock), then click Retry."
LangString Running ${LANG_FARSI} "Piper Studio باز است. آن را ببندید (از آیکون کنار ساعت هم)، بعد «تلاش مجدد» را بزنید."
LangString ProjectType ${LANG_ENGLISH} "Piper Studio project"
LangString ProjectType ${LANG_FARSI} "پروژهٔ Piper Studio"

Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

; Make sure the exe isn't in use (the installer can't replace a running program).
!macro CheckRunning
  retry:
  ClearErrors
  IfFileExists "$INSTDIR\${EXE}" 0 done
  FileOpen $0 "$INSTDIR\${EXE}" a
  IfErrors 0 closeit
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(Running)" IDRETRY retry
  Abort
  closeit:
  FileClose $0
  done:
!macroend

Section "$(SecApp)" SEC_APP
  SectionIn RO
  !insertmacro CheckRunning
  SetOutPath "$INSTDIR"
  File /r /x "*.ini" /x "PiperStudio_lasttext.txt" /x "replacements.txt" /x "history.txt" /x "words.txt" "${SRC}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\PiperStudio" "InstallDir" "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
  CreateShortcut "$SMPROGRAMS\${APP}\README.lnk" "$INSTDIR\README.txt"
  CreateShortcut "$SMPROGRAMS\${APP}\راهنما (فارسی).lnk" "$INSTDIR\README-FA.txt"
  CreateShortcut "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk" "$INSTDIR\Uninstall.exe"
  ; Add/Remove Programs
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "Samuel"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "https://github.com/SamuelTheSaint/PiperStudio"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "$(SecDesk)" SEC_DESK
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
SectionEnd

Section "$(SecAssoc)" SEC_ASSOC
  WriteRegStr HKCU "Software\Classes\.pstudio" "" "PiperStudio.Project"
  WriteRegStr HKCU "Software\Classes\PiperStudio.Project" "" "$(ProjectType)"
  WriteRegStr HKCU "Software\Classes\PiperStudio.Project\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "Software\Classes\PiperStudio.Project\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
  !insertmacro CheckRunning
  ; the program folder (voices, Kokoro downloads, engine). Your settings, phrases and recordings in
  ; %APPDATA%\PiperStudio and Documents\Piper Studio are kept.
  Delete "$INSTDIR\PiperStudio.exe"
  Delete "$INSTDIR\piper.exe"
  Delete "$INSTDIR\ffmpeg.exe"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\libtashkeel_model.ort"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\README-FA.txt"
  Delete "$INSTDIR\THIRD-PARTY-NOTICES.txt"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\portable.txt"
  RMDir /r "$INSTDIR\espeak-ng-data"
  RMDir /r "$INSTDIR\voices"
  RMDir /r "$INSTDIR\kokoro"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}\*.lnk"
  RMDir "$SMPROGRAMS\${APP}"
  Delete "$DESKTOP\${APP}.lnk"
  ReadRegStr $0 HKCU "Software\Classes\.pstudio" ""
  StrCmp $0 "PiperStudio.Project" 0 +2
    DeleteRegKey HKCU "Software\Classes\.pstudio"
  DeleteRegKey HKCU "Software\Classes\PiperStudio.Project"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\PiperStudio"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd
