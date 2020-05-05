
# Official OPC UA .NET Standard Stack and Samples


## Overview
1. Fully ported Core UA stack and SDK (Client, Server, Configuration & Sample assemblies) targeting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/standard/net-standard)
1. An official OPC UA [NuGet](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package of the core, client, server and configuration libraries is available for integration in .NET projects.
1. Sample Servers and Clients, including all required controls, for .NET Framework 4.6.2, .NET Core 2.0 and UWP.

.NET Standard allows you develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. 

One of the reference implementations inside this project has been certified for compliance through an OPC Foundation Certification Test Lab to prove its high quality. Fixes and enhancements since the certification process have been tested and verified for compliance using the Compliance Test Tool (CTT) V1.03.

Furthermore, cloud applications and services (such as ASP.NET, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

The Core UA stack and SDK has been tested with Mono 5.4 to add support for the [Xamarin Client](SampleApplications/Samples/XamarinClient/readme.md) and the Mono console application samples.

For more information and license terms, see http://opcfoundation.github.io/UA-.NETStandard.

#### General Project Info
[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard)
[![Github release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![Nuget Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)

#### Build Status
[![Travis Build Status](https://img.shields.io/travis/OPCFoundation/UA-.NETStandard/master?label=Travis)](https://travis-ci.org/OPCFoundation/UA-.NETStandard)
[![Build Status](https://img.shields.io/azure-devops/build/sysadmin0797/722e4f81-14ec-459b-aed2-c01ea07c7f3b/1/master?label=Azure%20Pipelines)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)
[![Build Status](https://img.shields.io/appveyor/build/opcfoundation-org/ua-netstandardlibrary/master?label=Appveyor)](https://ci.appveyor.com/project/opcfoundation-org/ua-netstandardlibrary)

#### Code Quality
[![Test Status](https://img.shields.io/azure-devops/tests/sysadmin0797/sysadmin/1?style=plastic)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)
[![Coverage Status](https://img.shields.io/azure-devops/coverage/sysadmin0797/sysadmin/1/master?style=plastic)](https://dev.azure.com/sysadmin0797/sysadmin/_build/latest?definitionId=1&_a=summary&repositoryFilter=1&branchFilter=2)


## Features included
#### Stack
1. Fully ported Core UA stack and SDK
1. X.509 certificate support for client and server authentication.
1. SHA-2 support (up to SHA512) including security profile Basic256Sha256 for configurations with high security needs.
1. Anonymous, username and X.509 certificate user authentication.
1. Folder & OS-level (X509Store) certificate-store support.
1. UA-TCP & HTTPS transports (client and server).
1. Sessions (including UI support in the samples).
1. Subscriptions (including UI support in the samples).

#### Samples
1. OPC UA [Reference Server](SampleApplications/Workshop/Reference/README.md).
   - This has been certified for compliance through an OPC Foundation Certification Test Lab. Fixes and enhancements since the certification process have been tested and verified for compliance using the Compliance Test Tool (CTT) Version [1.03.340.380](https://opcfoundation.org/developer-tools/certification-test-tools/ua-compliance-test-tool-uactt/). 
1. OPC UA [Aggregation Server](SampleApplications/Workshop/Aggregation/README.md).
1. [OPC Classic adapter for OPC UA](ComIOP/README.md).
1. OPC UA [Global Discovery Client and Global Discovery Server](SampleApplications/Samples/GDS/README.md).
1. OPC UA [Xamarin Client](SampleApplications/Samples/XamarinClient/readme.md).
1. OPC UA [Quickstart Samples](SampleApplications/Workshop).
   

## Getting Started
#### .NET Core
All the tools you need for .NET Standard come with the .Net Core tools. See [Get started with .NET Core](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

##### Tutorial
Following resources are for [UA-.NET-Legacy](https://github.com/OPCFoundation/UA-.NET-Legacy) but still helpful.

- [Application overview](http://opcfoundation.github.io/UA-.NETStandard/help/index.htm#overview.htm)
   - A listing and brief description of each application within the .NET Solution
- [Walkthrough: Client/Server Connection](http://opcfoundation.github.io/UA-.NETStandard/help/index.htm#getting_started.htm)
   - New users can learn the basics of OPC such as connecting, browsing, reading, writing, and subscribing to nodes etc.
- [Walkthrough: Securing Applications](http://opcfoundation.github.io/UA-.NETStandard/help/index.htm#overviewsecuringapplications.htm)
   - Learn about OPC UA application security by following these step by step instructions showing to establish and break application trusts.
- [Building your first UA Server](http://opcfoundation.github.io/UA-.NETStandard/help/index.htm#server_development.htm)
   - Complete walk-through from creating the project, selecting an application template, building your address space, testing the Server, and deployment.
- [Building your first UA Client](http://opcfoundation.github.io/UA-.NETStandard/help/index.htm#client_development.htm)
   - Complete walk-through from creating the project, selecting an application template, and consuming data.

Please refer README.md in each sample projects as well.

## How to build and run samples
### Using Visual Studio
1. Open the UA-NetStandard.sln solution file using Visual Studio 2017 or later.  
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

### Using 'dotnet' on Windows, Linux and iOS
This section describes how to run the **NetCoreConsoleClient** and **NetCoreConsoleServer** sample applications. For the other projects, please refer README.md in each sample projects.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. As of today .NET Standard 2.0 is required.

#### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore UA-NetStandard.sln`. This command calls into NuGet to restore the tree of dependencies.

#### Start the server 
1. Open a command prompt. 
1. Navigate to the folder **SampleApplications/Samples/NetCoreConsoleServer**. 
1. To run the server sample type `dotnet run --project NetCoreConsoleServer.csproj -a`. 
   - The server is now running and waiting for connections. 
   - The `-a` flag allows to auto accept unknown certificates and should only be used to simplify testing.

#### Start the client 
1. Open a command prompt 
1. Navigate to the folder **SampleApplications/Samples/NetCoreConsoleClient**. 
1. To run the sample type `dotnet run --project NetCoreConsoleClient.csproj -a` to connect to the OPC UA console sample server running on the same host. 
   - The `-a` flag allows to auto accept unknown certificates and should only be used to simplify testing. 
   - To connect to another OPC UA server specify the server as first argument and type e.g. `dotnet run --project NetCoreConsoleClient.csproj -a opc.tcp://myserver:51210/UA/SampleServer`. 
1. If not using the `-a` auto accept option, on first connection, or after certificates were renewed, the server may have refused the client certificate. Check the server and client folder **%LocalApplicationData%/OPC Foundation/CertificateStores/RejectedCertificates** for rejected certificates. To approve a certificate copy it to the **%LocalApplicationData%/OPC Foundation/CertificateStores/UA Applications** folder.
1. Retry step 3 to connect using a secure connection.

### What happened to the OPC UA Web Telemetry sample?
The web telemetry sample was removed as there is a much more complete (and better looking!) solution now available [here](https://github.com/azure/azure-iot-connected-factory). You can try this new solution, called "Connected Factory", out [here](http://www.azureiotsuite.com).


## Remarks

<a name="certificates"/>

### Self signed certificates for the sample applications
All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store.

#### Windows .NET applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\CertificateStores** with a root folder which is specified by the `SpecialFolder` variable **%CommonApplicationData%**. On Windows 7/8/8.1/10 this is usually the invisible folder **C:\ProgramData**. 

Note: Since the sample applications in the UA-.Net repository use the same storage and application names as UA-.NetStandard, but create only certificates with hostname `localhost`, it is recommended to delete all existing certificates in **MachineDefault** to recreate proper certificates for all sample applications when moving to the UA-.NetStandard repository. 

#### Windows UWP applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). 

The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\CertificateStores** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate stores.

#### .NET Standard Console applications on Windows, Linux, iOS etc.
The self signed certificates are stored in a folder called **OPC Foundation/CertificateStores/MachineDefault** with a root folder which is specified by the `SpecialFolder` variable **%LocalApplicationData%** or in a **X509Store** called **CurrentUser\\My**, depending on the configuration. For best cross platform support the personal store **CurrentUser\\My** was chosen to support all platforms with the same configuration. Some platforms, like macOS, do not support arbitrary certificate stores.

The *trusted*, *issuer* and *rejected* stores remain in a shared folder called **OPC Foundation\CertificateStores** with a root folder specified by the `SpecialFolder` variable **%LocalApplicationData%**. Depending on the target platform, this folder maps to a hidden locations under the user home directory.

### Local Discovery Server
By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be downloaded [here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
