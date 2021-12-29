REM build a docker container of the console reference server
set buildoptions=--configuration Release -p:NoHttps=true --framework net6.0
dotnet build %buildoptions% ConsoleReferenceServer.csproj
dotnet publish %buildoptions% ConsoleReferenceServer.csproj -o ./publish
docker build -t consolerefserver .
