msbuild /p:configuration=Debug /t:clean,restore,build "..\..\UA Quickstart Applications.sln"
msbuild /p:configuration=Release /t:build "..\..\UA Quickstart Applications.sln"

