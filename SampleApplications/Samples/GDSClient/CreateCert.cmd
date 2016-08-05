REM create the app certificate for NetCoreConsoleClient
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "UA Global Discovery Client" -dn %COMPUTERNAME% -sn "CN=UA Global Discovery Client/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:GdsClient
set CERTSTORE=


