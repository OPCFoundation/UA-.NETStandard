#!/bin/bash
echo build a docker container of the .NET Core reference server
dotnet build NetCoreReferenceServer.csproj
dotnet publish NetCoreReferenceServer.csproj -o ./publish
sudo docker build -t netcorerefserver .
