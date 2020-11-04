FROM mcr.microsoft.com/dotnet/core/runtime:3.1

COPY ./publish /publish
WORKDIR /publish

ENTRYPOINT ["dotnet", "NetCoreReferenceServer.dll"]
