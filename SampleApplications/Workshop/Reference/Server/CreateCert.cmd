@echo off
REM create the app certificate
cd /D %~dp0
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "Quickstart Reference Server" -dn %COMPUTERNAME% -sn "CN=Quickstart Reference Server/DC=%COMPUTERNAME%" -au "urn:localhost:UA:Quickstarts:ReferenceServer"
set CERTSTORE=


