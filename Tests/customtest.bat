@echo off
setlocal enabledelayedexpansion

echo This script is used to run custom platform tests for the UA Core Library
echo Supported parameters: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0

REM Check if the target framework parameter is provided
if "%1"=="" (
    echo Usage: %0 [TargetFramework]
    echo Allowed values for TargetFramework: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0, default
    goto :eof
)

REM Check if the provided TargetFramework is valid
set "validFrameworks= default net462 net472 netstandard2.0 netstandard2.1 net48 net6.0 net8.0 "
if "!validFrameworks: %1 =!"=="%validFrameworks%" (
    echo Invalid TargetFramework specified. Allowed values are: default, net462, net472 netstandard2.0, netstandard2.1, net48, net6.0, net8.0
    goto :eof
)

if "%1"=="default" (
    echo Using the default targets for the test runners.
    goto :cleanup
)

REM this is the variable used to switch the build scripts to a dedicated target
echo Using the %1 CustomTestTargets for the test runners.
set CustomTestTarget=%1

:cleanup

REM clean up all obj and bin folders

REM Set the root directory path as batch file location
set "root=%~dp0"

REM Delete 'obj' and 'bin' folders
for /d /r "%root%" %%d in (*obj *bin) do (
    echo Deleting "%%d"
	del /S /F /Q "%%d\*.*"
    rmdir /s /q "%%d"
)

echo Clean up complete.

echo restore %1
dotnet restore -f "..\UA Core Library.sln"
dotnet build --no-restore "..\UA Core Library.sln"
dotnet test "..\UA Core Library.sln"
