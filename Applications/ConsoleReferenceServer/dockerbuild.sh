#!/bin/bash
echo build a docker container of the .NET Core reference server
buildoptions=--configuration Release -p:NoHttps=true --framework net6.0
dotnet build $buildoptions ConsoleReferenceServer.csproj
dotnet publish $buildoptions ConsoleReferenceServer.csproj -o ./publish
sudo docker build -t consolerefserver .
