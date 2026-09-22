# Container support and remote debugging for Reference Server

## Overview

There are multiple options to run the reference server in a Docker container:

- Latest and release builds from the GitHub container registry [here](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These builds support only Linux targets.
- Since [this](https://github.com/OPCFoundation/UA-.NETStandard/commit/61a61081f6060804b12b8f351e32a8075703263d) commit the container image has amd64 and arm64 support. The build of the sample has been moved to containers, no more need to install the .NET SDK.
- Local build without .NET 6.0 SDK on Linux or Windows with Docker Desktop. The target OS is chosen based on the settings in Docker Desktop for Linux or Windows containers.
- Although with VS 2019 and greater there is built in Container support, so far issues in the UA Reference solution prevent build/startup/connection (under investigation).
- VS2022 supports native debugging on a Linux distribution with WSL.

## Other published sample images

In addition to the reference server (`refserver`), the [`Images CI`](../.github/workflows/docker-image.yml) workflow builds and publishes every other sample image — the sample servers, the device-integration pump server, the redundant client and both PubSub samples — to the GitHub container registry (`ghcr.io/opcfoundation/uanetstandard/<image>`). All are `linux/amd64` + `linux/arm64`, and all are built by that one workflow:

| Image | Sample application |
| --- | --- |
| `refserver` | `samples/Reference/ConsoleReferenceServer` |
| `ldsserver` | `samples/Lds/ConsoleLdsServer` |
| `boilerserver` | `samples/MinimalApi/MinimalBoilerServer` |
| `calcserver` | `samples/MinimalApi/MinimalCalcServer` |
| `mcpserver` | `tools/Opc.Ua.Mcp` |
| `redundantserver` | `samples/Redundancy/RedundantServer` |
| `redundantclient` | `samples/Redundancy/RedundantClient` |
| `redundantpubsub` | `samples/Redundancy/RedundantPubSub` |
| `pubsubclient` | `samples/PubSub/ConsoleReferencePubSubClient` |
| `pumpserver` | `samples/DI/PumpDeviceIntegrationServer` |

Image tags follow the [release-branch-only publication model](ReleaseProcess.md):

- **`<image>:latest`, `:release`, and the exact `<major>.<minor>` / `<major>.<minor>.<patch>` version tags** are updated only from a stable commit on a canonical `release/<major>.<minor>` branch (see [Release process](ReleaseProcess.md)). A build from that branch that is not yet stable (still `-preview.N`) does **not** move these tags.
- **`<image>:latest-<branch>`** (for example `refserver:latest-master`) tracks the most recent development build on that branch. `Images CI` builds these from `master` and from `release/*` branches while they are pre-release.
- **`<image>:<version>`** (for example `refserver:2.0.0-preview.6` or `refserver:2.0.0`) always identifies the exact package version the image was built with, on every branch.

For example: `docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest` gets the most recently approved stable release; `docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest-master` gets the most recent development build. Each image has a Dockerfile under its application folder that is built from the repository root as context (for example `docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .`).

## Building the local containers

1. Open a command prompt which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
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
