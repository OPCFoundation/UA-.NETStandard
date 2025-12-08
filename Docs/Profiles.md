# OPC UA Profiles and Facets Support

This document describes which [OPC UA Profiles and Facets](https://profiles.opcfoundation.org/) are implemented in the OPC UA .NET Standard Stack.

## Overview

The OPC UA .NET Standard Stack is a reference implementation that targets OPC UA specification version 1.05. It has been certified for compliance through an OPC Foundation Certification Test Lab and is continuously tested for compliance using the latest Compliance Test Tool (CTT).

For a complete list of all OPC UA profiles, visit the [OPC Foundation Profile Reporting](https://profiles.opcfoundation.org/profile/) website.

## Server Profiles

The Reference Server implementation supports the following OPC UA Server profiles:

### Core Server Profiles

- **[Standard UA Server Profile (2017)](http://opcfoundation.org/UA-Profile/Server/StandardUA2017)** - The core OPC UA Server profile that includes:
  - Basic server capabilities
  - Discovery services
  - Session management
  - Subscription management
  - MonitoredItem services
  - View services (Browse, BrowseNext, TranslateBrowsePathsToNodeIds)
  - Attribute services (Read, Write, HistoryRead, HistoryUpdate)
  - Query services

### Functional Facets

- **[Data Access Server Facet](http://opcfoundation.org/UA-Profile/Server/DataAccess)** - Support for data access functionality including variables, data types, and data change notifications

- **[Method Server Facet](http://opcfoundation.org/UA-Profile/Server/Methods)** - Support for calling methods on objects in the address space

- **[Reverse Connect Facet](http://opcfoundation.org/UA-Profile/Server/ReverseConnect)** - Server can initiate connections to clients (see [Reverse Connect documentation](ReverseConnect.md))

- **[Client Redundancy Facet](http://opcfoundation.org/UA-Profile/Server/ClientRedundancy)** - Support for client redundancy features including:
  - Transfer subscriptions between servers
  - Session management for redundant connections
  - See [Transfer Subscriptions documentation](TransferSubscription.md)

### Additional Features

The server implementation also provides support for:

- **Durable Subscriptions** - Subscriptions that persist across reconnections (see [Durable Subscriptions documentation](DurableSubscription.md))
- **Complex Types** - Custom structures and enumerations (see [Complex Types documentation](ComplexTypes.md))
- **Role-Based Access Control** - WellKnownRoles and RoleBasedUserManagement (see [Role-Based User Management documentation](RoleBasedUserManagement.md))
- **Async Server Support** - Asynchronous node managers using Task-based Asynchronous Pattern (TAP) (see [Async Server Support documentation](AsyncServerSupport.md))

### Currently Not Supported (Server)

The following server profiles/facets are **not yet fully supported**:

- **Alarms & Conditions** - Only a limited set of alarms is currently implemented (`ExclusiveLevel`, `NonExclusiveLevel`, `OffNormal`)
- **Historical Access** - Limited support for historical data access
- **Events** - Limited event support
- **Aggregates Server Facet** - Historical data aggregation
- **Query Server Facet** - Advanced query capabilities

## Client Profiles

The Client implementation supports:

- **Standard UA Client Profile** - Full client functionality for connecting to OPC UA servers
- **Subscription management** - Creating and managing subscriptions and monitored items
- **Transfer Subscriptions** - Support for transferring subscriptions between servers (see [Transfer Subscriptions documentation](TransferSubscription.md))
- **Reverse Connect** - Client can accept connections initiated by servers (see [Reverse Connect documentation](ReverseConnect.md))

## Transport Profiles

The stack implements the following transport profiles:

### Client and Server Transport Support

- **[UA TCP Transport](http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary)** (`opc.tcp://`) - The primary OPC UA binary transport protocol over TCP
  - Full support for UA Secure Conversation (UASC)
  - Binary encoding
  - Reverse connect capability

- **[HTTPS Binary Transport](http://opcfoundation.org/UA-Profile/Transport/https-uabinary)** (`opc.https://` and `https://`) - OPC UA binary protocol over HTTPS
  - Binary encoding over HTTPS
  - TLS/SSL encryption

### PubSub Transport Support

The [PubSub library](PubSub.md) supports the following transport profiles:

- **[PubSub UDP UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp)** - UDP transport with UADP message encoding
- **[PubSub MQTT UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp)** - MQTT transport with UADP message encoding
- **[PubSub MQTT JSON](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json)** - MQTT transport with JSON message encoding

### Currently Not Supported (Transport)

- **WebSocket Transport** (`opc.wss://`) - UA WebSocket Secure (WSS) transport is not currently supported
- **HTTPS JSON Transport** - JSON encoding over HTTPS is not currently supported

## Security Profiles

The stack supports the following OPC UA security profiles for secure communication:

### RSA-Based Security Policies

- **[Basic256Sha256](http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256)** - RSA encryption with SHA-256
  - 256-bit AES encryption
  - RSA-OAEP for key encryption
  - HMAC-SHA256 for message authentication
  - Minimum key size: 2048 bits

- **[Aes128_Sha256_RsaOaep](http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep)** - 128-bit AES with SHA-256
  - 128-bit AES encryption
  - RSA-OAEP for key encryption
  - HMAC-SHA256 for message authentication

- **[Aes256_Sha256_RsaPss](http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss)** - 256-bit AES with RSA-PSS signatures
  - 256-bit AES encryption
  - RSA-PSS signatures
  - HMAC-SHA256 for message authentication

### ECC-Based Security Policies

Support for Elliptic Curve Cryptography (ECC) security policies (see [ECC Profiles documentation](EccProfiles.md)):

- **[ECC_nistP256](http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256)** - NIST P-256 curve
- **[ECC_nistP384](http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384)** - NIST P-384 curve
- **[ECC_brainpoolP256r1](http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1)** - Brainpool P-256r1 curve
- **[ECC_brainpoolP384r1](http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1)** - Brainpool P-384r1 curve

**Platform Requirements for ECC:** ECC support is available on .NET Framework 4.8, .NET Standard 2.1, and .NET 5.0 or later. Not all curves are supported by all OS platforms and .NET implementations.

### Deprecated Security Policies

The following security policies are deprecated but still supported for backward compatibility:

- **[Basic256](http://opcfoundation.org/UA/SecurityPolicy#Basic256)** - Deprecated, uses SHA-1
- **[Basic128Rsa15](http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15)** - Deprecated, uses SHA-1 and RSA-PKCS#1 v1.5

**Note:** SHA-1 signed certificates are rejected by default (`RejectSHA1SignedCertificates` configuration option). These deprecated policies should only be enabled for compatibility with legacy systems.

### Security Policy None

- **[None](http://opcfoundation.org/UA/SecurityPolicy#None)** - No security
  - Should only be used for testing or on isolated networks
  - Not recommended for production environments

## User Authentication

The stack supports the following user authentication mechanisms:

- **Anonymous** - No user authentication
- **Username/Password** - User credentials encrypted using the active security policy
- **X.509 Certificate** - User authentication via X.509 certificates

Additional token types:
- **JWT (JSON Web Tokens)** - Support for issued tokens complying with JWT specification

## Certificate Types

The stack supports the following certificate types for application authentication:

### RSA Certificates
- **RsaSha256ApplicationCertificateType** - RSA certificates with SHA-256 signatures
  - Default minimum key size: 2048 bits
  - Recommended for production use

### ECC Certificates
- **EccNistP256ApplicationCertificateType** - ECC certificates with NIST P-256 curve
- **EccNistP384ApplicationCertificateType** - ECC certificates with NIST P-384 curve
- **EccBrainpoolP256r1ApplicationCertificateType** - ECC certificates with Brainpool P-256r1 curve
- **EccBrainpoolP384r1ApplicationCertificateType** - ECC certificates with Brainpool P-384r1 curve

See [Certificates documentation](Certificates.md) for more information on certificate management.

## Global Discovery Server (GDS)

The stack includes a Global Discovery Server implementation that supports:

- Application registration and discovery
- Certificate management
- Pull and Push certificate management models
- Support for both RSA and ECC certificate types
- Certificate revocation lists (CRL)

## Message Encoding

The stack supports the following message encoding formats:

- **UA Binary** - OPC UA binary encoding (primary encoding used for UA-TCP and HTTPS)
- **UADP** - UA Data Protocol for PubSub
- **JSON** - JSON encoding for PubSub MQTT

## Specification Compliance

- **OPC UA Specification:** Version 1.05
- **Certification:** The Reference Server has been certified for compliance through an OPC Foundation Certification Test Lab
- **Testing:** All releases are verified for compliance using the latest Compliance Test Tool (CTT)

## Configuration

### Server Profile Configuration

Server profiles are configured in the server configuration file using the `ServerProfileArray` element:

```xml
<ServerConfiguration>
  <!-- see https://opcfoundation-onlineapplications.org/profilereporting/ for list of available profiles -->
  <ServerProfileArray>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/StandardUA2017</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/DataAccess</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/Methods</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/ReverseConnect</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/ClientRedundancy</ua:String>
  </ServerProfileArray>
</ServerConfiguration>
```

### Security Policy Configuration

Security policies are configured in the `SecurityPolicies` section:

```xml
<SecurityPolicies>
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
  </ServerSecurityPolicy>
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep</SecurityPolicyUri>
  </ServerSecurityPolicy>
  <!-- ECC Security Policies -->
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
  </ServerSecurityPolicy>
</SecurityPolicies>
```

See the [Reference Server configuration file](../Applications/ConsoleReferenceServer/Quickstarts.ReferenceServer.Config.xml) for a complete example.

## Related Documentation

- [ECC Profiles](EccProfiles.md) - Detailed information about ECC certificate and security policy support
- [Certificates](Certificates.md) - Certificate management and storage
- [Reverse Connect](ReverseConnect.md) - Reverse connection configuration and usage
- [Transfer Subscriptions](TransferSubscription.md) - Subscription transfer between servers
- [Durable Subscriptions](DurableSubscription.md) - Persistent subscriptions across reconnections
- [Complex Types](ComplexTypes.md) - Custom structures and enumerations
- [Role-Based User Management](RoleBasedUserManagement.md) - Role-based access control
- [PubSub](PubSub.md) - Publisher-Subscriber pattern implementation
- [Async Server Support](AsyncServerSupport.md) - Asynchronous node manager implementation

## References

- [OPC Foundation Profile Reporting](https://profiles.opcfoundation.org/)
- [OPC UA Specification](https://reference.opcfoundation.org/)
- [OPC UA Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/)
