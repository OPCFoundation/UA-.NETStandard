@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building Boiler
Opc.Ua.ModelCompiler.exe -version v104  -d2 ".\Boiler\BoilerDesign.xml" -cg ".\Boiler\BoilerDesign.csv" -o2 ".\Boiler"

echo Building TestData
Opc.Ua.ModelCompiler.exe -version v104  -d2 ".\TestData\TestDataDesign.xml" -cg ".\TestData\TestDataDesign.csv" -o2 ".\TestData"

echo Building MemoryBuffer
Opc.Ua.ModelCompiler.exe -version v104  -d2 ".\MemoryBuffer\MemoryBufferDesign.xml" -cg ".\MemoryBuffer\MemoryBufferDesign.csv" -o2 ".\MemoryBuffer"

echo Success!


