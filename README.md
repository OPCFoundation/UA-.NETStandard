# UA Universal Windows Platform

This OPC UA implementation is targeted to Universal Windows Platform (UWP), a platform-homogeneous application architecture. UWP allows developing Metro-style apps that run on both Windows 10 and Windows 10 Mobile without the need to be re-written for each.
UWP is a part of Windows 10 and Windows 10 Mobile.
The OPC Foundation provides an OPC UA implementation for .NET (https://github.com/OPCFoundation/UA-.NET) that supports all versions of Windows on PC platforms since Windows XP. The OPC UA Stack for UWP is based on this stack and has been ported to UWP interfaces by Microsoft developers.
The OPC Foundation will eventually merge the .NET stack and the UWP stack.

Features included:

1. Fully ported core stack and SDK
2. Sample Client and Sample Server, including all required controls
3. X509 certificate support for client and server authentication
4. Anonymous user authentication
5. UA-TCP transport
6. Folder and Windows-certificate-store support
7. Sessions (including UI support in the samples)
8. Subscriptions (including UI support in the samples)


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