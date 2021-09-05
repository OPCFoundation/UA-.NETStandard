# OPC Foundation UA .Net Standard Library Reference Server

## Introduction
This OPC Server is designed to be the default OPC UA Server when opening the [OPC UA Compliance Test Tool](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/) and it uses an address-space that matches the design of the UACTT and the requirements for OPC UA compliance testing. 

It uses the OPC Foundation UA .NET Standard Library. Therefore it supports both the opc.tcp and https transports. There is a .Net 4.6 based server with UI and a .Net Standard 2.0 console version of the server which runs on any OS supporting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/articles/standard).

## How to build and run the Windows OPC UA Reference Server with UACTT
1. Open the solution **UA Reference.sln** with Visual Studio 2017.
2. Choose the project `Reference Server` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console OPC UA Reference Server on Windows, Linux and iOS
This section describes how to run the **ConsoleReferenceServer**.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. 

## Start the server 
1. Open a command prompt.
2. Navigate to the folder **Applications/ConsoleReferenceServer**.
3. To run the server sample type `dotnet run --project NetCoreReferenceServer.csproj`. The server is now running and waiting for the connection of the UACTT. 

## UACTT test certificates
The reference server always rejects new client certificates and requires that the UACTT certificates are in appropriate folders. 
- The console server certificates are stored in **%LocalApplicationData%/OPC Foundation/CertificateStores**.
- The Windows .Net 4.6 server stores the certificates in **%CommonApplicationData%\OPC Foundation\CertificateStores**.
    - **%CommonApplicationData%** maps to the path set by the environment variable **ProgramData** on Windows.  
    - **%LocalApplicationData%** maps to a hidden location in the user home folder and depends on the target platform.

### Certificate stores
Under **CertificateStores**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **MachineDefault** contains the reference server public certificate and private key.
- **RejectedCertificates** contains the rejected client certificates. To trust a client certificate, copy the rejected certificate to the **UA Applications/certs** folder.
- **UA Applications** contains *trusted* client and CAs certificates and CRLs.
- **UA Certificate Authorities** contains CAs certificates and CRLs needed for validation.

### Placing the UACTT certificates
Copy the certificates for testing with the UACTT to the following stores:
- **UA Applications/certs**: expired.der, notyetvalid.der, opcuactt.der, opcuactt_incorrectappuri.der, opcuactt_incorrectip.der, opcuactt_incorrectsign.der, opcuactt_revoked.der, opcuser.der, opcuser_incorrectsign.der, opcuser_revoked.der, opcuser-expired.der, opcuser-notyetvalid.der, opcuactt_ca.der
- **UA Applications/crl**: revocation_list_opcuactt_ca.crl

## UACTT Testing
Download and install the [OPC UA Compliance Test Tool](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/). 

Note: Access to the UACTT is granted to OPC Foundation Corporate Members.

### UACTT sample configuration
A sample configuration for the UACTT Version [1.03.340.380](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/) can be found in [UAReferenceServer.ctt.xml](../UAReferenceServer.ctt.xml). The reference server is tested against the **Standard UA Server** profile, the **Method Server Facet** and the **DataAccess Server Facet**. It is recommended to run the server as retail build with disabled logging, to avoid side effects due to timing artifacts when log entries are written to a disk drive. 

#### Finding the Address Space Configuration Code
- Project: Reference Server
- File: ReferenceNodeManager.cs
- Method: CreateAddressSpace

#### Finding the UA Services
- Project: Opc.Ua.Server
- File: StandardServer.cs


