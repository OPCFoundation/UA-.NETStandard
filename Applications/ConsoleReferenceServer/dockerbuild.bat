REM build a docker container of the console reference server
dotnet build NetCoreReferenceServer.csproj
dotnet publish NetCoreReferenceServer.csproj -o ./publish
docker build -t netcorerefserver .
