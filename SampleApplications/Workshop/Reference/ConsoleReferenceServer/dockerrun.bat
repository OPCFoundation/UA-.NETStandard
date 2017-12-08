REM run a docker image of the console reference server
REM The certificate store of the ref server is mapped to 'c:\OPC Foundation'
docker run -it -p 62541:62541 -h refserver -v "/c/OPC Foundation:/root/.local/share/OPC Foundation" consolerefserver:latest
