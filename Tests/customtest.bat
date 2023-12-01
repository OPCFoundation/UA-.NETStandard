REM This script is used to run custom platform tests for the UA Core Library
REM Supported parameters: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0
set CustomTestTarget=%1
dotnet restore -f "..\UA Core Library.sln"
dotnet build --no-restore "..\UA Core Library.sln"
dotnet test "..\UA Core Library.sln"
