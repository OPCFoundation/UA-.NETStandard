REM create the app certificate for NetCoreConsoleClient
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
..\..\..\Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "UA Global Discovery Server" -dn %COMPUTERNAME% -sn "CN=UA Global Discovery Server/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:GlobalDiscoveryServer
set CERTSTORE=


