@echo off
setlocal

echo Processing RegisteredApplication Schema
xsd /classes /n:Opc.Ua.Gds.Client RegisteredApplication.xsd

echo #pragma warning disable 1591 > temp.txt
type RegisteredApplication.cs >> temp.txt
type temp.txt > RegisteredApplication.cs

del temp.txt