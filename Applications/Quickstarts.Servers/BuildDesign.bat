@echo off
setlocal

REM SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building TestData
Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\TestData\TestDataDesign.xml" -cg ".\TestData\TestDataDesign.csv" -o2 ".\TestData"
echo Success!

echo Building MemoryBuffer
Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\MemoryBuffer\MemoryBufferDesign.xml" -cg ".\MemoryBuffer\MemoryBufferDesign.csv" -o2 ".\MemoryBuffer" 
echo Success!

echo Building BoilerDesign
Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\Boiler\BoilerDesign.xml" -c ".\Boiler\BoilerDesign.csv" -o2 ".\Boiler"
echo Success!
