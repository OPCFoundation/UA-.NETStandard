# UA Universal Windows Platform

This OPC UA reference implementation is targeting the Universal Windows Platform (UWP). UWP allows developing apps that run on all Windows 10 editions (including the IoT editions) without requiring edition-specific modifications.
The OPC Foundation provides an OPC UA reference implementation for .NET (https://github.com/OPCFoundation/UA-.NET) that supports all versions of Windows Desktop editions since Windows XP. The OPC UA reference implementation for UWP is based on this and has been ported to UWP interfaces by Microsoft.
The OPC Foundation will eventually merge the .NET stack and the UWP stack.

Features included:

1. Fully ported Core UA stack and SDK
2. Sample Client and Sample Server, including all required controls
3. X509 certificate support for client and server authentication
4. Anonymous user authentication
5. UA-TCP transport
6. Folder- and Windows-certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)

This Publishing_Prototype branch furthermore contains the **Publisher sample application** as demonstrated at Hannover Fair 2016.  The Publisher Demo acts as a Gateway and allows users to create traditional monitored Item subscriptions that are JSON encoded and published to one or more configured AMQP endpoint(s).  The Publisher Demo is based on the Sample Client.  Its AMQP publishing connections can be configured in the sample's Opc.UA.Publisher.Config.xml file, AMQPConnectionConfiguration section, which is extensively documented.  So far the Publisher has been tested against a Microsoft Azure IoTHub instance, as well as against a Microsoft Azure ServiceBus queue, but should work against any AMQP Broker that provides a standard AMQP 1.0 interface.

## License
This repository includes the UA .NET Stack, sample libraries, and sample applications. The UA .NET Stack follows a dual-license:

 * **OPC Foundation Corporate Members**: [RCL](https://opcfoundation.org/license/rcl.html)
 * **Everybody else**: [GPL 2.0](https://opcfoundation.org/license/gpl.html)

RCL enables OPC Foundation members to deploy their applications using the UA UWP stack without being required to disclose the application code. Non-members must disclose their application code when using the UA UWP Stack.

**Note**: Dual license applies to this repository only; GPL 2.0 applies to all derived repositories (for example 'forks').

All samples are provided under the [MIT license](https://opcfoundation.org/license/mit.html).


## Contributing
We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
