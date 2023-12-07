## .NET Platform support and Nuget package releases

The following Nuget packages are released in a monthly cadence, unless security issues require hotfixes.

[OPCFoundation.NetStandard.Opc.Ua](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)  
[OPCFoundation.NetStandard.Opc.Ua.Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/)  
[OPCFoundation.NetStandard.Opc.Ua.Security.Certificates](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Security.Certificates/)  
[OPCFoundation.NetStandard.Opc.Ua.Configuration](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Configuration/)  
[OPCFoundation.NetStandard.Opc.Ua.Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/)  
[OPCFoundation.NetStandard.Opc.Ua.Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/)  
[OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes/)  
[OPCFoundation.NetStandard.Opc.Ua.Bindings.Https](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/)  
[OPCFoundation.NetStandard.Opc.Ua.PubSub](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.PubSub/) (Beta)  

The OPCFoundation prefix is reserved and the assemblies and the Nuget packages are signed by the OPC Foundation. 
For the licenses please see the information in the packages.

In addition to the released packages on Nuget.org, for every successful build in main there are preview packages available on a [preview feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_artifacts/feed/opcua-preview) in Azure DevOps.

The versioning scheme is not compatible to the [Semantic Versioning](https://semver.org/) standard, due to some baggage that the releases are based on in the OPC UA specification.

The first two digits of the Nuget package version represent the spec version of the embedded Nodeset from the UA specification:

- 1.3.x.x  --> spec V1.03 released in 2015
- 1.4.x.x  --> spec V1.04 released in 2017
- 1.5.x.x  --> spec V1.05 released in 2022

The OPC UA spec is committed to backward compatibility, so the 1.04 releases work also with 1.03 certified servers and clients.
Once the UA working group releases the spec V1.05.03, the Nuget packages will be updated to be based on a 1.05 Nodeset (ETA Q1 2024), which can still be used to certify a V1.04 UA application.

The next digits in the Nuget package version represent the API level and the build number. 
An API level is mapped to a dedicated branch for a release, e.g. [release/1.4.372](https://github.com/OPCFoundation/UA-.NETStandard/tree/release/1.4.372). 
Thus for hotfixes, a released API level can easily receive cherry picks or security updates from the main branch.   
An API level remains in itself consistent, that it should not receive breaking changes that would require code changes in applications.  
However, internal improvements or even small features which extend existing APIs that may not require application changes may be included in build updates.
In fact the versioning doesn't map to the MAJOR.MINOR.PATCH semantic versioning. The build number corresponds to a mix of MINOR and PATCH, the API level corresponds to MAJOR (breaking changes). The spec version prefix however is guaranteed to be downwards compatible, and it should be possible to certify a UA Server that is built with a 1.5 library with a 1.4 certification test.

Currently the released Nuget packages support a wide variety of .NET platforms:

The following .NET versions are currently supported by the class libraries
- .NET Standard 2.0
- .NET Standard 2.1
- .NET Framework 4.8 *
- .NET 6.0 *
- .NET 8.0 **

The following platform is deprecated but can still be built and tested:
- .NET Framework 4.6.2

To reduce the ci build overhead and the number of tests to be run in Visual Studio, only the tagged versions (* and **) are part of a qualifying ci build to pass a pull request. 
All other platforms are only tested in weekly scheduled or manual ci builds.

By default, in Visual Studio only the platforms tagged with (*) are tested. In order to test the other platforms in a command line window or in VS, there is a custom build variable defined to target a specific build. E.g. to target a .NETStandard2.0 build, the test runners are compiled with .NET 6.0 but the class libraries target only netstandard2.0, to force the use of that target.
Another option is to test run such a custom target in a command window with a batch file [CustomTest.bat](../Tests/customtest.bat) which is provided to clean up, restore the project and to run the tests. To run the custom tests in Visual Studio a section in [target.props](../targets.props) needs to be uncommented and the target platform value must be set. 

```xml
<!-- 
  Uncomment the following lines to test a custom test target 
  supported values: net462, netstandard2.0, netstandard2.1, net48, net6.0, net8.0
 -->
  
  <PropertyGroup>
    <CustomTestTarget>netstandard2.0</CustomTestTarget>
  </PropertyGroup> 
```

Due to the limitations of the build system it is recommended to run the CustomTest batch file as well to force a clean build of the project for the test target.


## Further information on the supported Nuget packages

The OPCFoundation prefix is reserved and the assemblies and the Nuget packages are signed by the OPC Foundation. 

For improved source level debugging in Visual Studio, symbol packages are available on Nuget.org in the 'snupkg' format. A reference to the Nuget symbol server may have to be added in Visual Studio to enable the source level debug support.
In addition packages compiled as Debug are available on Nuget.org with a '.Debug' extension to the package name.

[OPCFoundation.NetStandard.Opc.Ua](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)

This is a wrapper package to include all the available packages from this repository, except PubSub. It is recommended to rather include the individual packages as below to reduce the number of dependencies.

[OPCFoundation.NetStandard.Opc.Ua.Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/)
[OPCFoundation.NetStandard.Opc.Ua.Security.Certificates](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Security.Certificates/)

Core and Certificates are required for Client and Server projects.

[OPCFoundation.NetStandard.Opc.Ua.Configuration](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Configuration/)

The configuration contains a helper class to configure a UA application from file or with a fluent API.

[OPCFoundation.NetStandard.Opc.Ua.Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/)

The server library is used to build a UA server.

[OPCFoundation.NetStandard.Opc.Ua.Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/)
[OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes/)

The client is used to build a client. The complex type library extends the client with support for complex types.

[OPCFoundation.NetStandard.Opc.Ua.Bindings.Https](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/)

The Https binding is an optional component to support UA over Https for 'opc.https' endpoints.

[OPCFoundation.NetStandard.Opc.Ua.PubSub](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.PubSub/) (Beta)

The PubSub library can be used to implement the publisher subscriber model. 

For the licenses please see the information in the packages.
