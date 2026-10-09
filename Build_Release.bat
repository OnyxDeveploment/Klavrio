@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "PROJECT_FILE=%~dp0Klavrio.csproj"
set "OUTPUT_DIR=%~dp0bin\Release\net8.0-windows"
set "STAGING_DIR=%~dp0artifacts\publish\win-x64-staging"
set "APP_EXE=%STAGING_DIR%\Klavrio.exe"
set "RELEASE_VERSION=%~1"
set "VERSION_ARGS=-p:KlavrioReleaseBuild=true"

echo.
echo ========================================
echo   Klavrio Release Build - Windows x64
echo ========================================
echo.

if not exist "%PROJECT_FILE%" (
    echo ERROR: Klavrio.csproj was not found.
    goto :failed
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: The .NET SDK was not found in PATH.
    echo Install .NET SDK 8 or newer and try again.
    goto :failed
)

set "SDK_MAJOR="
for /F "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do set "SDK_MAJOR=%%V"
if not defined SDK_MAJOR (
    echo ERROR: Could not determine the installed .NET SDK version.
    goto :failed
)

if %SDK_MAJOR% LSS 8 (
    echo ERROR: .NET SDK 8 or newer is required.
    echo Detected SDK:
    dotnet --version
    goto :failed
)

if defined RELEASE_VERSION (
    powershell -NoProfile -Command "if ($env:RELEASE_VERSION -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { exit 1 }"
    if errorlevel 1 (
        echo ERROR: Version must use four numeric parts, for example 1.0.0.1.
        goto :failed
    )
    set "VERSION_ARGS=-p:Version=%RELEASE_VERSION% -p:AssemblyVersion=%RELEASE_VERSION% -p:FileVersion=%RELEASE_VERSION% -p:InformationalVersion=%RELEASE_VERSION%"
    echo Building release version %RELEASE_VERSION%...
) else (
    echo Building the version declared in Klavrio.csproj...
)

tasklist /FI "IMAGENAME eq Klavrio.exe" 2>nul | find /I "Klavrio.exe" >nul
if not errorlevel 1 (
    echo ERROR: Klavrio is currently running.
    echo Exit it from the notification area before building.
    goto :failed
)

if exist "%STAGING_DIR%\" (
    echo Removing the previous staging output...
    rmdir /S /Q "%STAGING_DIR%"
    if exist "%STAGING_DIR%\" (
        echo ERROR: Could not clean the staging directory:
        echo   %STAGING_DIR%
        goto :failed
    )
)

echo Publishing a self-contained single-file release...
dotnet publish "%PROJECT_FILE%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%STAGING_DIR%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishTrimmed=false ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    %VERSION_ARGS%

if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    goto :failed
)

if not exist "%APP_EXE%" (
    echo ERROR: Publish completed without creating Klavrio.exe.
    goto :failed
)

if not exist "%STAGING_DIR%\Data\THIRD_PARTY_LICENSE.txt" (
    echo ERROR: The published Data folder is missing THIRD_PARTY_LICENSE.txt.
    goto :failed
)

if not exist "%STAGING_DIR%\Data\COMMUNITY_DICTIONARY_LICENSES.txt" (
    echo ERROR: The published Data folder is missing COMMUNITY_DICTIONARY_LICENSES.txt.
    goto :failed
)

if not exist "%STAGING_DIR%\Data\english.txt" (
    echo ERROR: The published Data folder is missing english.txt.
    goto :failed
)

if not exist "%STAGING_DIR%\Data\hebrew.txt" (
    echo ERROR: The published Data folder is missing hebrew.txt.
    goto :failed
)

if not exist "%STAGING_DIR%\THIRD_PARTY_NOTICES.txt" (
    echo ERROR: The published release is missing THIRD_PARTY_NOTICES.txt.
    goto :failed
)

for %%F in (
    APACHE-2.0.txt
    DOTNET_RUNTIME_LICENSE.txt
    DOTNET_RUNTIME_THIRD_PARTY_NOTICES.txt
    PICTOGRAMMERS_LICENSE.txt
    WPF_LICENSE.txt
    WPF_THIRD_PARTY_NOTICES.txt
) do (
    if not exist "%STAGING_DIR%\ThirdParty\%%F" (
        echo ERROR: The published release is missing ThirdParty\%%F.
        goto :failed
    )
)

copy /Y "%~dp0LICENSE" "%STAGING_DIR%\LICENSE" >nul
if errorlevel 1 (
    echo ERROR: Could not copy LICENSE into the release.
    goto :failed
)

if exist "%OUTPUT_DIR%\" (
    echo Replacing the previous release output...
    rmdir /S /Q "%OUTPUT_DIR%"
    if exist "%OUTPUT_DIR%\" (
        echo ERROR: Could not replace the release directory.
        echo A file may still be open in:
        echo   %OUTPUT_DIR%
        goto :failed
    )
)

if not exist "%~dp0bin\Release\" mkdir "%~dp0bin\Release"
move "%STAGING_DIR%" "%OUTPUT_DIR%" >nul
if errorlevel 1 (
    echo ERROR: Could not move the validated release into its final directory.
    goto :failed
)

echo Creating the manual application-update manifest...
if defined RELEASE_VERSION (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-ApplicationUpdateManifest.ps1" -Version "%RELEASE_VERSION%" -ExecutablePath "%OUTPUT_DIR%\Klavrio.exe"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-ApplicationUpdateManifest.ps1" -ExecutablePath "%OUTPUT_DIR%\Klavrio.exe"
)
if errorlevel 1 (
    echo ERROR: Could not create the application-update manifest.
    goto :failed
)

copy /Y "%~dp0artifacts\application-update\klavrio-update.json" "%~dp0klavrio-update.json" >nul
if errorlevel 1 (
    echo ERROR: Could not update the repository-root klavrio-update.json.
    goto :failed
)

echo.
echo Build completed successfully.
echo.
echo Executable:
echo   %OUTPUT_DIR%\Klavrio.exe
echo.
echo Included:
echo   Data dictionaries and their license notices
echo   Software, runtime, framework, and icon license notices
echo   Klavrio LICENSE
echo.
echo Manual update-feed files:
echo   %~dp0klavrio-update.json
echo   %~dp0artifacts\application-update\klavrio-update.json
echo   %~dp0artifacts\application-update\Klavrio.exe.sha256
echo.

if not defined CI pause
exit /b 0

:failed
echo.
echo Build failed. The previous completed release was not intentionally replaced.
echo.
if not defined CI pause
exit /b 1
