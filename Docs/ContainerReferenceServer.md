# Container support and remote debugging for Reference Server #

## Overview  ##

There are multiple options to run the reference server in a Docker container:

- Latest and release builds from the GitHub container registry [here](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These builds support only Linux targets.
- Local build using a .NET 6.0 SDK on Linux or Windows with Docker Desktop. The target OS is chosen based on the settings in Docker Desktop for Linux or Windows containers.
- Although with VS 2019 and greater there is built in Container support, so far issues in the UA Reference solution prevent build/startup/connection (under investigation). 
- VS2022 supports native debugging on a Linux distribution with WSL.

## Building the local containers ##

1. Open a Visual Studio command prompt with .NET 6.0 SDK installed.
2. Navigate to the folder `Applications/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell with the latest .NET 6.0 SDK installed.
2. Navigate to the folder `Applications/ConsoleReferenceServer`.
3. Build the docker container by executing the command `./dockerbuild.sh`.

## Run the reference server container

The following samples run the server in interactive mode, hostname is the same as the host, the certificate store, the log output and the configuration file (see option `-s`) are mapped to a folder called `./OPC Foundation`.

the following defaults are used: 
- the certificate store is mapped to './OPC Foundation/pki'
- A log file is created in './OPC Foundation/Logs'
- The shadow configuration file is created in './OPC Foundation/Quickstarts.ReferenceServer.Config.xml'

With the option `-s` the configuration file is first copied to the root of the mapped folders. In subsequent restarts the shadowed configuration file is used when the server is started and all settings can be changed from the mapped configuration file. 

### Run the local build of the Docker container

To run the local containers, batch files are provided called `dockerrun.bat` for Windows and `dockerrun.sh` for Linux. 

### Run the prebuilt Docker container hosted on Github

On Windows, open a command prompt and execute the following commands:
```cmd
docker pull ghcr.io/opcfoundation/uanetstandard/refserver:latest
docker run -it -p 62541:62541 -h %COMPUTERNAME% -v "%CD%/OPC Foundation:/root/.local/share/OPC Foundation" ghcr.io/opcfoundation/uanetstandard/refserver:latest -c -s
```

On Linux, execute the following commands in a shell:
```bash
sudo docker pull ghcr.io/opcfoundation/uanetstandard/refserver:latest
sudo docker run -it -p 62541:62541 -h $HOSTNAME -v "$(pwd)/OPC Foundation:/root/.local/share/OPC Foundation" ghcr.io/opcfoundation/uanetstandard/refserver:latest -c -s
```

## Known limitations and issues

- VS integrated docker build/debug support is not working with the Solution.

  
