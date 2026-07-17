REM build a docker container of the console reference server
dotnet tool install --global nbgv
for /f "delims=" %%a in ('nbgv get-version -v Version') do @set Version=%%a
for /f "delims=" %%a in ('nbgv get-version -v SimpleVersion') do @set SimpleVersion=%%a
for /f "delims=" %%a in ('nbgv get-version -v AssemblyInformationalVersion') do @set InformationalVersion=%%a
echo Version Info for docker build: %Version% %SimpleVersion% %InformationalVersion%
docker build --build-arg Version=%Version% --build-arg SimpleVersion=%SimpleVersion% --build-arg InformationalVersion=%InformationalVersion%  -f .\Dockerfile -t consolerefserver .\..\..
