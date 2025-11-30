@echo off
REM ============================================================================
REM Netloy Post-Publish Script for Windows
REM ============================================================================
REM This script demonstrates using macros in two ways:
REM   1. String replacement: ${MACRO_NAME} - replaced by Netloy before execution
REM   2. Environment variables: %MACRO_NAME% - accessed during execution
REM ============================================================================

echo.
echo ============================================================================
echo POST-PUBLISH SCRIPT - Windows
echo ============================================================================
echo.

REM ----------------------------------------------------------------------------
REM METHOD 1: String Replacement (values replaced by Netloy before execution)
REM ----------------------------------------------------------------------------
echo [INFO] Application Information (String Replacement):
echo   App Name     : ${APP_FRIENDLY_NAME}
echo   App ID       : ${APP_ID}
echo   Version      : ${APP_VERSION}
echo   Package Type : ${PACKAGE_TYPE}
echo   Runtime      : ${PACKAGE_ARCH}
echo.

REM ----------------------------------------------------------------------------
REM METHOD 2: Environment Variables (values accessed during execution)
REM ----------------------------------------------------------------------------
echo [INFO] Build Information (Environment Variables):
echo   Publisher    : %PUBLISHER_NAME%
echo   Executable   : %APP_EXEC_NAME%
echo   Output Dir   : %PUBLISH_OUTPUT_DIRECTORY%
echo   Config Dir   : %CONF_FILE_DIRECTORY%
echo.

REM ----------------------------------------------------------------------------
REM Example: Copy Additional Files
REM ----------------------------------------------------------------------------
echo [INFO] Copying additional files...

set "TARGET_DIR=%PUBLISH_OUTPUT_DIRECTORY%"

if exist "%CONF_FILE_DIRECTORY%\Resources" (
    echo   Source: %CONF_FILE_DIRECTORY%\Resources
    echo   Target: %TARGET_DIR%

    xcopy /Y /E /I "%CONF_FILE_DIRECTORY%\Resources\*" "%TARGET_DIR%\" >nul 2>&1

    if %ERRORLEVEL% EQU 0 (
        echo   [SUCCESS] Resources copied successfully
    ) else (
        echo   [WARNING] Failed to copy resources
    )
) else (
    echo   [SKIP] Resources folder not found
)
echo.

REM ----------------------------------------------------------------------------
REM Example: Create Version File
REM ----------------------------------------------------------------------------
echo [INFO] Creating version information file...

echo Application: ${APP_FRIENDLY_NAME} > "%TARGET_DIR%\version.txt"
echo Version: %APP_VERSION% >> "%TARGET_DIR%\version.txt"
echo Build Date: %DATE% %TIME% >> "%TARGET_DIR%\version.txt"
echo Package Type: %PACKAGE_TYPE% >> "%TARGET_DIR%\version.txt"
echo Runtime: %PACKAGE_ARCH% >> "%TARGET_DIR%\version.txt"
echo Publisher: %PUBLISHER_NAME% >> "%TARGET_DIR%\version.txt"

echo   [SUCCESS] Version file created: %TARGET_DIR%\version.txt
echo.

REM ----------------------------------------------------------------------------
REM Example: Conditional Processing by Architecture
REM ----------------------------------------------------------------------------
echo [INFO] Performing platform-specific tasks...

if /I "%PACKAGE_ARCH%"=="win-x64" (
    echo   [INFO] Detected x64 architecture
    echo   [ACTION] Optimizing for x64 platform
) else if /I "%PACKAGE_ARCH%"=="win-x86" (
    echo   [INFO] Detected x86 architecture
    echo   [ACTION] Optimizing for x86 platform
) else if /I "%PACKAGE_ARCH%"=="win-arm64" (
    echo   [INFO] Detected ARM64 architecture
    echo   [ACTION] Optimizing for ARM64 platform
)
echo.

REM ----------------------------------------------------------------------------
REM Example: Create README file
REM ----------------------------------------------------------------------------
echo [INFO] Creating README file...

echo ${APP_FRIENDLY_NAME} > "%TARGET_DIR%\README.txt"
echo ============================================ >> "%TARGET_DIR%\README.txt"
echo. >> "%TARGET_DIR%\README.txt"
echo Version: %APP_VERSION% >> "%TARGET_DIR%\README.txt"
echo Runtime: %PACKAGE_ARCH% >> "%TARGET_DIR%\README.txt"
echo. >> "%TARGET_DIR%\README.txt"
echo Installation: >> "%TARGET_DIR%\README.txt"
echo 1. Extract all files to a folder >> "%TARGET_DIR%\README.txt"
echo 2. Run %APP_EXEC_NAME% >> "%TARGET_DIR%\README.txt"
echo. >> "%TARGET_DIR%\README.txt"
echo For more information, visit: >> "%TARGET_DIR%\README.txt"
echo ${PUBLISHER_LINK_URL} >> "%TARGET_DIR%\README.txt"
echo. >> "%TARGET_DIR%\README.txt"
echo Copyright %PUBLISHER_COPYRIGHT% >> "%TARGET_DIR%\README.txt"

echo   [SUCCESS] README file created: %TARGET_DIR%\README.txt
echo.

REM ----------------------------------------------------------------------------
REM Example: Verify Output
REM ----------------------------------------------------------------------------
echo [INFO] Verifying build output...

if exist "%TARGET_DIR%\%APP_EXEC_NAME%" (
    echo   [SUCCESS] Main executable found: %APP_EXEC_NAME%
) else (
    echo   [ERROR] Main executable not found: %APP_EXEC_NAME%
    exit /b 1
)

echo   [INFO] Total files in output:
dir /B "%TARGET_DIR%" | find /C /V ""
echo.

REM ----------------------------------------------------------------------------
REM Completion
REM ----------------------------------------------------------------------------
echo ============================================================================
echo POST-PUBLISH SCRIPT COMPLETED SUCCESSFULLY
echo ============================================================================
echo   Output Location: %PUBLISHER_OUTPUT_DIRECTORY%
echo   Package: ${APP_FRIENDLY_NAME} v%APP_VERSION%
echo   Runtime: %PACKAGE_ARCH%
echo ============================================================================
echo.

exit /b 0
