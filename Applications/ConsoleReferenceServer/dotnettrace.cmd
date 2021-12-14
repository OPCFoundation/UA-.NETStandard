REM collect a trace using the EventSource provider OPC-UA-Core
dotnet tool install --global dotnet-trace
dotnet-trace collect --name consolereferenceserver --providers OPC-UA-Core,OPC-UA-Server
