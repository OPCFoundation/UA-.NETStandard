@echo off
:menu
cls
echo "Select a OPA UA Encoder fuzzing function:"
echo "1. Opc.Ua.BinaryDecoder"
echo "2. Opc.Ua.BinaryEncoder"
echo "3. Opc.Ua.JsonDecoder"
echo "4. Opc.Ua.JsonEncoder"
echo "5. Opc.Ua.XmlDecoder"
echo "6. Opc.Ua.XmlEncoder"
echo "7. Exit"

set /p choice="Enter your choice (1-7): "

if "%choice%"=="1" (
    echo "Running libfuzzer with Opc.Ua.BinaryDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryDecoder -corpus ./Fuzz/Testcases.Binary/
) else if "%choice%"=="2" (
    echo "Running libfuzzer with Opc.Ua.BinaryEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryEncoder -corpus ./Fuzz/Testcases.Binary/
) else if "%choice%"=="3" (
    echo "Running libfuzzer with Opc.Ua.JsonDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonDecoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
) else if "%choice%"=="4" (
    echo "Running libfuzzer with Opc.Ua.JsonEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonEncoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
) else if "%choice%"=="5" (
    echo "Running libfuzzer with Opc.Ua.XmlDecoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlDecoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
) else if "%choice%"=="6" (
    echo "Running libfuzzer with Opc.Ua.XmlEncoder"
    powershell.exe -File ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlEncoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
) else if "%choice%"=="7" (
    echo Exiting.
    exit /b
) else (
    echo Invalid input. Please enter a number between 1 and 7.
)

echo Done