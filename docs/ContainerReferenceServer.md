# Container support and remote debugging for Reference Server

## Overview

Run the Reference Server in a Docker container using a published image or a
local build:

- Pull the latest or a release image from the [GitHub Container Registry](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). These images support Linux targets.
- The image supports `amd64` and `arm64`. The sample builds inside a container, so you do not need to install the .NET SDK. This change was introduced in [this commit](https://github.com/OPCFoundation/UA-.NETStandard/commit/61a61081f6060804b12b8f351e32a8075703263d).
- Build locally with Docker Desktop on Linux or Windows. Docker Desktop settings select the target container OS.
- Visual Studio 2019 and later include container support, but issues in the UA Reference solution currently prevent build, startup, or connection.
- Visual Studio 2022 supports native debugging of a Linux distribution through WSL.

## Contents

- [Overview](#overview)
- [Other published sample images](#other-published-sample-images)
- [Building the local containers](#building-the-local-containers)
- [Run the reference server container](#run-the-reference-server-container)
  - [Run the local build of the Docker container](#run-the-local-build-of-the-docker-container)
  - [Run the prebuilt Docker container hosted on GitHub](#run-the-prebuilt-docker-container-hosted-on-github)
- [Known limitations and issues](#known-limitations-and-issues)

## Other published sample images

The [`Images CI`](../.github/workflows/docker-image.yml) workflow builds
and publishes the sample images below to the GitHub Container Registry
(`ghcr.io/opcfoundation/uanetstandard/<image>`). It builds each image for
`linux/amd64` and `linux/arm64`.

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

The workflow does not compile anything under emulation. Its
`publish-samples` job publishes every sample above once, natively, as
framework-dependent portable IL (no runtime identifier and no app host). The
same output then runs on both platforms. Each `build-and-push-image` leg
downloads that output and builds its Dockerfile with
`--build-arg PUBLISH_SOURCE=prebuilt --build-arg PUBLISH_DIR=<folder>`. That
build only copies files onto the platform's .NET runtime base image, so QEMU
is not needed.

All sample Dockerfiles use the .NET 10 Azure Linux 3 SDK image for building and
an Azure Linux 3 distroless-extra runtime or ASP.NET image for the final image.
The `distroless-extra` variant retains ICU and time-zone data for globalization.
Pump and ModelManagementServer prepare writable directories and the OpenSSL
configuration in a regular Azure Linux layout stage before copying them into
their distroless final images.

A local `docker build` without these arguments compiles from source in the
Dockerfile's `build` stage. That stage is pinned to `$BUILDPLATFORM`, so a
multi-platform `docker buildx build --platform linux/amd64,linux/arm64` also
compiles natively.

Image tags follow the [release-branch-only publication model](ReleaseProcess.md):

- **Stable tags** include `<image>:latest`, `<image>:release`, and version
  tags such as `<major>.<minor>` and `<major>.<minor>.<patch>`. Update them
  only from a stable commit on a canonical `release/<major>.<minor>` branch
  (see [Release process](ReleaseProcess.md)). A preview build (`-preview.N`)
  does **not** move these tags.
- **`<image>:latest-<branch>`** (for example `refserver:latest-master`) tracks the most recent development build on that branch. `Images CI` builds these from `master` and from `release/*` branches while they are pre-release.
- **`<image>:<version>`** (for example `refserver:2.0.0-preview.6` or
  `refserver:2.0.0`) identifies the exact package version used to build the
  image, on any branch.

For example, pull the most recently approved stable Lds server image with:

```sh
docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest
```

Pull the most recent development build with:

```sh
docker pull ghcr.io/opcfoundation/uanetstandard/ldsserver:latest-master
```

Each application folder contains a Dockerfile. Build it from the repository
root so the build can use the repository as context, for example:

```sh
docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .
```

## Building the local containers

1. Open a command prompt which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `dockerbuild.cmd`.

On Linux,

1. Open a shell which can execute docker commands.
2. Navigate to the folder `samples/Reference/ConsoleReferenceServer`.
3. Build the docker container by executing the command `./dockerbuild.sh`.

## Run the reference server container

Run the container in interactive mode with the host's hostname. The
certificate store, log output, and configuration file (when you use `-s`)
are mounted in `./OPC Foundation`.

The container uses these paths by default:

- Certificate store: `./OPC Foundation/pki`
- Log file: `./OPC Foundation/Logs`
- Shadow configuration: `./OPC Foundation/Quickstarts.ReferenceServer.Config.xml`

With `-s`, the container first copies the configuration file to the mounted
folder. On later restarts, the server reads this copy, so you can change the
settings there.

### Run the local build of the Docker container

To run the local containers, batch files are provided called `dockerrun.bat` for Windows and `dockerrun.sh` for Linux.

### Run the prebuilt Docker container hosted on GitHub

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
