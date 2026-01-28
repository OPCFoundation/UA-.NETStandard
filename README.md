
# OPC UA .NET Stack

[![Github top language](https://img.shields.io/github/languages/top/OPCFoundation/UA-.NETStandard)](https://github.com/OPCFoundation/UA-.NETStandard) [![Github stars](https://img.shields.io/github/stars/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard) [![Github forks](https://img.shields.io/github/forks/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard) [![Github size](https://img.shields.io/github/repo-size/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard) [![Github release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases) [![Nuget Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) [![Azure DevOps](https://opcfoundation.visualstudio.com/opcua-netstandard/_apis/build/status/OPCFoundation.UA-.NETStandard?branchName=master)](https://opcfoundation.visualstudio.com/opcua-netstandard/_build/latest?definitionId=14&branchName=master) [![Github Actions](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml/badge.svg)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml) [![Tests](https://img.shields.io/azure-devops/tests/opcfoundation/opcua-netstandard/14/master?style=plastic&label=Tests)](https://opcfoundation.visualstudio.com/opcua-netstandard/_test/analytics?definitionId=14&contextType=build) [![CodeQL](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml) [![Coverage Status](https://codecov.io/gh/OPCFoundation/UA-.NETStandard/branch/master/graph/badge.svg?token=vDf5AnilUt)](https://codecov.io/gh/OPCFoundation/UA-.NETStandard) [![Connection Stability](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stability-test.yml/badge.svg)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stability-test.yml)

## Overview

This OPC UA reference implementation targets .NET Framework, .NET, and [.NET Standard 2.1](https://docs.microsoft.com/dotnet/standard/net-standard).

.NET allows to develop apps that run on all common platforms available today, including Linux, iOS, Android (via Xamarin) and Windows without requiring platform-specific modifications.

One of the reference implementations inside this project has been certified for compliance through an OPC Foundation Certification Test Lab to prove its high quality. Fixes and enhancements since the certification process have been tested and verified for compliance using the latest Compliance Test Tool (CTT).

For a comprehensive list of supported [OPC UA Profiles and Facets](Docs/Profiles.md), see the dedicated documentation.

More samples based on the official [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) packages can be found in the [OPC UA .NET Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples) repository. For development there is also a [preview Nuget feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local) available. For local testing a [Docker container of the Reference Server](Docs/ContainerReferenceServer.md) is available for preview and release builds.

### Features included

#### Core and Libraries

* Fully ported Core OPC UA Stack and Libraries (Client, Server, Configuration, Complex Types & GDS assemblies).
* Reference sample Server and Client.
* X.509 [Certificate](Docs/Certificates.md) support for client and server authentication.
* SHA-2 support (up to SHA512) including security profile Basic256Sha256, Aes128Sha256RsaOaep and  Aes256Sha256RsaPss for configurations with high security needs.
* ECC Security policies ECC_nistP256, ECC_nistP384, ECC_brainpoolP256r1 and ECC_brainpoolP384r1.
* Anonymous, username and X.509 certificate user authentication.
* UA-TCP & HTTPS transports (client and server).
* [Reverse Connect](Docs/ReverseConnect.md) for the UA-TCP transport (client and server).
* Folder & OS-level (X509Store) [Certificate Stores](Docs/Certificates.md) with *Global Discovery Server* and *Server Push* support.
* Sessions and (durable) Subscriptions.
* A [PubSub](Docs/PubSub.md) library with samples.

### Key Features and Updates in OPC UA 1.05

* **Security Enhancements**: Improved encryption and authentication mechanisms.
* **CRL Support**: Added Certificate Revocation List support for X509Store on Windows.
* **Performance Improvements**: Faster binary encoding and decoding, reducing memory usage and latency.
* **Role-Based Management**: Support for WellKnownRoles and RoleBasedUserManagement [WellKnownRoles & RoleBasedUserManagement](Docs/RoleBasedUserManagement.md).
* **Improved Logging**: Enhanced logging with `ILogger` and `EventSource`.
* **ECC Profiles**: Support for NIST & Brainpool [Security Profiles](Docs/EccProfiles.md).
* **Durable Subscriptions**: Support for Durable Subscriptions [Durable Subscriptions](Docs/DurableSubscription.md).

#### Breaking Changes and Heads-Up when upgrading from 1.04 to 1.05

* A few features are still missing to fully comply for 1.05, but certification for V1.04 is still possible with the 1.05 release.
* **Thread Safety and Locking**: Improved thread safety and reduced locking in secure channel operations.
* **Audit and Redaction**: New interfaces for auditing and redacting sensitive information.

#### **New in 1.05.378.**

* Support for AsyncNodeManagers in the Server Library, see [Server Async (TAP) Support](Docs/AsyncServerSupport.md)
* Reworked [Observability](Docs/Observability.md) via `ITelemetryContext` in preparation for better DI support. See documentation for breaking changes.

#### **New in 1.05.376.**

* Support for [Durable Subscriptions](Docs/DurableSubscription.md)

#### **New in 1.05.375**

* Support for [ECC Certificates](Docs/EccProfiles.md).

#### **New in 1.05.374.70**

* CRL Support for the X509Store on Windows

#### **New in 1.05.373**

* 1.05 Nodeset
* Support for [WellKnownRoles & RoleBasedUserManagement](Docs/RoleBasedUserManagement.md).

#### **New in 1.04.368**

* Improved support for [Logging](Docs/Observability.md) with `ILogger` and `EventSource`.
* Support for custom certificate stores with refactored `ICertificateStore` and `CertificateStoreType` interface.
* Client and Server support for [TransferSubscriptions](Docs/TransferSubscription.md).
* How to use [Container support](Docs/ContainerReferenceServer.md) with reference server.

#### Samples and Nuget packages

* OPC UA [Console Reference Server](Applications/ConsoleReferenceServer) for .NET. A [Linux Container](https://github.com/OPCFoundation/UA-.NETStandard/pkgs/container/uanetstandard%2Frefserver) of the latest builds is available. See also [Container support](Docs/ContainerReferenceServer.md).
* The OPC UA [Reference Server](Applications/README.md) has been certified for compliance through an OPC Foundation Certification Test Lab. Fixes and enhancements since the certification process have been tested and verified for compliance using the [Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/).
    All releases are verified for compliance with the latest official Compliance Test Tool by the maintainers.
* OPC UA [Console Reference Publisher](Applications/ConsoleReferencePublisher/README.md) and [Console Reference Subscriber](Applications/ConsoleReferenceSubscriber/README.md) for .NET and .NET Framework.
* An official OPC UA [Nuget](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) package of the core, client, server and configuration libraries is available for integration in .NET projects. Note: The package has been split into [Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/), [Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/) and [Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/) packages to reduce the dependencies in projects where only client or server is needed. The [https binding](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/) is now a seperate optional package.
* A [preview Nuget feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local) is available from Azure Devops.

## Getting Started

All the tools you need to build .NET libraries and application come with the .NET SDK. You need the [.net 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build the project. To run applications you need one of the target [.NET runtimes](https://dotnet.microsoft.com/download/dotnet) installed. See [Get started with .NET](https://docs.microsoft.com/dotnet/articles/core/getting-started) for what you need.

## How to build and run the reference samples in Visual Studio on Windows

1. Open the `UA.slnx` solution file using [Visual Studio 2026](https://visualstudio.microsoft.com/downloads/). Ensure you have all target frameworks enabled, including .NET 4.8.x, .NET 8.0, 9.0, and 10.0 (LTS) but Visual Studio will prompt you to install what is missing.
2. Choose a project in the Solution Explorer and set it with a right click as `Startup Project`.
3. Hit `F5` to build and execute the sample.

## How to build and run the console samples on Windows, Linux and iOS

This section describes how to run the **ConsoleReferenceServer** sample application.

Please follow instructions in this [article](https://aka.ms/dotnetcoregs) to setup the .NET 10 SDK which provides the dotnet command that allows you to build and run the samples on your platform. Once the `dotnet` command is available, navigate to the root folder in your local copy of the repository and execute `dotnet restore 'UA.slnx'`. This command calls into NuGet to restore the tree of dependencies.

## Start the server

1. Open a command prompt.
2. Navigate to the folder **Applications/ConsoleReferenceServer**.
3. To run the server sample type `dotnet run --project ConsoleReferenceServer.csproj -a`.
    * The server is now running and waiting for connections.

## Contributing

We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the [contributor license agreement (CLA)](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf) before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request.

## Remarks

### Self signed certificates for the sample applications

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store. Please read [Certificates](Docs/Certificates.md) for more information about certificates and stores

### Local Discovery Server

By default all sample applications are configured to register with a Local Discovery Server (LDS). A reference implementation of a LDS for Windows can be [downloaded here](https://opcfoundation.org/developer-tools/developer-kits-unified-architecture/local-discovery-server-lds). To setup trust with the LDS the certificates need to be exchanged or registration will fail.
