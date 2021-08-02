@REM Copyright (c) OPC Foundation. All rights reserved.
@REM Licensed under the MIT license. See LICENSE file in the project root for full license information.

@setlocal EnableExtensions EnableDelayedExpansion
@echo off

set current-path=%~dp0
rem // remove trailing slash
set current-path=%current-path:~0,-1%
set build_root=%current-path%\..
set framework=net462

cd %build_root%

cd Tests\Opc.Ua.Security.Certificates.Tests\
dotnet run -v n --configuration Release  --framework %framework% -- 
cd ..\Opc.Ua.Core.Tests
dotnet run -v n --configuration Release  --framework %framework% -- 
cd ..\Opc.Ua.Server.Tests
dotnet run -v n --configuration Release  --framework %framework% -- 
cd ..\Opc.Ua.Client.Tests
dotnet run -v n --configuration Release  --framework %framework% -- 
cd ..
