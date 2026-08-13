Unicode true
ManifestSupportedOS Win10
ManifestDPIAware true
RequestExecutionLevel user
CRCCheck on
SetCompressor /SOLID lzma
SetCompressorDictSize 32
SetDatablockOptimize on
SetOverwrite on

!ifndef APP_VERSION
    !define APP_VERSION "1.0.0"
!endif

!ifndef SOURCE_DIR
    !error "SOURCE_DIR must point to the prepared application directory."
!endif

!ifndef OUTPUT_FILE
    !define OUTPUT_FILE "LocalPhotoPDF-Setup-${APP_VERSION}-win-x64.exe"
!endif

!define APP_NAME "LocalPhotoPDF"
!define APP_EXE "LocalPhotoPDF.exe"
!define APP_PUBLISHER "LocalPhotoPDF contributors"
!define APP_MUTEX "Local\LocalPhotoPDF.AppMutex.v1"
!define APP_REG_KEY "Software\LocalPhotoPDF"
!define APP_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalPhotoPDF"

Var AppMutexHandle

Name "${APP_NAME} ${APP_VERSION}"
Caption "${APP_NAME} Setup"
BrandingText "LocalPhotoPDF"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\LocalPhotoPDF"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} per-user installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 LocalPhotoPDF contributors"

!include "MUI2.nsh"
!include "Sections.nsh"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open LocalPhotoPDF"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "!LocalPhotoPDF (required)" SEC_CORE
    SectionIn RO
    SetShellVarContext current
    SetOutPath "$INSTDIR"
    ClearErrors
    File /r "${SOURCE_DIR}\*"
    IfErrors install_files_failed

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\LocalPhotoPDF.lnk" "$INSTDIR\${APP_EXE}"

    WriteRegStr HKCU "${APP_REG_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKCU "${APP_UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${APP_UNINSTALL_KEY}" "NoRepair" 1
    Goto install_files_done

    install_files_failed:
        IfSilent install_files_no_message
        MessageBox MB_OK|MB_ICONSTOP "Setup could not safely update LocalPhotoPDF. Close the app, check access to the install folder, and run setup again."
    install_files_no_message:
        SetErrorLevel 1
        Abort

    install_files_done:
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
    SetShellVarContext current
    CreateShortcut "$DESKTOP\LocalPhotoPDF.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section "-Release setup safety lock"
    ; Finish-page launch happens after sections complete. Release the installer-held
    ; mutex here so "Open LocalPhotoPDF" can start the newly installed app.
    Call ReleaseAppMutex
SectionEnd

LangString DESC_SEC_CORE ${LANG_ENGLISH} "Install LocalPhotoPDF for your Windows account and add a Start menu shortcut."
LangString DESC_SEC_DESKTOP ${LANG_ENGLISH} "Add a LocalPhotoPDF shortcut to your desktop."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CORE} $(DESC_SEC_CORE)
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} $(DESC_SEC_DESKTOP)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function .onInit
    SetShellVarContext current
    StrCpy $INSTDIR "$LOCALAPPDATA\Programs\LocalPhotoPDF"
    Call AcquireAppMutex
FunctionEnd

Function AcquireAppMutex
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "${APP_MUTEX}") p.r0'
    StrCmp $0 0 mutex_failed
    System::Call 'kernel32::WaitForSingleObject(p r0, i 0) i.r1'
    StrCmp $1 0 mutex_acquired
    StrCmp $1 128 mutex_acquired
    System::Call 'kernel32::CloseHandle(p r0)'
    StrCmp $1 258 mutex_exists
    Goto mutex_failed

    mutex_acquired:
    StrCpy $AppMutexHandle $0
    Return

    mutex_exists:
        IfSilent mutex_exists_no_message
        MessageBox MB_OK|MB_ICONEXCLAMATION "Close LocalPhotoPDF before installing or updating it, then run setup again."
    mutex_exists_no_message:
        SetErrorLevel 1
        Abort

    mutex_failed:
        IfSilent mutex_failed_no_message
        MessageBox MB_OK|MB_ICONSTOP "Setup could not create its LocalPhotoPDF safety lock. Nothing was changed."
    mutex_failed_no_message:
        SetErrorLevel 1
        Abort
FunctionEnd

Function ReleaseAppMutex
    StrCmp $AppMutexHandle "" release_done
    System::Call 'kernel32::ReleaseMutex(p $AppMutexHandle)'
    System::Call 'kernel32::CloseHandle(p $AppMutexHandle)'
    StrCpy $AppMutexHandle ""
    release_done:
FunctionEnd

Function .onInstSuccess
    Call ReleaseAppMutex
FunctionEnd

Function .onGUIEnd
    Call ReleaseAppMutex
FunctionEnd

Function un.onInit
    SetShellVarContext current
    StrCpy $INSTDIR "$LOCALAPPDATA\Programs\LocalPhotoPDF"
    Call un.AcquireAppMutex

    ReadRegStr $0 HKCU "${APP_REG_KEY}" "InstallLocation"
    StrCmp $0 "$INSTDIR" registry_ok
        IfSilent registry_mismatch_no_message
        MessageBox MB_OK|MB_ICONSTOP "LocalPhotoPDF's recorded install location does not match its protected per-user folder. Nothing was removed."
    registry_mismatch_no_message:
        SetErrorLevel 1
        Abort

    registry_ok:
    IfFileExists "$INSTDIR\${APP_EXE}" executable_ok
        IfSilent executable_missing_no_message
        MessageBox MB_OK|MB_ICONSTOP "LocalPhotoPDF.exe was not found in the protected per-user install folder. Nothing was removed."
    executable_missing_no_message:
        SetErrorLevel 1
        Abort

    executable_ok:
FunctionEnd

Function un.AcquireAppMutex
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "${APP_MUTEX}") p.r0'
    StrCmp $0 0 mutex_failed
    System::Call 'kernel32::WaitForSingleObject(p r0, i 0) i.r1'
    StrCmp $1 0 mutex_acquired
    StrCmp $1 128 mutex_acquired
    System::Call 'kernel32::CloseHandle(p r0)'
    StrCmp $1 258 mutex_exists
    Goto mutex_failed

    mutex_acquired:
    StrCpy $AppMutexHandle $0
    Return

    mutex_exists:
        IfSilent mutex_exists_no_message
        MessageBox MB_OK|MB_ICONEXCLAMATION "Close LocalPhotoPDF before uninstalling it, then try again."
    mutex_exists_no_message:
        SetErrorLevel 1
        Abort

    mutex_failed:
        IfSilent mutex_failed_no_message
        MessageBox MB_OK|MB_ICONSTOP "The uninstaller could not create its LocalPhotoPDF safety lock. Nothing was removed."
    mutex_failed_no_message:
        SetErrorLevel 1
        Abort
FunctionEnd

Function un.ReleaseAppMutex
    StrCmp $AppMutexHandle "" release_done
    System::Call 'kernel32::ReleaseMutex(p $AppMutexHandle)'
    System::Call 'kernel32::CloseHandle(p $AppMutexHandle)'
    StrCpy $AppMutexHandle ""
    release_done:
FunctionEnd

Function un.onUninstSuccess
    Call un.ReleaseAppMutex
FunctionEnd

Function un.onGUIEnd
    Call un.ReleaseAppMutex
FunctionEnd

Section "Uninstall"
    SetShellVarContext current
    Delete "$DESKTOP\LocalPhotoPDF.lnk"
    Delete "$SMPROGRAMS\LocalPhotoPDF.lnk"

    ; Remove only files owned by LocalPhotoPDF. Unexpected files and folders are
    ; deliberately preserved rather than recursively erased.
    Delete "$LOCALAPPDATA\LocalPhotoPDF\settings.json"
    Delete "$LOCALAPPDATA\LocalPhotoPDF\settings.json.tmp"
    RMDir "$LOCALAPPDATA\LocalPhotoPDF"

    ClearErrors
    Delete "$INSTDIR\LocalPhotoPDF.exe"
    IfErrors retry_app_delete app_delete_done
    retry_app_delete:
        Sleep 250
        ClearErrors
        Delete "$INSTDIR\LocalPhotoPDF.exe"
    app_delete_done:
    Delete "$INSTDIR\LICENSE"
    Delete "$INSTDIR\README.md"
    Delete "$INSTDIR\USER-GUIDE.md"
    Delete "$INSTDIR\PRIVACY.md"
    Delete "$INSTDIR\SECURITY.md"
    Delete "$INSTDIR\THIRD-PARTY-NOTICES.md"
    Delete "$INSTDIR\SBOM.cdx.json"
    Delete "$INSTDIR\licenses\dotnet-runtime-LICENSE.txt"
    Delete "$INSTDIR\licenses\dotnet-runtime-THIRD-PARTY-NOTICES.txt"
    Delete "$INSTDIR\licenses\windowsdesktop-runtime-LICENSE.txt"
    Delete "$INSTDIR\licenses\microsoft-extensions-LICENSE.txt"
    Delete "$INSTDIR\licenses\microsoft-extensions-THIRD-PARTY-NOTICES.txt"
    Delete "$INSTDIR\licenses\NSIS-COPYING.txt"
    Delete "$INSTDIR\licenses\PDFsharp-LICENSE.txt"
    RMDir "$INSTDIR\licenses"
    IfErrors uninstall_files_failed

    ClearErrors
    Delete "$INSTDIR\Uninstall.exe"
    IfErrors uninstall_files_failed

    RMDir "$INSTDIR"

    DeleteRegKey HKCU "${APP_UNINSTALL_KEY}"
    DeleteRegKey HKCU "${APP_REG_KEY}"
    Goto uninstall_files_done

    uninstall_files_failed:
        IfSilent uninstall_files_no_message
        MessageBox MB_OK|MB_ICONSTOP "The uninstaller could not remove every LocalPhotoPDF file. Close anything using the install folder and run the uninstaller again. The uninstall registration was kept for a safe retry."
    uninstall_files_no_message:
        SetErrorLevel 1

    uninstall_files_done:
SectionEnd
