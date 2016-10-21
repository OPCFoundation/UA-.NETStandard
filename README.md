# OPC UA .Net Standard Library Stack and Prototype Samples

## Overview
This OPC UA reference implementation is targeting the [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library). .Net Standard allows developing apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

##Features included
1. Fully ported Core UA stack and SDK (Client, Server, Configuration & Sample assemblies)
2. Sample Servers, Clients and Publishers (for sending OPC UA Pub/Sub telemetry data to the cloud), including all required controls, for .Net 4.6, .NetCore and UWP.
3. Sample Global Discovery Server and Client for .Net 4.6 (experimental)
4. X.509 certificate support for client and server authentication
5. Anonymous, username, X.509 certificate (experimental) and JWT (experimental) user authentication
6. UA-TCP & HTTPS transports (client and server)
7. Folder certificate-store support
8. Sessions (including UI support in the samples)
9. Subscriptions (including UI support in the samples)

##Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

<a name="certificates"/>
##How to create self signed certificates for the sample applications

###On Windows
1. Open a command prompt in the root folder of your repository
2. Run the script `CreateAllCerts.cmd` in the root folder of your repository to create the certificates for all sample applications.
3. Alternatively, you can run the script `CreateCert.cmd` in each sample project folder to create new self signed certificates for the application.
4. The self signed certificates are stored in **OPC Foundation/CertificateStores/MachineDefault** in each application project folder

###On Linux
1. Open a command prompt 
2. Navigate to the project folder of the sample app, e.g. **SampleApplications/Samples/NetCoreConsoleClient**
3. Run the script `./createcert.sh` to create the certificates for the sample applications.
4. The self signed certificates are stored in **OPC Foundation/CertificateStores/MachineDefault** in each application project folder

##How to build and run the samples in Visual Studio on Windows

0. Create [certificates](#certificates) for all sample applications.
1. Open the solution UA-NetStandard.sln with VisualStudio.
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.
 
##How to build and run the console samples on Windows, Linux and iOS
This section describes how to run the **NetCoreConsoleClient**, **NetCoreConsolePublisher** and **NetCoreConsoleServer** sample applications.

Please follow instructions in this [article] (https://docs.microsoft.com/en-us/dotnet/articles/core/tutorials/using-with-xplat-cli) to setup the dotnet command line environment for your platform. 

###Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies.
 
### Start the server 
1. Open a command prompt 
2. Now navigate to the folder **SampleApplications/Samples/NetCoreConsoleServer**. 
3. Run the script `./createcert.sh` on Linux or `CreateCert.cmd` on Windows to create the self signed certificate for the command line application.
4. To run the server sample type `dotnet run`. The server is now running and waiting for connections. In this sample configuration the server always accepts new client certificates. 
 
### Start the client 
1. Open a command prompt 
4. Now navigate to the folder **SampleApplications/Samples/NetCoreConsoleClient**. 
5. Run the script `./createcert.sh` on Linux or `CreateCert.cmd` on Windows to create the self signed certificate for the command line application.
6. To execute the sample type `dotnet run` to connect to the OPC UA console sample server running on the same host. To connect to another OPC UA server specify the server as first argument and type e.g. `dotnet run opc.tcp://myserver:51210/UA/SampleServer`.

##How to configure the Publisher samples
So far the Publisher sample application and the OPC UA Telemetry WebApp has been tested end to end against a Microsoft Azure IoTHub instance, as well as against a Microsoft Azure ServiceBus queue.
They should work against any AMQP Broker that provides a standard AMQP 1.0 interface. These AMQP endpoint(s) can be configured via the Opc.UA.Publisher.Config.xml file. The `<AMQPConnectionConfiguration`> element in this file is extensively documented.

* Go to the [Azure portal](https://portal.azure.com/) and create a new [IoTHub](https://azure.microsoft.com/en-us/documentation/articles/iot-hub-csharp-csharp-getstarted/).

* Get [DeviceExplorer](https://github.com/Azure/azure-iot-sdks/blob/master/tools/DeviceExplorer/doc/how_to_use_device_explorer.md) and configure it and connect to the IoTHub you have just created.

* Create a new device in your IoTHub using DeviceExplorer.
 
* Open the solution UA-NetStandard.sln with VisualStudio.

* Open the Samples\XXXPublisher\Opc.Ua.Publisher.Config.xml file to setup your IoTHub connection. In the `<AmqpConnectionConfiguration>` element configure the following elements:
   * `<Host>`{Host}`</Host>`, where {Host} is the Hostname shown on the details page of your IoTHub on the Azure portal (same as the HostName part of the IoT Hub Connection String DeviceExplorer Configuration tab, eg. *myiothub.azure-devices.net*).
   * `<Endpoint>`/devices/{DeviceId}/messages/events`</Endpoint>`, where {DeviceID} is the name of the device you have created with DeviceExplorer (same as the Id of the device in DeviceExplorer Management tab, eg. */devices/myOPCDevice/messages/events*).
   * `<KeyValue>`{KeyValue}`</KeyValue>`, where {KeyValue} is the Primary Key of the device and could be found under Devices->{DeviceId}->Device Details->Primary Key of your IoTHub in the Azure portal (same as the PrimaryKey of your device shown in DeviceExplorer Management tab).
   * `<TokenScope>`{Host}/devices/{DeviceId}`</TokenScope>`, {Host} and {DeviceID} are the same as above, eg. *myiothub.azure-devices.net/devices/myOPCDevice*

* Save the file, rebuild the solution and start it. This will start a local instance of the application.	

* You will get a message that a certificate is missing. Keep this message on the screen while you generate your certificates.

* Open a command prompt and navigate to the Publisher project folder

* Run the script `CreateCert.cmd`. Alternatively, you can run the script `CreateAllCerts.cmd` in the root folder of your repository to create the certificates for all sample applications.

* Copy the "OPC Foundation" folder into the Publisher's binary folder of the path shown in the message.

* Now acknowledge the certificate message in the Opc.Ua.Publisher app and close the application. 

* Restart the Opc.Ua.Publisher application. If you get a message for a missconfigured domain, acknowledge with "Yes" to use the certificate.

* When the Opc.Ua.Publisher application is started from the debugger, the line *13:03:01.079 AMQP Connection opened, connected to '/devices/myOPCDevice/messages/events'...* should be in the output window indicating a succesful connection.
        
* Press "Connect" button to connect to the default endpoint currently displayed, even though is not the AMQP endpoint.

* You should see two dialogs, one after the other, which allows you to select "Security Mode", "Security Policy" and other settings. Just click "OK" twice for now to accept defaults.

* On the left window in the application you see all existing sessions (you are able to connect to multiple OPC UA servers here) and on the right side you can browse the OPC UA node addresss space of the server.

* Choose a node in the right window by browsing to it and selecting it with the mouse (for constantly updating results, choose a value, which changes frequently like Objects->Server->ServerStatus->CurrentTime).
         
* Press the "Publish" button and the application will start publishing the node's Pub/Sub encoded data to your IoTHub.

* In DeviceExplorer go to the Data tab, press the Monitor button and you should see data being received by your IoTHub.

##How to build and run the OPC UA Web Telemetry sample

* Go to the [Azure portal](https://portal.azure.com/) and create a new Storage account.

* Open the solution OpcUaWebTelemetry.sln with VisualStudio 2015.

* Open the MessageProcessing\Configuration.cs file to configure the app to use your Azure resources (Storage account and IoTHub).
```
        // {StorageAccountName} is the name of the storage account and could be found 
        // under Settings->Access keys->Storage account name of your storage account on the Azure portal, eg. *myopcstore*.
        // {AccessKey} is the access key of the storage account and could be found 
        // under Settings->Access keys->key1 of your storage account on the Azure portal.
        public static string StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={AccessKey}";

        // {ConsumerGroupName} is the name of a aonsumer group of your IoTHub. The IoTHub you use is the
        // one you have created for use with the OPC UA Publisher sample.
        // You need to create this consumer group via the messaging settings of your IoTHub in the Azure portal. 
        // We recommend that you do not share this Consumer group with other consumers, nor that you use the $Default consumer group. 
        public static string EventHubConsumerGroup = "{ConsumerGroupName}";

        // {EventHubEndpoint} is the Event Hub compatible endpoint of your IoTHub and could be found 
        // under Settings->Messaging->Event Hub-compatible endpoint of your IoTHub in the Azure portal,
        // eg. *sb://iothub-ns-myiothub-12345-d35c0ac1cab.servicebus.windows.net/*
        // {PrimaryKey} is the IoT Hub primary key for access with iothubowner policy and could be found
        // under Settings->Shared access policies->iothubowner->Primary key of your IoTHub in the Azure portal.  
        public static string EventHubConnectionString = "Endpoint={EventHubEndpoint};SharedAccessKeyName=iothubowner;SharedAccessKey={PrimaryKey}";

        // {HubName} is the Event Hub compatible name of your IoTHub and could be found 
        // under Settings->Messaging->Event Hub-compatible name of your IoTHub in the Azure portal, eg. *myiothub*
        public static string EventHubName = "{HubName}";
```
* Save the file, rebuild the solution and start it. This will start a local instance of the application.

* The solution can also be deployed into a Azure App service. Please use VisualStudio 2015's Azure publishing functionality for this purpose.

* Now run the OPC UA Publisher sample, connect to a OPC UA server and publish a node.

* You should see the node value on the web page after a few seconds.

##License

This repository includes the UA .NetStandard Stack, sample libraries, and sample applications. The UA .NetStandard Stack follows a dual-license:

 * **OPC Foundation Corporate Members**: [RCL](https://opcfoundation.org/license/rcl.html)
 * **Everybody else**: [GPL 2.0](https://opcfoundation.org/license/gpl.html)
 * RCL enables OPC Foundation members to deploy their applications using the UA .NetStandard stack without being required to disclose the application code. Non-members must disclose their application code when using the UA .NetStandard Stack.
 * **Note**: Dual license applies to this repository only; GPL 2.0 applies to all derived repositories (for example 'forks'). For details check the License section below.
 * All samples are provided under the [MIT license](https://opcfoundation.org/license/mit.html).

##Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
