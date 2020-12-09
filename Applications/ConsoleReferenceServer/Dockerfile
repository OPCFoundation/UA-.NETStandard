FROM microsoft/dotnet:2.1-runtime

COPY ./publish /publish
WORKDIR /publish

ENTRYPOINT ["dotnet", "NetCoreReferenceServer.dll"]
