@echo off
setlocal

SET PATH=%PATH%;..\..\..\Scripts;..\..\..\Bin;..\..\..\..\Bin

Opc.Ua.ModelDesigner.exe -d2 ".\ModelDesign.xml" -c ".\ModelDesign.csv" -o ".\" 