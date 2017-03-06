# OPC Foundation UA .Net Standard Library Reference Server

## Introduction
This OPC Server is designed to be default OPC UA Server when opening the [OPC UA Compliance Test Tool](https://opcfoundation.org/developer-tools/certification-test-tools/ua-compliance-test-tool-uactt/) and uses an address-space that matches the design of the UACTT. 

It is built with the Quickstart application template (OPC Server) and uses the OPC Foundation UA .NET Standard Library SDK. Therefore it supports the opc.tcp and http transports. There is a .Net 4.6 based server with UI and a console version of the server which runs on any OS supporting [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library).

## How to build and run the Windows OPC UA Reference Server with UACTT
1. Open the solution **UA Reference.sln** with VisualStudio.
2. Choose the project `Reference server` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console OPC UA Reference Server on Windows, Linux and iOS
This section describes how to run the **ConsoleReferenceServer**.

Please follow instructions in this [article](https://docs.microsoft.com/en-us/dotnet/articles/core/tutorials/using-with-xplat-cli) to setup the dotnet command line environment for your platform. 

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies.
 
### Start the server 
1. Open a command prompt.
2. Now navigate to the folder **SampleApplications/Workshop/Reference/ConsoleReferenceServer**.
3. To run the server sample type `dotnet run`. The server is now running and waiting for the connection of the UACTT. 

## UACTT test certificates
The reference server always rejects new client certificates and requires a few UACTT certificates in approbriate folders. The console server certificates are stored in **OPC Foundation/CertificateStores** while the Windows .Net 4.6 server stores the certificates in **%CommonApplicationData%\OPC Foundation\CertificateStores**. **%CommonApplicationData%** maps to the path set by the environment variable **ProgramData** on Windows.

### Certificate stores
Under **CertificateStores**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **MachineDefault** contains the reference server public certificate and private key.
- **RejectedCertificates** contains the rejected client certificates. To trust a client certificate, copy the rejected certificate to the **UA Applications/certs** folder.
- **UA Applications** contains trusted client certificates.
- **UA Certificate Authorities** contains trusted CA certificates and CRLs.

### Placing the UACTT certificates
Copy the certificates for testing with the UACTT to the following stores:
- **UA Applications/certs**: expired.der, notyetvalid.der, opcuactt.der, opcuactt_incorrectip.der, cttuser.der
- **UA Certificate Authorities/certs**: opcuactt_ca.der
- **UA Certificate Authorities/crl**: revocation_list_opcuactt_ca.crl

## UACTT Testing
Download and install the [OPC UA Compliance Test Tool](https://opcfoundation.org/developer-tools/certification-test-tools/ua-compliance-test-tool-uactt/). 

Note: Access to the UACTT is granted to OPC Foundation Corporate Members.

### UACTT sample configuration
A sample configuration for the UACTT Version 1.02.336.244 can be found in [UAReferenceServer.ctt.xml](UAReferenceServer.ctt.xml). The reference server is tested against the **Standard UA Server** profile. It is recommended to run the server as retail build with disabled logging, to avoid side effects due to timing artifacts when log entries are written to a disk drive. 

#### Finding the Address Space Configuration Code
- Project: Reference Server
- File: ReferenceNodeManager.cs
- Method: CreateAddressSpace

#### Finding the UA Services
- Project: Opc.Ua.Server
- File: StandardServer.cs


