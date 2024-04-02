# OPC UA .NET Standard stack documentation

## Overview

The OPC UA .NET Standard stack enables you to build multi-platform OPC UA Applications with rich functionality including:
 - Server
 - Client
 - PubSub


### For more information and license terms, see the projects official [Website](http://opcfoundation.github.io/UA-.NETStandard).

## Getting started

The reference [Client](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Applications/ConsoleReferenceClient) & [Server](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Applications/ReferenceServer) projects provide a starting point in implementing your own application.

Simple Client:
```C#
// Define the UA Client application
ApplicationInstance application = new ApplicationInstance();
application.ApplicationName = "My OPC UA Client";
application.ApplicationType = ApplicationType.Client;    

// load the application configuration.
await application.LoadApplicationConfiguration();

// check the application certificate.
await application.CheckApplicationInstanceCertificate(false, minimumKeySize: 0);

using (UAClient uaClient = new UAClient(application.ApplicationConfiguration, reverseConnectManager, output, ClientBase.ValidateResponse) {
                        AutoAccept = autoAccept,
                        SessionLifeTime = 60_000,
                    });

await uaClient.ConnectAsync(serverUrl.ToString(), !noSecurity, quitCTS.Token).ConfigureAwait(false);
```

Simple Server:
```C#

```


## Packages Overview

Caution, there are multiple packages available with different functional scopes, for a detailed overview take a look at the [Information about the different Packages](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PlatformBuild.md#further-information-on-the-supported-nuget-packages).
 
## Additional documentation

Additional information about the OPC UA .NET Standard stack is available on the GitHub Repo of the project in the [detailed Documentation](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Docs#opc-ua-net-standard-stack-documentation).