#!/bin/bash

# Function to display the menu
display_menu() {
    echo "Select a OPA UA Encoder fuzzing function:"
    echo "1. Opc.Ua.BinaryDecoder"
    echo "2. Opc.Ua.BinaryEncoder"
    echo "3. Opc.Ua.BinaryEncoder Indempotent"
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
}

# Function to execute fuzz-afl PowerShell script based on user choice
execute_powershell_script() {
    case $1 in
        1)
            echo "Running afl-fuzz with Opc.Ua.BinaryDecoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Binary -fuzztarget AflfuzzBinaryDecoder
            ;;
        2)
            echo "Running afl-fuzz with Opc.Ua.BinaryEncoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Binary -fuzztarget AflfuzzBinaryEncoder
            ;;
        3)
            echo "Running afl-fuzz with Opc.Ua.BinaryEncoder Indempotent"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Binary -fuzztarget AflfuzzBinaryEncoderIndempotent
            ;;
        4)
            echo "Running afl-fuzz with Opc.Ua.JsonDecoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Json -x ../dictionaries/json.dict -fuzztarget AflfuzzJsonDecoder
            ;;
        5)
            echo "Running afl-fuzz with Opc.Ua.JsonEncoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Json -x ../dictionaries/json.dict -fuzztarget AflfuzzJsonEncoder
            ;;
        6)
            echo "Running afl-fuzz with Opc.Ua.XmlDecoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Xml -x ../dictionaries/xml.dict -fuzztarget AflfuzzXmlDecoder
            ;;
        7)
            echo "Running afl-fuzz with Opc.Ua.XmlEncoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Xml -x ../dictionaries/xml.dict -fuzztarget AflfuzzXmlEncoder
            ;;
        8)
            echo "Running afl-fuzz with Certificate decoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Certificates -fuzztarget AflfuzzCertificateDecoder
            ;;
        9)
            echo "Running afl-fuzz with Certificate chain decoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Certificates -fuzztarget AflfuzzCertificateChainDecoder
            ;;
        10)
            echo "Running afl-fuzz with Certificate chain decoder and custom blob parser"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Certificates -fuzztarget AflfuzzCertificateChainDecoderCustom
            ;;
        11)
            echo "Running afl-fuzz with CRL Decoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.CRLs -fuzztarget AflfuzzCRLDecoder
            ;;
        12)
            echo "Running afl-fuzz with CRL Encoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.CRLs -fuzztarget AflfuzzCRLEncoder
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
        echo "Invalid input. Please enter a number between 1 and 12."
        ;;
esac

echo 
