FROM mcr.microsoft.com/dotnet/runtime:6.0

COPY ./publish /publish
WORKDIR /publish

ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
