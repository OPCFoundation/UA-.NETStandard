@echo off
setlocal

REM if docker is not available, ensure the Opc.Ua.ModelCompiler.exe is in the PATH
set MODELCOMPILER=Opc.Ua.ModelCompiler.exe
REM The version of the ModelCompiler from the OPCF to use as docker container
set MODELCOMPILERIMAGE=ghcr.io/opcfoundation/ua-modelcompiler:2.3.0
set MODELROOT=.

echo pull latest modelcompiler from github container registry
echo %MODELCOMPILERIMAGE%
docker pull %MODELCOMPILERIMAGE%
IF ERRORLEVEL 1 (
:nodocker
    Echo The docker command to download ModelCompiler failed. Using local PATH instead to execute ModelCompiler.
) ELSE (
    Echo Successfully pulled the latest docker container for ModelCompiler.
    set MODELROOT=/model
    set MODELCOMPILER=docker run -v "%CD%:/model" -it --rm --name ua-modelcompiler %MODELCOMPILERIMAGE%
)

echo Building TestData
%MODELCOMPILER% compile -version v104 -id 1000 -d2 "%MODELROOT%/TestData/Generated/TestDataDesign.xml" -cg "%MODELROOT%/TestData/Generated/TestDataDesign.csv" -o2 "%MODELROOT%/TestData/Generated"
IF %ERRORLEVEL% EQU 0 echo Success!

echo Building MemoryBuffer
%MODELCOMPILER% compile -version v104 -id 1000 -d2 "%MODELROOT%/MemoryBuffer/Generated/MemoryBufferDesign.xml" -cg "%MODELROOT%/MemoryBuffer/Generated/MemoryBufferDesign.csv" -o2 "%MODELROOT%/MemoryBuffer/Generated"
IF %ERRORLEVEL% EQU 0 echo Success!

echo Building BoilerDesign
%MODELCOMPILER% compile -version v104 -id 1000 -d2 "%MODELROOT%/Boiler/Generated/BoilerDesign.xml" -cg "%MODELROOT%/Boiler/Generated/BoilerDesign.csv" -o2 "%MODELROOT%/Boiler/Generated"
IF %ERRORLEVEL% EQU 0 echo Success!

