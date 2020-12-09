#!/bin/bash
echo Run a docker container of the console reference server
echo The certificate store of the ref server is mapped to './OPC Foundation'
sudo docker run -it -p 62541:62541 -h refserver -v "$(pwd)/OPC Foundation:/root/.local/share/OPC Foundation" netcorerefserver:latest
