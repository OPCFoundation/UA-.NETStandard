REM create the app certificate for NetCoreConsoleServer
set CERTSTORE=".\OPC Foundation\CertificateStores\MachineDefault"
rd /S/Q %CERTSTORE%
md %CERTSTORE%
Opc.Ua.CertificateGenerator.exe -cmd issue -sp %CERTSTORE% -an "UA Sample Server" -dn %COMPUTERNAME% -sn "CN=UA Sample Server/DC=%COMPUTERNAME%" -au "urn:localhost:OPCFoundation:SampleServer
set CERTSTORE=


