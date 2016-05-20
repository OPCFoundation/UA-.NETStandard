# OPC UA Universal Windows Platform Stack and Samples

## Overview
This OPC UA reference implementation is targeting the [Universal Windows Platform (UWP)](https://developer.microsoft.com/en-us/windows/getstarted). UWP allows developing apps that run on all Windows 10 editions (including the IoT editions) without requiring edition-specific modifications.
The OPC Foundation provides an [OPC UA reference implementation for .NET](https://github.com/OPCFoundation/UA-.NET) that supports all versions of Windows Desktop editions since Windows XP. The OPC UA reference implementation for UWP is based on this and has been ported to use UWP API's by Microsoft.
The OPC Foundation will eventually merge the .NET stack and the UWP stack.

Features included:
1. Fully ported Core UA stack and SDK
2. Sample Client and Sample Server, including all required controls
3. X.509 certificate support for client and server authentication
4. Anonymous user authentication
5. UA-TCP transport
6. Folder- and Windows-certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)

This Publishing_Prototype branch furthermore contains the **Publisher sample application** and **OPC UA Telemetry WebApp** as demonstrated at Hannover Fair 2016.  
The Publisher sample application (Opc.Ua.Publisher) is based on the Opc.Ua.SampleClient. It acts as a gateway and allows users to create traditional monitored item subscriptions on existing OPC UA server systems.
Those monitored items are enocoded in JSON and published to one or more configured AMQP endpoint(s).
These AMQP endpoint(s) can be configured via the Opc.UA.Publisher.Config.xml file. The `<AMQPConnectionConfiguration`> element in this file is extensively documented.

So far the Publisher sample application and the OPC UA Telemetry WebApp has been tested end to end against a Microsoft Azure IoTHub instance, as well as against a Microsoft Azure ServiceBus queue.
They should work against any AMQP Broker that provides a standard AMQP 1.0 interface.

RCL enables OPC Foundation members to deploy their applications using the UA UWP stack without being required to disclose the application code. Non-members must disclose their application code when using the UA UWP Stack.

**Note**: Dual license applies to this repository only; GPL 2.0 applies to all derived repositories (for example 'forks'). For details check the License section below.

All samples are provided under the [MIT license](https://opcfoundation.org/license/mit.html).

## How to build and run OPC UA UWP Publisher sample
* Go to the [Azure portal](https://portal.azure.com/) and create a new [IoTHub](https://azure.microsoft.com/en-us/documentation/articles/iot-hub-csharp-csharp-getstarted/).

* Get [DeviceExplorer](https://github.com/Azure/azure-iot-sdks/blob/master/tools/DeviceExplorer/doc/how_to_use_device_explorer.md) and configure it and connect to the IoTHub you have just created.

* Create a new device in your IoTHub using DeviceExplorer.
 
* Open the solution UA-UWP.sln with VisualStudio 2015.

* Open the Samples\Opc.Ua.Publisher\Opc.Ua.Publisher.Config.xml file to setup your IoTHub connection. In the `<AmqpConnectionConfiguration>` element configure the following elements:
   * `<Host>`{Host}`</Host>`, where {Host} is the Hostname shown on the details page of your IoTHub on the Azure portal (same as the HostName part of the IoT Hub Connection String DeviceExplorer Configuration tab).
   * `<Endpoint>`/devices/{DeviceId}/messages/events`</Endpoint>`, where {DeviceID} is the name of the device you have created with DeviceExplorer (same as the Id of the device in DeviceExplorer Management tab).
   * `<KeyValue>`{KeyValue}`</KeyValue>`, where {KeyValue} is the Primary Key of the device and could be found under Devices->{DeviceId}->Device Details->Primary Key of your IoTHub in the Azure portal (same as the PrimaryKey of your device shown in DeviceExplorer Management tab).
   * `<TokenScope>`{Host}/devices/{DeviceId}`</TokenScope>`, {Host} and {DeviceID} are the same as above

* Save the file, rebuild the solution and start it. This will start a local instance of the application.	

* You will get a message, that a certificate is missing. Keep this message on the screen while you generate your certificates.

* Get from [OPC Misc Tool](https://github.com/OPCFoundation/Misc-Tools.git) the Certificate generator tool. (build the solution and get the Opc.Ua.CerticateGenerator.exe)
    
* Open a command prompt

* Create the folder "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" and use the hostname command to find out the {hostname} to be used below.

* Issue the following two commands:
   ```
   Opc.Ua.CertificateGenerator.exe -cmd issue -sp "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" -an "UA Sample Client" -dn {hostname} -sn "CN=UA Sample Client/DC={hostname}" -au "urn:localhost:OPCFoundation:SampleClient"

   Opc.Ua.CertificateGenerator.exe -cmd issue -sp "%TEMP%\OPC Foundation\CertificateStores\MachineDefault" -an "UA Sample Server" -dn {hostname} -sn "CN=UA Sample Server/DC={hostname}" -au "urn:localhost:OPCFoundation:SampleServer"
   ```
* Copy the "%TEMP%\OPC Foundation" folder into the "LocalState" folder of the path shown in the error message (ex: \Users\UserName\AppData\Local\Packages\xxxxx-xxxx-xxx-xxx\LocalState).

* Now acknowledge the certificate message in the Opc.Ua.Publisher app and close the application. 

* Restart the Opc.Ua.Publisher application. Now you should not get a certificate missing message. If you get a message for a missconfigured domain, acknowledge with "Yes" to use the certificate.
        
* If you don't have any OPC UA server device to connect to you may clone the OPC Foundations [.NET repository](https://github.com/OPCFoundation/UA-.NET.git), open the solution "UA Sample Applications.sln" with VisualStudio 2015, build the solution and run the "UA Sample Server" (Note: you need to start this application with Administrator rights).

+ Now you enter a connection URL into the connection URL list box of "Opc.Ua.Publisher" (Note: do a right click into the list box to put the focus on it).   
    
* In our case we are entering the connection URL, which is shown in the "UA Sample Server" application as "Server Endpoint URLs" connection URL list box into the "Opc.Ua.Publisher".

* Press "Connect" button to connect to the "UA Sample Server". 

* You should see a dialog, which allows you to select "Security Mode", "Security Policy" and other settings. Choose your settings and acknowledge with "Ok".

* Then you should see a dialog, to allow entering your username and password. For an anonymous session, you are good to enter nothing and just acknowledge the dialog with "Ok".

* On the left window in the application you see all existing sessions (you are able to connect to multiple OPC UA servers here) and on the right side you could browse the addresss space of the server.

* Choose a node in the right window by browsing to it and selecting it with the mouse (Note: choose a value, which changes frequently like Objects->Server->ServerStatus->CurrentTime).
         
* Press the "Publish" button and the application will start publishing the nodes JSON encoded data (Note: the JSON format is not compliant to the upcoming OPC Foundation PubSub specification) to your IoTHub.

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
        // one you have created for use with the OPC UA UWP Publisher sample.
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

* The solution could also be deployed into a Azure App service. Please use VisualStudio 2015's Azure publishing functionality for this purpose.

* Now run the OPC UA Publisher sample, which could be found [here](https://github.com/OPCFoundation/UA-.UWP-Universal-Windows-Platform.git). Connect to a OPC UA server and publish a node.

* You should see this node value on the web page after some time.


# License
This repository includes the UA .NET Stack, sample libraries, and sample applications. The UA .NET Stack follows a dual-license:

 * **OPC Foundation Corporate Members**: [RCL](https://opcfoundation.org/license/rcl.html)
 * **Everybody else**: [GPL 2.0](https://opcfoundation.org/license/gpl.html)


# Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
