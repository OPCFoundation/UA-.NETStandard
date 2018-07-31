rem start docker with mapped logs
docker run -it -p 58810:58812 -e 58810 -h dockergds -v "/c/GDS:/root/.local/share/OPC Foundation/GDS/Logs" gds:latest
