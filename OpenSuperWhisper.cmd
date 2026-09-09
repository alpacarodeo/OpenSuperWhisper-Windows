@echo off
setlocal
title OpenSuperWhisper Settings

if /I "%~1"=="hotkey" goto change_hotkey
if /I "%~1"=="language" goto change_language
if /I "%~1"=="show" goto show_hotkey
if /I "%~1"=="start" goto start_app
if /I "%~1"=="stop" goto stop_app

:menu
cls
echo =====================================================
echo              OpenSuperWhisper Settings
echo =====================================================
echo.
echo Current microphone shortcut:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure-hotkey.ps1" -ShowCurrent
echo.
echo Current dictation language:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure-language.ps1" -ShowCurrent
echo.
echo [1] Change microphone shortcut
echo [2] Change dictation language
echo [3] Show current shortcut
echo [4] Start OpenSuperWhisper
echo [5] Stop OpenSuperWhisper
echo [6] Open recordings folder
echo [7] Exit
echo.
set /p "CHOICE=Type a number and press Enter: "
if "%CHOICE%"=="1" goto change_hotkey_menu
if "%CHOICE%"=="2" goto change_language_menu
if "%CHOICE%"=="3" goto show_hotkey_menu
if "%CHOICE%"=="4" goto start_app_menu
if "%CHOICE%"=="5" goto stop_app_menu
if "%CHOICE%"=="6" goto recordings
if "%CHOICE%"=="7" exit /b 0
goto menu

:change_hotkey_menu
call :change_hotkey_action
pause
goto menu

:change_language_menu
call :change_language_action
pause
goto menu

:show_hotkey_menu
call :show_hotkey_action
pause
goto menu

:start_app_menu
call :start_app_action
pause
goto menu

:stop_app_menu
call :stop_app_action
pause
goto menu

:recordings
if not exist "%LOCALAPPDATA%\OpenSuperWhisper\Recordings" mkdir "%LOCALAPPDATA%\OpenSuperWhisper\Recordings"
start "" "%LOCALAPPDATA%\OpenSuperWhisper\Recordings"
goto menu

:change_hotkey
call :change_hotkey_action
exit /b %ERRORLEVEL%

:change_language
call :change_language_action
exit /b %ERRORLEVEL%

:show_hotkey
call :show_hotkey_action
exit /b %ERRORLEVEL%

:start_app
call :start_app_action
exit /b %ERRORLEVEL%

:stop_app
call :stop_app_action
exit /b %ERRORLEVEL%

:change_hotkey_action
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure-hotkey.ps1"
exit /b %ERRORLEVEL%

:change_language_action
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure-language.ps1"
exit /b %ERRORLEVEL%

:show_hotkey_action
echo Current microphone shortcut:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure-hotkey.ps1" -ShowCurrent
exit /b %ERRORLEVEL%

:start_app_action
start "" /b "%~dp0OpenSuperWhisper.Windows.exe" --background
echo OpenSuperWhisper started.
exit /b 0

:stop_app_action
taskkill.exe /IM OpenSuperWhisper.Windows.exe /F >nul 2>&1
echo OpenSuperWhisper stopped.
exit /b 0
