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
Var WaitForRunningApp
Var InstallStaging
Var InstallPrevious

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
!include "FileFunc.nsh"

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

    ; Never write over the working install. Writing straight into it meant a failure part way
    ; through left it half old and half new. Every file goes into a staging folder beside it
    ; first, and the old folder is swapped out only once all of them are written.
    StrCpy $InstallStaging "$INSTDIR.staging"
    StrCpy $InstallPrevious "$INSTDIR.previous"

    ; A run that died between the two renames below left the previous install set aside with
    ; nothing in its place. Put it back before anything else.
    IfFileExists "$INSTDIR\*.*" clear_leftovers
    IfFileExists "$InstallPrevious\*.*" 0 clear_leftovers
        ClearErrors
        Rename "$InstallPrevious" "$INSTDIR"
        IfErrors install_files_failed

    clear_leftovers:
    Push "$InstallStaging"
    Call RemoveTree
    Push "$InstallPrevious"
    Call RemoveTree
    IfFileExists "$InstallStaging\*.*" install_files_failed
    IfFileExists "$InstallPrevious\*.*" install_files_failed

    SetOutPath "$InstallStaging"
    ClearErrors
    File /r "${SOURCE_DIR}\*"
    WriteUninstaller "$InstallStaging\Uninstall.exe"
    IfErrors install_staging_failed

    ; Leave the staging folder first. It is setup's working directory now, and Windows will
    ; not rename a folder that a process has open as its working directory.
    SetOutPath "$TEMP"
    IfFileExists "$INSTDIR\*.*" 0 swap_in_staging
        ClearErrors
        Rename "$INSTDIR" "$InstallPrevious"
        IfErrors install_staging_failed

    swap_in_staging:
    ClearErrors
    Rename "$InstallStaging" "$INSTDIR"
    IfErrors 0 swap_done
        IfFileExists "$InstallPrevious\*.*" 0 install_staging_failed
        Rename "$InstallPrevious" "$INSTDIR"
        Goto install_staging_failed

    swap_done:
    ; If something still holds a file in the old folder this fails quietly, and the next run
    ; or the uninstaller removes it.
    Push "$InstallPrevious"
    Call RemoveTree
    SetOutPath "$INSTDIR"
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

    install_staging_failed:
        Call RemoveInstallStaging
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

    ; LocalPhotoPDF's own update button starts this installer and then closes itself, passing
    ; /FROMAPP. Shutting a WPF application down is not instant: it saves settings, stops the
    ; imaging thread, and only then releases the single-instance mutex. With a zero timeout
    ; that was a race, and losing it aborted the update with "close LocalPhotoPDF and run setup
    ; again" while LocalPhotoPDF was in the middle of closing. So when the app asked for this,
    ; wait for it. When a person started setup by hand, do not wait: they can see their own
    ; window and an unexplained pause would just look like a hang.
    StrCpy $WaitForRunningApp 0
    ${GetParameters} $R8
    ClearErrors
    ${GetOptions} $R8 "/FROMAPP" $R7
    IfErrors +2
    StrCpy $WaitForRunningApp 1

    Call RefuseElevatedRun
    Call AcquireAppMutex
FunctionEnd

Function RefuseElevatedRun
    ; Setup only touches one person's own profile, so it has no use for elevation, and an
    ; elevated run would be acting on folders an unelevated program could have laid out for it.
    ; 18 asks for TokenElevationType and 2 is TokenElevationTypeFull, which means someone chose
    ; to elevate. With UAC turned off the answer is 1, and setup carries on as normal.
    System::Call 'kernel32::GetCurrentProcess() p.r0'
    System::Call 'advapi32::OpenProcessToken(p r0, i 8, *p .r1) i.r2'
    StrCmp $2 0 elevation_checked
    System::Call 'advapi32::GetTokenInformation(p r1, i 18, *i .r3, i 4, *i .r4) i.r2'
    System::Call 'kernel32::CloseHandle(p r1)'
    StrCmp $2 0 elevation_checked
    StrCmp $3 2 0 elevation_checked
        IfSilent elevated_no_message
        MessageBox MB_OK|MB_ICONSTOP "Setup was started as administrator. LocalPhotoPDF installs for your own Windows account only, so please start setup normally. Nothing was changed."
    elevated_no_message:
        SetErrorLevel 1
        Abort

    elevation_checked:
FunctionEnd

Function AcquireAppMutex
    StrCpy $R9 1
    StrCmp $WaitForRunningApp 0 acquire_attempt
    StrCpy $R9 60   ; 60 tries, 250 ms apart, so up to fifteen seconds

    acquire_attempt:
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "${APP_MUTEX}") p.r0'
    StrCmp $0 0 mutex_failed
    System::Call 'kernel32::WaitForSingleObject(p r0, i 0) i.r1'
    StrCmp $1 0 mutex_acquired
    StrCmp $1 128 mutex_acquired
    System::Call 'kernel32::CloseHandle(p r0)'
    StrCmp $1 258 mutex_busy
    Goto mutex_failed

    mutex_busy:
    IntOp $R9 $R9 - 1
    IntCmp $R9 0 mutex_exists mutex_exists
    Sleep 250
    Goto acquire_attempt

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

Function RemoveInstallStaging
    ; Guarded so an empty path never reaches the delete.
    StrCmp $InstallStaging "" remove_staging_done
    SetOutPath "$TEMP"
    Push "$InstallStaging"
    Call RemoveTree
    remove_staging_done:
FunctionEnd

Function .onInstFailed
    ; Also reached when a person chooses Abort on a file error dialog, which skips the
    ; section's own clean-up. The install itself was never touched before the swap.
    Call RemoveInstallStaging
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

; RMDir /r walks through junctions, so a link planted as one of setup's own folders, or inside
; the old install, would get whatever it points at emptied. cmd's rmdir /s removes the link and
; never looks inside it. /d skips any AutoRun command set for cmd.
!macro RemoveTreeFunction UN
Function ${UN}RemoveTree
    Exch $0
    nsExec::Exec '"$SYSDIR\cmd.exe" /d /c rmdir /s /q "$0"'
    Pop $0
    Pop $0
FunctionEnd
!macroend

!insertmacro RemoveTreeFunction ""
!insertmacro RemoveTreeFunction "un."

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
    ; Setup's own staging and set-aside folders, left only if a file in one was held open.
    Push "$INSTDIR.staging"
    Call un.RemoveTree
    Push "$INSTDIR.previous"
    Call un.RemoveTree

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
