
# Official OPC UA .Net Standard Stack from the OPC Foundation

## Overview
This OPC UA reference implementation is targeting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/standard/net-standard) specification.

.Net Standard allows you develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. 

One of the reference implementations inside this project has been certified for compliance through an OPC Foundation Certification Test Lab to prove its high quality. Fixes and enhancements since the certification process have been tested and verified for compliance using the Compliance Test Tool (CTT) V1.04.

Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

More samples based on the official [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) packages can be found in the [OPC UA .NET Standard Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples) repository.

## For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandard).

## Features included
1. Fully ported Core OPC UA Stack and Libraries (Client, Server, Configuration, ComplexTypes & GDS assemblies).
2. Reference sample Servers and Clients.
3. X.509 certificate support for client and server authentication.
4. SHA-2 support (up to SHA512) including security profile Basic256Sha256 for configurations with high security needs.
5. Anonymous, username and X.509 certificate user authentication.
6. UA-TCP & HTTPS transports (client and server).
7. Folder & OS-level (X509Store) certificate-store support.
8. Sessions.
9. Subscriptions.
10. OPC UA [Reference Server](Applications/ReferenceServer/README.md).
11. OPC UA [Console Reference Server](Applications/ConsoleReferenceServer).
12. OPC UA [Reference Client](Applications/ReferenceClient).
13. OPC UA [Reference Server](Applications/ReferenceServer/README.md) has been certified for compliance through an OPC Foundation Certification Test Lab. Fixes and enhancements since the certification process have been tested and verified for compliance using the Compliance Test Tool (CTT) Version [1.03.340.380](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/). 
14. An official OPC UA [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package of the core, client, server and configuration libraries is available for integration in .Net projects.

## Project Information

### General Project Info

[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![Nuget Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)

### Build Status

[![Travis Build Status](https://img.shields.io/travis/OPCFoundation/UA-.NETStandard/master?label=Travis)](https://travis-ci.org/OPCFoundation/UA-.NETStandard)
[![Build Status](https://opcfoundation.visualstudio.com/opcua-netstandard/_apis/build/status/OPCFoundation.UA-.NETStandard?branchName=master)](https://opcfoundation.visualstudio.com/opcua-netstandard/_build/latest?definitionId=11&branchName=master)
[![Build Status](https://img.shields.io/appveyor/build/opcfoundation-org/ua-netstandardlibrary/master?label=Appveyor)](https://ci.appveyor.com/project/opcfoundation-org/ua-netstandardlibrary)

### Code Quality

[![Test Status](https://img.shields.io/azure-devops/tests/opcfoundation/opcua-netstandard/11?style=plastic)](https://opcfoundation.visualstudio.com/opcua-netstandard/_test/analytics?definitionId=11&contextType=build)
[![Coverage Status](https://img.shields.io/azure-devops/coverage/opcfoundation/opcua-netstandard/11/master?style=plastic)](https://codecov.io/gh/OPCFoundation/UA-.NETStandard/branch/master)


## Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

## Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store.

## Local Discovery Server
By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be downloaded [here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.

## How to build and run the reference samples in Visual Studio on Windows

1. Open the UA Reference.sln solution file using Visual Studio 2017.  
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the and **NetCoreReferenceServer** sample application.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. As of today .Net Standard 2.0 is required.

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore UA Reference.sln`. This command calls into NuGet to restore the tree of dependencies.

### Start the server 
1. Open a command prompt. 
2. Navigate to the folder **Applications/NetCoreReferenceServer**. 
3. To run the server sample type `dotnet run --project NetCoreReferenceServer.csproj -a`. 
    - The server is now running and waiting for connections. 

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
