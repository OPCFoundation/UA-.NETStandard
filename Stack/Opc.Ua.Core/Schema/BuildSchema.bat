@echo off
setlocal

set XSD=xsd

where %XSD% >nul 2>&1
if not %ERRORLEVEL%==0 (
    echo %XSD% is NOT in the PATH.
    exit 1
)

set SVCUTIL=svcutil

where %SVCUTIL% >nul 2>&1
if not %ERRORLEVEL%==0 (
    echo %SVCUTIL% is NOT in the PATH.
    exit 1
)

echo Processing NodeSet Schema
%XSD% /classes /n:Opc.Ua.Export UANodeSet.xsd

echo #pragma warning disable 1591 > temp.txt
type UANodeSet.cs >> temp.txt
type temp.txt > UANodeSet.cs

echo Processing SecuredApplication Schema
%SVCUTIL% /dconly /namespace:*,Opc.Ua.Security /out:SecuredApplication.cs SecuredApplication.xsd 

echo #pragma warning disable 1591 > temp.txt
type SecuredApplication.cs >> temp.txt
type temp.txt > SecuredApplication.cs 

del /Q temp.txt
