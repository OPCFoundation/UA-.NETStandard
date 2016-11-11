@echo off
REM create the app certificate
cd /D %~dp0
set CERTSTORE=".\OPC Foundation\GDS\PKI\own"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -ks 2048 -an "UA Global Discovery Client" -dn %COMPUTERNAME% -sn "CN=UA Global Discovery Client/DC=%COMPUTERNAME%" -au "urn:%COMPUTERNAME%:OPCFoundation:GdsClient"
set CERTSTORE=


