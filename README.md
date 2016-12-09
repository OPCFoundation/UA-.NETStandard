# OPC UA .Net Standard Library Stack and Samples

## Overview
This OPC UA reference implementation is targeting the [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library). .Net Standard allows developing apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported. For more information and license terms, see [here](http://opcfoundation.github.io/UA-.NETStandardLibrary).

##Features included
1. Fully ported Core UA stack and SDK (Client, Server, Configuration & Sample assemblies)
2. Sample Servers and Clients, including all required controls, for .Net 4.6, .NetCore and UWP.
3. X.509 certificate support for client and server authentication
4. Anonymous, username, X.509 certificate (experimental) and JWT (experimental) user authentication
5. UA-TCP & HTTPS transports (client and server)
6. Folder certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)

##Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

<a name="certificates"/>
##Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory store and remain in use until deleted from the store.

###Windows .Net applications
By default the self signed certificates are stored in a folder called **OPC Foundation\CertificateStores\MachineDefault** in the release folder where the executable is started. E.g. a debug build would start in **bin\Debug** relative to the project folder.

###Windows UWP applications
By default the self signed certificates are stored in a folder called **OPC Foundation\CertificateStores\MachineDefault** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate store.

###.Net Standard console applications on Windows, Linux, iOS etc.
The self signed certificates are stored in **OPC Foundation/CertificateStores/MachineDefault** in each application project folder

##How to build and run the samples in Visual Studio on Windows

1. Open the solution UA-NetStandard.sln with VisualStudio.
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.
 
##How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the **NetCoreConsoleClient** and **NetCoreConsoleServer** sample applications.

Please follow instructions in this [article] (https://docs.microsoft.com/en-us/dotnet/articles/core/tutorials/using-with-xplat-cli) to setup the dotnet command line environment for your platform. 

###Prerequisites
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

##How to build and run the OPC UA Web Telemetry sample

* Go to the [Azure portal](https://portal.azure.com/) and create a new Storage account.

* Open the solution OpcUaWebTelemetry.sln with VisualStudio 2015.

* Open the MessageProcessing\Configuration.cs file to configure the app to use your Azure resources (Storage account and IoTHub).
```
        // {StorageAccountName} is the name of the storage account and could be found 
        // under Settings->Access keys->Storage account name of your storage account on the Azure portal.
        // {AccessKey} is the access key of the storage account and could be found 
        // under Settings->Access keys->key1 of your storage account on the Azure portal.
        public static string StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={AccessKey}";

        // {ConsumerGroupName} is the name of a aonsumer group of your IoTHub. The IoTHub you use is the
        // one you have created for use with the OPC UA Publisher sample.
        // You need to create this consumer group via the messaging settings of your IoTHub in the Azure portal. 
        // We recommend that you do not share this Consumer group with other consumers, nor that you use the $Default consumer group. 
        public static string EventHubConsumerGroup = "{ConsumerGroupName}";

        // {EventHubEndpoint} is the Event Hub compatible endpoint of your IoTHub and could be found 
        // under Settings->Messaging->Event Hub-compatible endpoint of your IoTHub in the Azure portal.
        // {PrimaryKey} is the IoT Hub primary key for access with iothubowner policy and could be found
        // under Settings->Shared access policies->iothubowner->Primary key of your IoTHub in the Azure portal.  
        public static string EventHubConnectionString = "Endpoint={EventHubEndpoint};SharedAccessKeyName=iothubowner;{PrimaryKey}";

        // {HubName} is the Event Hub compatible name of your IoTHub and could be found 
        // under Settings->Messaging->Event Hub-compatible name of your IoTHub in the Azure portal.
        public static string EventHubName = "{HubName}";
```
* Save the file, rebuild the solution and start it. This will start a local instance of the application.

* The solution can also be deployed into a Azure App service. Please use VisualStudio 2015's Azure publishing functionality for this purpose.

* Now run the OPC UA Publisher sample, connect to a OPC UA server and publish a node.

* You should see the node value on the web page after a few seconds.

##Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
