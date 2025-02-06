#!/bin/bash

# Function to display the menu
display_menu() {
    echo "Select a OPA UA Encoder fuzzing function:"
    echo "1. Opc.Ua.BinaryDecoder"
    echo "2. Opc.Ua.BinaryEncoder"
    echo "3. Opc.Ua.BinaryEncoder Indempotent"
    echo "3. Opc.Ua.JsonDecoder"
    echo "4. Opc.Ua.JsonEncoder"
    echo "5. Opc.Ua.XmlDecoder"
    echo "6. Opc.Ua.XmlEncoder"
    echo "8. ASN.1 Certificate decoder"
    echo "9. ASN.1 Certificate chain decoder"
    echo "10. ASN.1 Certificate chain decoder with custom blob parser (macOS)"
    echo "11. ASN.1 CRL decoder"
    echo "12. ASN.1 CRL encoder"
    echo "13. Exit"
}

# Function to execute fuzz-afl PowerShell script based on user choice
execute_powershell_script() {
    case $1 in
        1)
            echo "Running libfuzzer with Opc.Ua.BinaryDecoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryDecoder -corpus ./Fuzz/Testcases.Binary/
            ;;
        2)
            echo "Running libfuzzer with Opc.Ua.BinaryEncoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryEncoder -corpus ./Fuzz/Testcases.Binary/
            ;;
        3)
            echo "Running libfuzzer with Opc.Ua.BinaryEncoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzBinaryEncoderIndempotent -corpus ./Fuzz/Testcases.Binary/
            ;;
        4)
            echo "Running libfuzzer with Opc.Ua.JsonDecoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonDecoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
            ;;
        5)
            echo "Running libfuzzer with Opc.Ua.JsonEncoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonEncoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
            ;;
        6)
            echo "Running libfuzzer with Opc.Ua.XmlDecoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlDecoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
            ;;
        7)
            echo "Running libfuzzer with Opc.Ua.XmlEncoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzXmlEncoder -dict ../dictionaries/xml.dict -corpus ./Fuzz/Testcases.Xml/
            ;;
        8)
            echo "Running libfuzzer with ASN.1 Certificate decoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateDecoder -corpus ./Fuzz/Testcases.Certificates/
            ;;
        9)
            echo "Running libfuzzer with ASN.1 Certificate chain decoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateChainDecoder -corpus ./Fuzz/Testcases.Certificates/
            ;;
        10)
            echo "Running libfuzzer with ASN.1 Certificate chain decoder with custom blob parser (macOS)"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCertificateChainDecoderCustom -corpus ./Fuzz/Testcases.Certificates/
            ;;
        11)
            echo "Running libfuzzer with ASN.1 CRL decoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCRLDecoder -corpus ./Fuzz/Testcases.CRLs/
            ;;
        12)
            echo "Running libfuzzer with ASN.1 CRL encoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzCRLEncoder -corpus ./Fuzz/Testcases.CRLs/
            ;;
        *)
            echo "Invalid option. Exiting."
            ;;
    esac
}

# Main 
display_menu

read -p "Enter your choice (1-12): " choice

case $choice in
    1|2|3|4|5|6|7|8|9|10|11|12)
        execute_powershell_script $choice
        ;;
    13)
        echo "Exiting."
        break
        ;;
    *)
        echo "Invalid input. Please enter a number between 1 and 13."
        ;;
esac

echo 
