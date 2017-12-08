REM build a docker image of the console reference server
dotnet build ConsoleReferenceServer.csproj
dotnet publish ConsoleReferenceServer.csproj -o ./publish
docker build -t consolerefserver .
