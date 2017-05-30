@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe -d2 ".\Model\ModelDesign.xml" -cg ".\Model\ModelDesign.csv" -o2 ".\Model"
echo Success!

copy .\Model\*.Constants.cs ..\Client
copy .\Model\*.DataTypes.cs ..\Client


