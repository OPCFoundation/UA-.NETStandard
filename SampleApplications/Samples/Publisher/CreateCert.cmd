@echo off
REM create the app certificate
cd /D %~dp0
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "Opc.Ua.Publisher" -dn %COMPUTERNAME% -sn "CN=Opc.Ua.Publisher/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:AMQPPublisher
set CERTSTORE=


