# OPC UA .NET Standard stack documentation

## Overview

The OPC UA .NET Standard stack enables you to build multi-platform OPC UA Applications with rich functionality including:
 - Server
 - Client
 - PubSub


### For more information and license terms, see the projects official [Website](http://opcfoundation.github.io/UA-.NETStandard).

## Getting started

The reference [Client](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Applications/ConsoleReferenceClient) & [Server](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Applications/ReferenceServer) projects provide a starting point in implementing your own application.

<!---
 Simple Client:
```C#
ToDo
```

Simple Server:
```C#
ToDo
```
-->


## Packages Overview

Caution, there are multiple packages available with different functional scopes, for a detailed overview take a look at the [Information about the different Packages](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PlatformBuild.md#further-information-on-the-supported-nuget-packages).

## Version Numbering

The OPC UA .NET Standard library uses a four-part version number (e.g., 1.5.377.x) that differs from standard SemVer. The first two digits reflect the OPC UA specification version (1.04, 1.05), the third digit increments for breaking changes, and the fourth digit is the build number since the last breaking change. Important bug fixes and behavioral changes may only increment the fourth digit, so always review release notes when updating to ensure you're aware of all changes, even within the same major.minor.patch version.
 
## Additional documentation

Additional information about the OPC UA .NET Standard stack is available on the GitHub Repo of the project in the [detailed Documentation](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Docs#opc-ua-net-standard-stack-documentation).