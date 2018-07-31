dotnet build NetCoreGlobalDiscoveryServer.csproj
dotnet publish NetCoreGlobalDiscoveryServer.csproj -o ./publish
docker build -t gds .
