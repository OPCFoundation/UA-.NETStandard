#!/bin/bash

# Function to display the menu
display_menu() {
    echo "Select a OPA UA Encoder fuzzing function:"
    echo "1. Opc.Ua.BinaryDecoder"
    echo "2. Opc.Ua.BinaryEncoder"
    echo "3. Opc.Ua.JsonDecoder"
    echo "4. Opc.Ua.JsonEncoder"
    echo "5. Opc.Ua.XmlDecoder"
    echo "6. Opc.Ua.XmlEncoder"
    echo "7. Exit"
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
            echo "Running afl-fuzz with Opc.Ua.JsonDecoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Json -x ../dictionaries/json.dict -fuzztarget AflfuzzJsonDecoder
            ;;
        4)
            echo "Running afl-fuzz with Opc.Ua.JsonEncoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Json -x ../dictionaries/json.dict -fuzztarget AflfuzzJsonEncoder
            ;;
        5)
            echo "Running afl-fuzz with Opc.Ua.XmlDecoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Xml -x ../dictionaries/xml.dict -fuzztarget AflfuzzXmlDecoder
            ;;
        6)
            echo "Running afl-fuzz with Opc.Ua.XmlEncoder"
            pwsh ../scripts/fuzz-afl.ps1 ./Fuzz/Encoders.Fuzz.csproj -i ./Fuzz/Testcases.Xml -x ../dictionaries/xml.dict -fuzztarget AflfuzzXmlEncoder
            ;;
        *)
            echo "Invalid option. Exiting."
            ;;
    esac
}

# Main 
display_menu

read -p "Enter your choice (1-5): " choice

case $choice in
    1|2|3|4|5|6)
        execute_powershell_script $choice
        ;;
    7)
        echo "Exiting."
        break
        ;;
    *)
        echo "Invalid input. Please enter a number between 1 and 7."
        ;;
esac

echo 
