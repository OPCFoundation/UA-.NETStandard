@echo off
setlocal enabledelayedexpansion

echo This script is used to run custom platform tests for the UA Core Library
echo Supported parameters: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0

REM Check if the target framework parameter is provided
if "%1"=="" (
    echo Usage: %0 [TargetFramework]
    echo Allowed values for TargetFramework: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0
    goto :eof
)

REM Check if the provided TargetFramework is valid
set "validFrameworks= net462 netstandard2.0 netstandard2.1 net48 net6.0 net8.0"
if "!validFrameworks: %1 =!"=="%validFrameworks%" (
    echo Invalid TargetFramework specified. Allowed values are: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0
    goto :eof
)

REM this is the variable used to switch the build scripts to a dedicated target
set CustomTestTarget=%1

REM clean up all obj and bin folders

REM Set the root directory path as batch file location
set "root=%~dp0"

REM Delete 'obj' and 'bin' folders
for /d /r "%root%" %%d in (*obj *bin) do (
    echo Deleting "%%d"
	del /S /F /Q "%%d\*.*"
    rmdir /s "%%d"
)

echo Clean up complete.

echo restore %1
dotnet restore -f "..\UA Core Library.sln"
dotnet build --no-restore "..\UA Core Library.sln"
dotnet test "..\UA Core Library.sln"
