@echo off
REM Sample batch file with input parameters
REM Usage: example.bat [name] [age] [city]

echo ========================================
echo Welcome!
echo ========================================
echo.

REM Check if parameters exist
if "%~1"=="" (
    echo Error: Please provide your name!
    echo Usage: %~nx0 [name] [age] [city]
    goto :end
)

if "%~2"=="" (
    echo Error: Please provide your age!
    echo Usage: %~nx0 [name] [age] [city]
    goto :end
)

if "%~3"=="" (
    echo Error: Please provide your city!
    echo Usage: %~nx0 [name] [age] [city]
    goto :end
)

echo Application Name: ${APP_BASE_NAME}

REM Print information
echo First parameter (name): %~1
echo Second parameter (age): %~2
echo Third parameter (city): %~3
echo.
echo Hello %~1! You are %~2 years old and live in %~3.
echo.

:end
echo ========================================
echo End of program
echo ========================================
