#!/bin/bash

# Function to display the menu
display_menu() {
    echo "Select a OPA UA Encoder fuzzing function:"
    echo "1. Opc.Ua.BinaryDecoder"
    echo "2. Opc.Ua.BinaryEncoder"
    echo "3. Opc.Ua.JsonDecoder"
    echo "4. Opc.Ua.JsonEncoder"
    echo "5. Exit"
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
            echo "Running libfuzzer with Opc.Ua.JsonDecoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonDecoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
            ;;
        4)
            echo "Running libfuzzer with Opc.Ua.JsonEncoder"
            pwsh ../scripts/fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-ubuntu" -project ./Fuzz/Encoders.Fuzz.csproj -fuzztarget LibfuzzJsonEncoder -dict ../dictionaries/json.dict -corpus ./Fuzz/Testcases.Json/
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
    1|2|3|4)
        execute_powershell_script $choice
        ;;
    5)
        echo "Exiting."
        break
        ;;
    *)
        echo "Invalid input. Please enter a number between 1 and 5."
        ;;
esac

echo 
