@echo off

REM Create all certs for OPC UA apps
cd /D %~dp0

echo Clear all certificate stores and renew all certificates 
echo Press key to continue or Ctrl-C to exit
pause >NUL

rem ensure Opc.Ua.CertificateGenerator.exe exists
where /q Opc.Ua.CertificateGenerator.exe
if errorlevel 1 goto :NeedCertGenerator

echo Creating all applications certificates

For /R .\ %%G IN (CreateCer?.cmd) DO ( 
   pushd "%%~pG"
   cmd /c CreateCert.cmd
   popd
)
cd /D %~dp0

echo All certificates generated

exit /b 0

:NeedCertGenerator
@Echo Opc.Ua.CertificateGenerator.exe not found 
@Echo Please download from https://github.com/OPCFoundation/UA-.NETStandardLibrary
exit /b 1