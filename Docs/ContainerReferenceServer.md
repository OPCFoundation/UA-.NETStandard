# Container support and remote debugging for Reference Server #

## Overview  ##

There are multiple options to run the reference server in a Docker container:

- Latest and release builds from the GitHub container registry [here](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These builds support only Linux targets.
- Local build using a .NET 6.0 SDK on Windows and Linux with Docker Desktop. The target OS is chosen based on the settings in Docker Desktop for Linux or Windows containers.
- Technically starting with VS 2019 there is built in Container support, but so far issues in the UA Reference solution prevent startup/connection (under investigation). 

## Building the local containers ##

1. Open a Visual Studio command prompt.
2. Navigate to the folder `Applications/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell with the latest .NET 6.0 SDK installed.
2. Navigate to the folder `Applications/ConsoleReferenceServer`.
3. Build the docker container by executing the command `./dockerbuild.sh`.

## Running the container server

TODO



## Known limitations and issues

TODO

  
