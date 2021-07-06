@REM Copyright (c) OPC Foundation. All rights reserved.
@REM Licensed under the MIT license. See LICENSE file in the project root for full license information.

@setlocal EnableExtensions EnableDelayedExpansion
@echo off

set current-path=%~dp0
rem // remove trailing slash
set current-path=%current-path:~0,-1%
set build_root=%current-path%\..
set framework=netcoreapp3.1

cd %build_root%

rd /s /Q .\CodeCoverage
rd /s /Q .\TestResults
dotnet test "UA Core Library.sln" -v n --configuration Release  --framework %framework% --collect:"XPlat Code Coverage" --settings ./Tests/coverlet.runsettings.xml --results-directory ./TestResults 

REM ensure latest report tool is installed
dotnet tool uninstall -g dotnet-reportgenerator-globaltool
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./CodeCoverage  "-title:UA .Net Standard Test Coverage" -reporttypes:Badges;Html;HtmlSummary;Cobertura 

REM Display result in browser
.\CodeCoverage\index.html