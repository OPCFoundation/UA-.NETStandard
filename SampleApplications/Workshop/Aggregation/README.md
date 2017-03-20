# OPC Foundation UA .Net Standard Library Aggregation Client and Server

## Introduction
This OPC aggregation server is by default configured to aggregate the **UA Sample Server** and **UA Sample Client**. Once the **Aggregation Server** is started, the aggregated OPC UA servers can be viewed in the namespace of the **Aggregation Server** with the **Aggregation Client** or any other OPC UA client.

They are built with the Quickstart application template (OPC Client/Server) and use the OPC Foundation UA .NET Standard Library SDK. Therefore they support the opc.tcp and http transports. 

There is a .Net 4.6 based aggregation server with UI and a console version of the aggregation server which runs on any supported OS for [.NET Standard Library](https://docs.microsoft.com/en-us/dotnet/articles/standard/library).

## How to build and run the OPC UA Aggregation Server

### Prerequisite: Build and run the aggregated servers
1. Open the solution **UA-NetStandard.sln** with VisualStudio.
2. Choose the project **UA Sample Server** in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F7` to build the solution and all samples.
4. Hit `Ctrl-F5` and execute the **UA Sample Server** sample.
5. Choose the project **UA Sample Client** in the Solution Explorer and set it with a right click as `Startup Project`.
6. Hit `Ctrl-F5` and execute the **UA Sample Client** sample.

## Build and run the OPC UA Aggregation Server
Pick the Windows or the Console OPC UA Aggregation Server. 

### Build and run the Windows OPC UA Aggregation Server
1. Open the solution **UA Aggregation.sln** with VisualStudio.
2. Choose the project **Aggregation Server** in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

### Build and run the Console OPC UA Aggregation Server on Windows, Linux and iOS
This section describes how to run the **ConsoleAggregationServer**.

Please follow instructions in this [article](https://docs.microsoft.com/en-us/dotnet/articles/core/tutorials/using-with-xplat-cli) to setup the dotnet command line environment for your platform. 

### Prerequisites
1. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore`. This command calls into NuGet to restore the tree of dependencies.
 
### Start the server 
1. Open a command prompt.
2. Now navigate to the folder **SampleApplications/Workshop/Reference/ConsoleReferenceServer**.
3. To run the server sample type `dotnet run`. 

The server is now running, connecting to the aggregated servers and waiting for the connection of a OPC UA client. 

## Build and run the OPC UA Aggregation Client
1. Open the solution **UA Aggregation.sln** with VisualStudio.
2. Choose the project **Aggregation Client** in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.
4. Press `Connect` to connect to the **Aggregation Server**.
5. Browse the aggregation server and access the **UA Sample Client** or the **UA Sample Server** in the namespace of the **Aggregation Server**.

