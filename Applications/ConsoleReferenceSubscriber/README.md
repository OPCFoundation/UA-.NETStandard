
# OPC Foundation UA .Net Standard Library - Console Reference Subscriber

## Introduction
This OPC application was created to provide the sample code for creating Subscriber applications using the OPC Foundation UA .Net Standard PubSub Library. There is a .Net Core 3.1 (2.1) console version of the Subscriber which runs on any OS supporting [.NET Standard](https://docs.microsoft.com/en-us/dotnet/articles/standard).
The Reference Subscriber is configured to run in parallel with the [Console Reference Publisher](../ConsoleReferencePublisher/README.md)

## How to build and run the Windows OPC UA Reference Server from Visual Studio
1. Open the solution **UA Reference.sln** with Visual Studio 2019.
2. Choose the project `ConsoleReferenceSubscriber` in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console OPC UA Reference Subscriber on Windows, Linux and iOS
This section describes how to run the **ConsoleReferenceSubscriber**.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the dotnet command line environment for your platform. 

## Start the Subscriber
1. Open a command prompt.
2. Navigate to the folder **Applications/ConsoleReferenceSubscriber**.
3. To run the Subscriber sample type `dotnet run --project ConsoleReferenceSubscriber.csproj --framework netcoreapp3.1.` 
The Subscriber will start and listen for network messages sent by the Reference Publisher. 
