@echo off
:menu
cls
echo "Select a OPA UA Encoder fuzzing function:"
echo "1. Opc.Ua.BinaryDecoder"
echo "2. Opc.Ua.BinaryEncoder"
echo "3. Opc.Ua.BinaryDecoder Indempotent"
echo "4. Opc.Ua.JsonDecoder"
echo "5. Opc.Ua.JsonEncoder"
echo "6. Opc.Ua.XmlDecoder"
echo "7. Opc.Ua.XmlEncoder"
echo "8. ASN.1 Certificate decoder"
echo "9. ASN.1 Certificate chain decoder"
echo "10. ASN.1 Certificate chain decoder with custom blob parser (macOS)"
echo "11. ASN.1 CRL decoder"
echo "12. ASN.1 CRL encoder"
echo "13. Exit"

set /p choice="Enter your choice (1-13): "

echo Choice %choice%

if "%choice%"=="1" (
    echo "Running libfuzzer with Opc.Ua.BinaryDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryDecoder -corpus ./Fuzz/Testcases.Binary/
) else if "%choice%"=="2" (
    echo "Running libfuzzer with Opc.Ua.BinaryEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryEncoder -corpus ./Fuzz/Testcases.Binary/
) else if "%choice%"=="3" (
    echo "Running libfuzzer with Opc.Ua.BinaryEncoder Indempotent"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryEncoderIndempotent -corpus ./Fuzz/Testcases.Binary/
) else if "%choice%"=="4" (
    echo "Running libfuzzer with Opc.Ua.JsonDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonDecoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
) else if "%choice%"=="5" (
    echo "Running libfuzzer with Opc.Ua.JsonEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonEncoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
) else if "%choice%"=="6" (
    echo "Running libfuzzer with Opc.Ua.XmlDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlDecoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
) else if "%choice%"=="7" (
    echo "Running libfuzzer with Opc.Ua.XmlEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlEncoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
) else if "%choice%"=="8" (
    echo "Running libfuzzer with ASN.1 Certificate decoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateDecoder -corpus ./Fuzz/Testcases.Certificates/
) else if "%choice%"=="9" (
    echo "Running libfuzzer with ASN.1 Certificate chain decoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateChainDecoder -corpus ./Fuzz/Testcases.Certificates/
) else if "%choice%"=="10" (
    echo "Running libfuzzer with ASN.1 Certificate chain decoder with custom blob parser"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateChainDecoderCustom -corpus ./Fuzz/Testcases.Certificates/
) else if "%choice%"=="11" (
    echo "Running libfuzzer with ASN.1 CRL decoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCRLDecoder -corpus ./Fuzz/Testcases.CRLs/
) else if "%choice%"=="12" (
    echo "Running libfuzzer with ASN.1 CRL encoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCRLEncoder -corpus ./Fuzz/Testcases.CRLs/
) else if "%choice%"=="13" (
    echo Exiting.
    exit /b
) else (
    echo Invalid input. Please enter a number between 1 and 13.
)

echo Done