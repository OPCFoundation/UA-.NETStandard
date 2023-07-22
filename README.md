
# Official OPC UA .NET Standard Stack from the OPC Foundation

## Overview
This OPC UA reference implementation is targeting the [.NET Standard](https://docs.microsoft.com/en-us/dotnet/standard/net-standard) specification.

.NET Standard allows to develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10/11 (including embedded/IoT editions) without requiring platform-specific modifications. 

One of the reference implementations inside this project has been certified for compliance through an OPC Foundation Certification Test Lab to prove its high quality. Fixes and enhancements since the certification process have been tested and verified for compliance using the latest Compliance Test Tool (CTT).

Furthermore, cloud applications and services (such as ASP.NET, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

More samples based on the official [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) packages can be found in the [OPC UA .NET Standard Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples) repository. For development there is also a [preview Nuget feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local) available. For local testing a [Docker container of the Reference Server](Docs/ContainerReferenceServer.md) is available for preview and release builds.

## For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandard).

### Features included

#### Core and Libraries

* Fully ported Core OPC UA Stack and Libraries (Client, Server, Configuration, Complex Types & GDS assemblies).
* Reference sample Server and Client. 
* X.509 [Certificate](Docs/Certificates.md) support for client and server authentication.
* SHA-2 support (up to SHA512) including security profile Basic256Sha256, Aes128Sha256RsaOaep and  Aes256Sha256RsaPss for configurations with high security needs.
* Anonymous, username and X.509 certificate user authentication.
* UA-TCP & HTTPS transports (client and server).
* [Reverse Connect](Docs/ReverseConnect.md) for the UA-TCP transport (client and server).
* Folder & OS-level (X509Store) [Certificate Stores](Docs/Certificates.md) with *Global Discovery Server* and *Server Push* support.
* Sessions and Subscriptions.
* A [PubSub](Docs/PubSub.md) library with samples.

#### **New in 1.4.368**
* Improved support for [Logging](Docs/Logging.md) with `ILogger` and `EventSource`. 
* Support for custom certificate stores with refactored `ICertificateStore` and `CertificateStoreType` interface.
* Client and Server support for [TransferSubscriptions](Docs/TransferSubscription.md).
* How to use [Container support](Docs/ContainerReferenceServer.md) with reference server.

#### Samples and Nuget packages

* OPC UA [Reference Server](Applications/ReferenceServer) and [Reference Client](Applications/ReferenceClient) for .NET Framework.
* OPC UA [Console Reference Server](Applications/ConsoleReferenceServer) for .NET Core. A Linux Container of the latest builds is available [here](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver). See also [Container support](Docs/ContainerReferenceServer.md).
* The OPC UA [Reference Server](Applications/ReferenceServer/README.md) has been certified for compliance through an OPC Foundation Certification Test Lab. Fixes and enhancements since the certification process have been tested and verified for compliance using the [Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/). 
    All releases are verified for compliance with the latest official Compliance Test Tool by the maintainers.
* OPC UA [Console Reference Publisher](Applications/ConsoleReferencePublisher/README.md) and [Console Reference Subscriber](Applications/ConsoleReferenceSubscriber/README.md) for .NET Core and .NET Framework.
* An official OPC UA [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package of the core, client, server and configuration libraries is available for integration in .NET projects. Note: The package has been split into [Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/), [Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/) and [Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/) packages to reduce the dependencies in projects where only client or server is needed. The [https binding](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/) is now a seperate optional package.
* A [preview Nuget feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local) is available from Azure Devops.

## Project Information

### General Project Info
[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![Nuget Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)

### Build Status
[![Azure DevOps](https://opcfoundation.visualstudio.com/opcua-netstandard/_apis/build/status/OPCFoundation.UA-.NETStandard?branchName=master)](https://opcfoundation.visualstudio.com/opcua-netstandard/_build/latest?definitionId=14&branchName=master)
[![Github Actions](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml/badge.svg)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml)

### Code Quality
[![Test Status](https://img.shields.io/azure-devops/tests/opcfoundation/opcua-netstandard/14?style=plastic)](https://opcfoundation.visualstudio.com/opcua-netstandard/_test/analytics?definitionId=14&contextType=build)
[![CodeQL](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml)
[![Coverage Status](https://codecov.io/gh/OPCFoundation/UA-.NETStandard/branch/master/graph/badge.svg?token=vDf5AnilUt)](https://codecov.io/gh/OPCFoundation/UA-.NETStandard)

## Getting Started
All the tools you need for .NET Standard come with the .NET Core tools. See [Get started with .NET Core](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

## How to build and run the reference samples in Visual Studio on Windows

Note: Since .NET Core 2.1 is end of life, 
- VS 2017 has only limited support for .NET 4.8. 
- VS 2019 is fully supported with .NET 4.8 and up to .NET Core 3.1 (end of life). 
- VS 2022 is the current supported version, including .NET 6.0 (LTS). 

1. Open the UA Reference.sln solution file using Visual Studio. 
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the **ConsoleReferenceServer** sample application.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. As of today .NET Core SDK 3.1 is required for Visual Studio 2019 and .NET SDK 6.0 is required for Visual Studio 2022.

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore 'UA Reference.sln'`. This command calls into NuGet to restore the tree of dependencies.

### Start the server 
1. Open a command prompt. 
2. Navigate to the folder **Applications/ConsoleReferenceServer**. 
3. To run the server sample type `dotnet run --project ConsoleReferenceServer.csproj -a`. 
    - The server is now running and waiting for connections. 

## Remarks

### Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store. Please read [Certificates](Docs/Certificates.md) for more information about certificates and stores

### Local Discovery Server

By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be downloaded [here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.

## Contributing

We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
