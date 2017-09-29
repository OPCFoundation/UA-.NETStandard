# OPC UA .Net Standard Library Stack and Samples

## Overview
This OPC UA reference implementation is targeting the [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library). .Net Standard allows developing apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported. For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandardLibrary).

## Features included
1. Fully ported Core UA stack and SDK (Client, Server, Configuration & Sample assemblies)
2. Sample Servers and Clients, including all required controls, for .Net 4.6, .NetCore and UWP.
3. X.509 certificate support for client and server authentication
4. Anonymous, username, X.509 certificate (experimental) and JWT (experimental) user authentication
5. UA-TCP & HTTPS transports (client and server)
6. Folder & OS-level (X509Store) certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)
9. OPC UA [Reference Server](SampleApplications/Workshop/Reference/README.md), [Aggregation Server](SampleApplications/Workshop/Aggregation/README.md) and [OPC Classic Adapter](ComIOP/README.md) samples

## Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

<a name="certificates"/>

## Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory store and remain in use until deleted from the store.

### Windows .Net applications
By default the self signed certificates are stored in a folder called **OPC Foundation\CertificateStores\MachineDefault** in a root folder which is specified by the environment variable **ProgramData**. On Windows 7/8/8.1/10 this is usually the invisible folder **C:\ProgramData**. 
Note: Since the sample applications in the UA-.Net repository use the same storage and application names as UA-.NetStandardLibrary, but create only certificates with hostname `localhost`, it is recommended to delete all existing certificates in **MachineDefault** to recreate proper certificates for all sample applications when moving to the UA-.NetStandardLibrary repository. 

### Windows UWP applications
By default the self signed certificates are stored in a folder called **OPC Foundation\CertificateStores\MachineDefault** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate store.

### .Net Standard Console applications on Windows, Linux, iOS etc.
The self signed certificates are stored in **OPC Foundation/CertificateStores/MachineDefault** in each application project folder

## Local Discovery Server
By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be downloaded from [here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.

## How to build and run the samples in Visual Studio on Windows

1. Open the UA-NetStandard.sln solution file using Visual Studio 2017.  
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.
 
## How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the **NetCoreConsoleClient** and **NetCoreConsoleServer** sample applications.

Please follow instructions in this [article](https://docs.microsoft.com/en-us/dotnet/articles/core/tutorials/using-with-xplat-cli) to setup the dotnet command line environment for your platform. 

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies.
 
### Start the server 
1. Open a command prompt 
2. Now navigate to the folder **SampleApplications/Samples/NetCoreConsoleServer**. 
3. To run the server sample type `dotnet run`. The server is now running and waiting for connections. In this sample configuration the server always rejects new client certificates. 
 
### Start the client 
1. Open a command prompt 
2. Now navigate to the folder **SampleApplications/Samples/NetCoreConsoleClient**. 
3. To execute the sample type `dotnet run` to connect to the OPC UA console sample server running on the same host. To connect to another OPC UA server specify the server as first argument and type e.g. `dotnet run opc.tcp://myserver:51210/UA/SampleServer`.
4. On first connection, or after certificates were renewed, the server may have refused the client certificate. Check the server and client folder **OPC Foundation\CertificateStores\RejectedCertificates** for rejected certificates. To approve a certificate copy it to the **OPC Foundation\CertificateStores\UA Applications** folder.
5. Retry step 3 to connect using a secure connection.

## How to build and run the OPC UA COM Server Wrapper
- Please refer to the OPC Foundation UA .Net Standard Library [COM Server Wrapper](ComIOP/README.md) for a detailed description how to run the OPC COM wrapper.

## How to build and run the OPC UA Aggregation Client and Server
- Please refer to the OPC Foundation UA .Net Standard Library [Aggregation Client and Server](SampleApplications/Workshop/Aggregation/README.md) for a detailed description how to run the aggregation client and server.

## How to build and run the OPC UA Reference Server with UACTT
- Please refer to the OPC Foundation UA .Net Standard Library [Reference Server](SampleApplications/Workshop/Reference/README.md) for a detailed description how to run the reference server against the UACTT.

## What happened to the OPC UA Web Telemetry sample?
The web telemetry sample was removed as there is a much more complete (and better looking!) solution now available [here](https://github.com/azure/azure-iot-connected-factory). You can try this new solution, called "Connected Factory", out [here](http://www.azureiotsuite.com).

## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
