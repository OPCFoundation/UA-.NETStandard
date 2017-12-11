# OPC Foundation UA .Net Standard Global Discovery Server and Client

## Introduction
This Global Discovery Server and Client implement the `Global Discovery and Certificate Management Server` profile as specified the OPC Unified Architecture Specification Part 12: Discovery Release 1.03.
The Solution is split into these projects:
- GlobalDiscoveryServer: Global Discovery Server for .Net 4.6 with SQL server as registration and certificate database.
- GlobalDiscoveryServerLibrary: Common Global Discovery Server classes for .Net 4.6 and .Net Standard.
- NetCoreGlobalDiscoveryServer: Global Discovery Server for .Net Standard with Json database implementation to demonstrate the abstracted database registration and certificate authority interface. (The gdsdb.json is not a secure database and should only be used for testing).
- GlobalDiscoveryClient: Global Discovery Client for .Net 4.6. with Windows forms user interface.
- GlobalDiscoveryClientControls: Global Discovery Client Controls reusable controls for .Net 4.6.
- GlobalDiscoveryClientLibrary: Common Global Discovery Client classes for .Net 4.6 and .Net Standard.
- GlobalDiscoveryClientTest: Unit tests for Global discovery client and server libraries.

## How to build and run the Windows OPC UA Global Discovery Server
1. Open the solution **UA Global Discovery Server.sln** with VisualStudio.
2. Choose the project `GlobalDiscoveryServer` in the Solution Explorer and set it with a right click as `Startup Project`.
3. The server has a dependency on the Entity Framework and SQL server. By default the server connects to the data source `Data Source=(localdb)\MSSQLLocalDB` which is the SQL server installed with Visual Studio. The default location for the database files is the user home directory. To change the data source modify the connection string in the `app.config` file.
4. Hit `Ctrl-F5` to build and execute the sample.

## How to build and run the console OPC UA Global Discovery Server on Windows, Linux and iOS
This section describes how to run the **NetCoreGlobalDiscoveryServer**.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. 

### Start the server 
1. Open a command prompt.
2. Now navigate to the folder **SampleApplications/Samples/GDS/NetCoreGlobalDiscoveryServer**.
3. Execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies. In latest .Net versions this command is optional.
4. To run the server type `dotnet run`. The server is now running and waiting for the connection of a GDS client. 

## GDS Users
The sample GDS servers only implement the username/password authentication. The following combinations can be used to connect to the servers:
- **GDS Administrator:** 
  - Username: **appadmin**, PW: **demo**
  - This user has the ability to register and unregister applications and to issue new certificates. It should be used by the GDS Client application to connect.
- **GDS User:**
  - Username: **appuser**, PW: **demo**
  - This user has only a limited ability to search for applications.
- **System Administrator:** 
  - Username: **sysadmin**, PW: **demo**
  - This user is defined for server push management and has the ability to access the server configuration nodes of the GDS server to update the server certificate and the trust lists. Server push configuration management is not a requirement for a GDS server and only supported here to demonstrate the functionality.

## Certificates
The global discovery server creates the CA certificates for all configured certificate groups on the first start. By default a global discovery server accepts any incoming secure connection with an authenticated user ([GDS Users]). The console server certificates are stored in **%LocalApplicationData%/OPC Foundation/GDS/PKI** while the Windows .Net 4.6 server stores the certificates in **%CommonApplicationData%\OPC Foundation\GDS\PKI**. **%CommonApplicationData%** maps to the path set by the environment variable **ProgramData** on Windows. On Linux and macOS **%LocalApplicationData%** maps to **~/root/.local/share**. On Windows **%LocalApplicationData%** maps to **%USERPROFILE%\AppData\Local**. 

### GDS Certificate stores
Under **PKI**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **own** contains the reference server public certificate and private key.
- **rejected** contains the rejected client certificates. To trust a client certificate, copy the rejected certificate to the **trusted/certs** folder.
- **trusted** contains *trusted* client and CAs certificates and CRLs.
- **issuers** contains CAs certificates and CRLs needed for validation of certificate chains.

### GDS CA Certificate stores
Under **PKI**, the following stores contain certificates under **certs**, CRLs under **crl** or private keys under **private**.
- **authorities** contains the public certificate, CRLs and private keys of the CA authorities.
- **applications** contains the public certificates of all applications regsitered with the GDS.
- **PKI/CA** contains folders for all supported certificate groups. At this point only the `DefaultApplicationGroup` **default** is supported.
  - **PKI/CA/default** contains the *issuer* and *trusted* stores for the default application group. Each store contains the CA certificates and CRLs.

### Customize the GDS CA Certificates
To customize the CA certificate search for `<SubjectName>CN=IOP-2017 CA, O=OPC Foundation</SubjectName>` and enter your new subject. Then search the code and the configuration files for `SomeCompany` and enter your company name as appropriate.

## How to build and run the Windows OPC UA Global Discovery Client
1. Open the solution **UA Global Discovery Server.sln** with VisualStudio.
2. Choose the project `GlobalDiscoveryClient` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `Ctrl-F5` to build and execute the sample.
4. Press the 'Registration' button to connect to a running GDS server. Use the `GDS Administrator` credentials to connect and to be able to register applications and to issue certificates.
5. Select the appropriate `Registration Type`: `ClientPull`, `Server Pull` or `Server Push Management` and proceed in the section below..


### 
