# OPC UA .Net Standard Library Stack and Samples

## Overview
This OPC UA reference implementation is targeting the [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library). .Net Standard allows developing apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows 7/8/8.1/10 (including embedded/IoT editions) without requiring platform-specific modifications. Furthermore, cloud applications and services (such as ASP.Net, DNX, Azure Websites, Azure Webjobs, Azure Nano Server and Azure Service Fabric) are also supported.

##Features included
1. Fully ported Core UA stack and SDK (Client, Server, Configuration & Sample assemblies)
2. Sample Publishers (for sending OPC UA Pub/Sub telemetry data to the cloud), Clients and Servers, including all required controls
3. X.509 certificate support for client and server authentication
4. Anonymous user authentication
5. UA-TCP & HTTPS transports
6. Folder certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)

##Getting Started
All the tools you need for .Net Standard come with the .Net Core tools. See [here](https://docs.microsoft.com/en-us/dotnet/articles/core/getting-started) for what you need.

## How to run Publisher samples
So far the Publisher sample application and the OPC UA Telemetry WebApp has been tested end to end against a Microsoft Azure IoTHub instance, as well as against a Microsoft Azure ServiceBus queue.
They should work against any AMQP Broker that provides a standard AMQP 1.0 interface. These AMQP endpoint(s) can be configured via the Opc.UA.Publisher.Config.xml file. The `<AMQPConnectionConfiguration`> element in this file is extensively documented.

* Go to the [Azure portal](https://portal.azure.com/) and create a new [IoTHub](https://azure.microsoft.com/en-us/documentation/articles/iot-hub-csharp-csharp-getstarted/).

* Get [DeviceExplorer](https://github.com/Azure/azure-iot-sdks/blob/master/tools/DeviceExplorer/doc/how_to_use_device_explorer.md) and configure it and connect to the IoTHub you have just created.

* Create a new device in your IoTHub using DeviceExplorer.
 
* Open the solution UA-NetStandard.sln with VisualStudio.

* Open the Samples\XXXPublisher\Opc.Ua.Publisher.Config.xml file to setup your IoTHub connection. In the `<AmqpConnectionConfiguration>` element configure the following elements:
   * `<Host>`{Host}`</Host>`, where {Host} is the Hostname shown on the details page of your IoTHub on the Azure portal (same as the HostName part of the IoT Hub Connection String DeviceExplorer Configuration tab).
   * `<Endpoint>`/devices/{DeviceId}/messages/events`</Endpoint>`, where {DeviceID} is the name of the device you have created with DeviceExplorer (same as the Id of the device in DeviceExplorer Management tab).
   * `<KeyValue>`{KeyValue}`</KeyValue>`, where {KeyValue} is the Primary Key of the device and could be found under Devices->{DeviceId}->Device Details->Primary Key of your IoTHub in the Azure portal (same as the PrimaryKey of your device shown in DeviceExplorer Management tab).
   * `<TokenScope>`{Host}/devices/{DeviceId}`</TokenScope>`, {Host} and {DeviceID} are the same as above

* Save the file, rebuild the solution and start it. This will start a local instance of the application.	

* You will get a message that a certificate is missing. Keep this message on the screen while you generate your certificates.

* Get the certificate generator tool from [OPC Misc Tool](https://github.com/OPCFoundation/Misc-Tools.git) . (build the solution and get the Opc.Ua.CerticateGenerator.exe)
    
* Open a command prompt

* Create the folder "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" and use the hostname command to find out the {hostname} to be used below.

* Issue the following two commands:
   ```
   Opc.Ua.CertificateGenerator.exe -cmd issue -sp "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" -an "UA Sample Client" -dn {hostname} -sn "CN=UA Sample Client/DC={hostname}" -au "urn:localhost:OPCFoundation:SampleClient"

   Opc.Ua.CertificateGenerator.exe -cmd issue -sp "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" -an "UA Sample Server" -dn {hostname} -sn "CN=UA Sample Server/DC={hostname}" -au "urn:localhost:OPCFoundation:SampleServer"
   ```
* Copy the "%TEMP%\OPC Foundation" folder into the Publisher's binary folder of the path shown in the message.

* Now acknowledge the certificate message in the Opc.Ua.Publisher app and close the application. 

* Restart the Opc.Ua.Publisher application. If you get a message for a missconfigured domain, acknowledge with "Yes" to use the certificate.
        
* Press "Connect" button to connect to the default endpoint currently displayed. 

* You should see two dialogs, one after the other, which allows you to select "Security Mode", "Security Policy" and other settings. Just click "OK" for now to accept defaults.

* On the left window in the application you see all existing sessions (you are able to connect to multiple OPC UA servers here) and on the right side you can browse the OPC UA node addresss space of the server.

* Choose a node in the right window by browsing to it and selecting it with the mouse (for constantly updating results, choose a value, which changes frequently like Objects->Server->ServerStatus->CurrentTime).
         
* Press the "Publish" button and the application will start publishing the node's Pub/Sub encoded data to your IoTHub.

* In DeviceExplorer go to the Data tab, press the Monitor button and you should see data being received by your IoTHub.



# OPC UA Web Telemetry Sample

## How to build and run the OPC UA Web Telemetry sample

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
        public static string EventHubConnectionString = "{EventHubEndpoint};SharedAccessKeyName=iothubowner;{PrimaryKey}";

        // {HubName} is the Event Hub compatible name of your IoTHub and could be found 
        // under Settings->Messaging->Event Hub-compatible name of your IoTHub in the Azure portal.
        public static string EventHubName = "{HubName}";
```
* Save the file, rebuild the solution and start it. This will start a local instance of the application.

* The solution can also be deployed into a Azure App service. Please use VisualStudio 2015's Azure publishing functionality for this purpose.

* Now run the OPC UA Publisher sample, connect to a OPC UA server and publish a node.

* You should see the node value on the web page after a few seconds.


# License

This repository includes the UA .NetStandard Stack, sample libraries, and sample applications. The UA .NetStandard Stack follows a dual-license:

 * **OPC Foundation Corporate Members**: [RCL](https://opcfoundation.org/license/rcl.html)
 * **Everybody else**: [GPL 2.0](https://opcfoundation.org/license/gpl.html)
 * RCL enables OPC Foundation members to deploy their applications using the UA .NetStandard stack without being required to disclose the application code. Non-members must disclose their application code when using the UA .NetStandard Stack.
 * **Note**: Dual license applies to this repository only; GPL 2.0 applies to all derived repositories (for example 'forks'). For details check the License section below.
 * All samples are provided under the [MIT license](https://opcfoundation.org/license/mit.html).

# Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
