@echo off
REM create the app certificate
cd /D %~dp0
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -ks 2048 -an "UA Sample Server" -dn %COMPUTERNAME% -sn "CN=UA Sample Server/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:SampleServer
set CERTSTORE=


