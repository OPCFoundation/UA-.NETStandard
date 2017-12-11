#!/bin/bash
echo build a docker container of the console reference server
dotnet build ConsoleReferenceServer.csproj
dotnet publish ConsoleReferenceServer.csproj -o ./publish
sudo docker build -t consolerefserver .
