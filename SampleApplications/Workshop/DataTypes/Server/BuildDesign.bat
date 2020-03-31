@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building ModelDesign2
Opc.Ua.ModelCompiler.exe -version v104  -d2 ".\Instances\ModelDesign2.xml" -cg ".\Instances\ModelDesign2.csv" -o2 ".\Instances"
echo Success!


