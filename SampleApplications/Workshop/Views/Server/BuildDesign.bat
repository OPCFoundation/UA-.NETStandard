@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building OperationsDesign
Opc.Ua.ModelCompiler.exe -d2 ".\Model\OperationsDesign.xml" -cg ".\Model\OperationsDesign.csv" -o2 ".\Model"
echo Success!

echo Building EngineeringDesign
Opc.Ua.ModelCompiler.exe -d2 ".\Model\EngineeringDesign.xml" -cg ".\Model\EngineeringDesign.csv" -o2 ".\Model"
echo Success!

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe -d2 ".\Model\ModelDesign.xml" -cg ".\Model\ModelDesign.csv" -o2 ".\Model"
echo Success!



