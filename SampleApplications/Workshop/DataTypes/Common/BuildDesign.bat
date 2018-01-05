@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building ModelDesign1
Opc.Ua.ModelCompiler.exe -d2 ".\Types\ModelDesign1.xml" -cg ".\Types\ModelDesign1.csv" -o2 ".\Types"
echo Success!





