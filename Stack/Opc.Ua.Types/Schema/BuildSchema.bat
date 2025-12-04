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

powershell -NoProfile -Command ^
  "$content = Get-Content -Raw -Encoding UTF8 'UANodeSet.cs';" ^
  "$utf8NoBom = New-Object System.Text.UTF8Encoding $false;" ^
  "[System.IO.File]::WriteAllText('temp1.txt', $content, $utf8NoBom)"

echo #pragma warning disable 1591 > temp2.txt
type temp1.txt >> temp2.txt
type temp2.txt > UANodeSet.cs

if not exist "SecuredApplication.xsd" goto end
echo Processing SecuredApplication Schema
%SVCUTIL% /dconly /namespace:*,Opc.Ua.Security /out:SecuredApplication.cs SecuredApplication.xsd 

powershell -NoProfile -Command ^
  "$content = Get-Content -Raw -Encoding UTF8 'SecuredApplication.cs';" ^
  "$utf8NoBom = New-Object System.Text.UTF8Encoding $false;" ^
  "[System.IO.File]::WriteAllText('temp1.txt', $content, $utf8NoBom)"

echo #pragma warning disable 1591 > temp2.txt
type temp1.txt >> temp2.txt
type temp2.txt > SecuredApplication.cs 

:end

del /Q temp1.txt
del /Q temp2.txt