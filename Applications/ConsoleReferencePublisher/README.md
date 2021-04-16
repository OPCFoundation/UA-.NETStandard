


# OPC Foundation UA .Net Standard Library - Console Reference Publisher

## Introduction
This OPC application was created to provide the sample code for creating Publisher applications using the OPC Foundation UA .Net Standard PubSub Library. There is a .Net Core 3.1 (2.1) console version of the Publisher which runs on any OS supporting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/articles/standard).
The Reference Publisher is configured to run in parallel with the [Console Reference Subscriber](../ConsoleReferenceSubscriber/README.md)

## How to build and run the console OPC UA Reference Publisher from Visual Studio
1. Open the solution **UA Reference.sln** with Visual Studio 2019.
2. Choose the project `ConsoleReferencePublisher` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console OPC UA Reference Publisher on Windows, Linux and iOS
This section describes how to run the **ConsoleReferencePublisher**.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. 

## Start the Publisher
1. Open a command prompt.
2. Navigate to the folder **Applications/ConsoleReferencePublisher**.
3. To run the Publisher sample type `dotnet run --project ConsoleReferencePublisher.csproj --framework netcoreapp3.1.` 
The Publisher will start and publish network messages that can be consumed by the Reference Subscriber. 
Publisher Initialization

# Programmer's Guide

## Publisher Initialization

The following four steps are required to implement a functional Publisher:

 1. Create [Publisher Configuration](#publisher-configuration).
 
        // Create configuration using UDP protocol and UADP Encoding 
        PubSubConfigurationDataType pubSubConfiguration = CreatePublisherConfiguration_UdpUadp();
    
      Or use the alternative configuration object for MQTT with JSON encoding
                
        // Create configuration using MQTT protocol and JSON Encoding
        PubSubConfigurationDataType pubSubConfiguration = CreatePublisherConfiguration_MqttJson();

    The CreatePublisherConfiguration methods can be found in  [Console Reference Publisher](./Program.cs)

 2. Create an instance of the [UaPubSubApplication Class](../../Docs/PubSub.md#uapubsubapplication-class) using the configuration data from step 1.
        // Create an instance of UaPubSubApplication
        UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration);

 3. Provide the data to be published based on the configuration of published data sets. This step is described in the [Publisher Data](publisher_data.htm) section.
 4. Start PubSub application

        // Start the publisher
        uaPubSubApplication.Start();

After this step the Publisher will publish data as configured.

## Publisher Configuration

The Publisher configuration is a subset of the [PubSub Configuration](../../Docs/PubSub.md#pubsub-configuration). A functional Publisher application needs to have a configuration (PubSubConfgurationDataType instance) that contains a list of published data sets (PublishedDataSetDataType instances) and at least one connection (PubSubConnectionDataType instance) with at least one writer group configuration (WriterGroupDataType instance). The writer group contains at least one data set writer (DataSetWriterDataType instance) pointing to a published data set from the current configuration.

The diagram shows the subset of classes involved in an OPC UA Publisher configuration.

![PublisherConfigClasses](../../Docs/Images/PublisherConfigClasses.png)



