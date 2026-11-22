@REM SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
@REM SPDX-License-Identifier: MIT

@setlocal EnableExtensions EnableDelayedExpansion
@echo off

set current-path=%~dp0
rem // remove trailing slash
set current-path=%current-path:~0,-1%
set build_root=%current-path%\..
set framework=net10.0
set runtimes=net48 net10.0
set filter=*

cd %build_root%

cd tests\Opc.Ua.Security.Certificates.Tests\
dotnet run -v n --configuration Release  --framework %framework% -- --filter %filter% --runtimes %runtimes%
cd ..\Opc.Ua.Core.Tests
dotnet run -v n --configuration Release  --framework %framework% -- --filter %filter% --runtimes %runtimes%
cd ..\Opc.Ua.Server.Tests
dotnet run -v n --configuration Release  --framework %framework% -- --filter %filter% --runtimes %runtimes%
cd ..\Opc.Ua.Client.Tests
dotnet run -v n --configuration Release  --framework %framework% -- --filter %filter% --runtimes %runtimes%
cd ..\Opc.Ua.Types.Tests
dotnet run -v n --configuration Release  --framework %framework% -- --filter %filter% --runtimes %runtimes%
cd ..
