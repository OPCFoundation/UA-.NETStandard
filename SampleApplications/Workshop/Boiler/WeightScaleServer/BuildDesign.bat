@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\Bin;..\..\..\Bin

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe -d2 ".\ModelDesign.xml" -cg ".\ModelDesign.csv" -o2 ".\"
echo Success!

copy Quickstarts.Boiler.Constants.cs ..\Client
copy Quickstarts.Boiler.DataTypes.cs ..\Client


