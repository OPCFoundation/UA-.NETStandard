@echo off
REM create the app certificate for NetCoreConsoleClient
cd /D %~dp0
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "UA Sample Client" -dn %COMPUTERNAME% -sn "CN=UA Sample Client/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:SampleClient
set CERTSTORE=


