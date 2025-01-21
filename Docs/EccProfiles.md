# Support for Elliptic Curve Cryptography (ECC) Certificates in Server and Client Applications

The server and client applications now support encrypted communication using both the RSA and the ECC encryption algorithms.
The following document tries to explain the changes in the configuration of the server and client applications needed to support ECC certificates as well as the well consacrated RSA certificates.

The means by which client and server application security related configuration has been configured upto introducing the ECC certificates, is described in the section [Previous Client and Server application configuration supports only RSA certificates](###previous-client-and-server-application-configuration-supports-only-rsa-certificates).

The new security related configuration of client and server applications is described in the section [New Client and Server application configuration support both RSA and ECC certificates](###new-client-and-server-application-configuration-support-both-rsa-and-ecc-certificates).

The compatibility between the old and the new configuration of client and server applications is described in the section [Old Client and Server application configuration VS New Client and Server spplication configuration](###old-client-and-server-application-configuration-vs-new-client-and-server-spplication-configuration).

The limitations of the support for ECC certificates are described in the section [Known Limitations](###known-limitations).


## Previous Client and Server application configuration supports only RSA certificates

Up to now, the configuration supported encrypted communication using only the RSA encryption algorithm. That means that the server and client certificates were RSA certificates and there was just one RSA certificate per application needed to be configured.
The XML tag which contained the RSA certificate to be configured was `<ApplicationCertificate>`. This tag is still used for the RSA certificate configuration in backward compatibility mode as further described below:


```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
 <!-- Where the application instance certificate is stored (MachineDefault) -->
 <ApplicationCertificate>
   <StoreType>Directory</StoreType>
   <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
   <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
 </ApplicationCertificate>
 ....
</SecurityConfiguration>
```

## Previous Server application configuration supports only RSA certificates

For Server applications the configuration of the RSA certificate involves also specifying the SecurityPolicies to be supported by the server:
    
```xml
    <!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- The security policy to use. -->
    <SecurityPolicies>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
        </ServerSecurityPolicy>   
        <ServerSecurityPolicy>
            <SecurityMode>None_1</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
    ....
</SecurityConfiguration>
```

## New Client and Server application configuration support both RSA and ECC certificates

With the newly introduced support for ECC certificates, the configuration of the server application has been extended to support both RSA and ECC certificates. The XML tag under which both the RSA and ECC certificates are configured is `<ApplicationCertificates>`.

The `<ApplicationCertificate>` tag is still used for the RSA certificate configuration in backward compatibility mode, meaning that old server configurations are still supported, but they cannot simultaneously coexist. 

The new configuration of the server application is described below:

```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- Where the application instance certificate is stored (MachineDefault) -->
    <ApplicationCertificates>
        <CertificateIdentifier>
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>RsaSha256</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>NistP256</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>NistP256</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>NistP384</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>NistP384</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>BrainpoolP256r1</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>BrainpoolP256r1</CertificateTypeString>
        </CertificateIdentifier>
        <CertificateIdentifier>
            <!-- <TypeId>BrainpoolP384r1</TypeId> -->
            <StoreType>Directory</StoreType>
            <StorePath>%LocalApplicationData%/OPC Foundation/pki/own</StorePath>
            <SubjectName>CN=Quickstart Reference Server, C=US, S=Arizona, O=OPC Foundation, DC=localhost</SubjectName>
            <CertificateTypeString>BrainpoolP384r1</CertificateTypeString>
        </CertificateIdentifier>
    </ApplicationCertificates>
....
</SecurityConfiguration>
```

This layout of the configuration file allows the server to support both RSA and ECC certificates and allows the server to generate certificates of different types. The `<CertificateTypeString>` tag is used to specify the type of the certificate. 

The supported types are: 
 - `RsaSha256`              for RSA certificates
 - `NistP256`               for ECC certificates with NIST P256 curve
 - `NistP384`               for ECC certificates with NIST P384 curve
 - `BrainpoolP256r1`        for ECC certificates with Brainpool P256r1 curve
 - `BrainpoolP384r1`        for ECC certificates with Brainpool P384r1 curve

Additionally, this layout enables the user to select a specific `<SubjectName>` for each certificate. This is useful when the user wants to generate certificates with different `<SubjectName>`s.


## New Server application configuration related to ECC certificates

The Server applications can configure the supported SecurityPolicies in the same way as before, but now the SecurityPolicies can be configured for each ECC specific SecurityPolicyUri which is to be supported by the server:

```xml
<!-- The security configuration for the server. -->
<SecurityConfiguration>
    <!-- The security policy to use. -->
    <SecurityPolicies>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri></SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>Sign_2</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384</SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1<SecurityPolicyUri>
        </ServerSecurityPolicy>
        <ServerSecurityPolicy>
            <SecurityMode>SignAndEncrypt_3</SecurityMode>
            <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1</SecurityPolicyUri>
        </ServerSecurityPolicy>
    </SecurityPolicies>
    ....
</SecurityConfiguration>
```

With the introduction of ECC certificates, the `<SecurityPolicies>` section of the configuration file has been extended to support ECC specific SecurityPolicies. The ECC specific SecurityPolicies are the following:

 - `http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256`
 - `http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384`
 - `http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1`
 - `http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1`

If ECC specific SecurityPolicies are specified in the `<SecurityPolicies>` section of the configuration file, then the server will need to support the corresponding ECC certificates configured in the `<ApplicationCertificates>` section of the configuration file with the corresponding `<CertificateTypeString>`s, not having them configured will result in the server not being able to start.

Since the UserIdentityToken is also encrypted using the RSA or ECC certificate depending on the active security policy or the specified `<SecurityPolicyUri>` of the `<UserTokenPolicy>`, servers which intend to explicitly state which ECC encryption (if any) of the UserIdentity tokens is supported, should specify the supported `<SecurityPolicy>` in the `<UserTokenPolicies>` section of the configuration file (as it has always been). The supported `<SecurityPolicy>`s are the same as the ones specified in the `<SecurityPolicies>` section of the configuration file.

The following example shows how the server can specify the supported `<SecurityPolicy>`s for the UserIdentityTokens:

```xml
<!-- The SDK expects the server to support the same set of user tokens for every endpoint. -->
<UserTokenPolicies>
    <!-- Allows anonymous users -->
    <ua:UserTokenPolicy>
        <ua:TokenType>Anonymous_0</ua:TokenType>
        <!-- <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</ua:SecurityPolicyUri> -->
    </ua:UserTokenPolicy>
        <!-- Allows username/password with password encrypted using the active security policy-->
    <ua:UserTokenPolicy>
        <ua:TokenType>UserName_1</ua:TokenType>
        <!-- passwords must be encrypted - this specifies what algorithm to use -->
        <!-- if no algorithm is specified, the active security policy is used -->
        <!-- <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</ua:SecurityPolicyUri> -->
    </ua:UserTokenPolicy>
        <!-- Allows username/password with password encrypted using ECC security-->
    <ua:UserTokenPolicy>
        <ua:TokenType>UserName_1</ua:TokenType>
        <!-- passwords must be encrypted - this specifies what algorithm to use -->
        <!-- if no algorithm is specified, the active security policy is used -->
        <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</ua:SecurityPolicyUri>
    </ua:UserTokenPolicy>
</UserTokenPolicies>
```

The `<SecurityPolicyUri>` tag can be ommited in the `<UserTokenPolicy>` tag, in which case the active security policy is used for the encryption of the UserIdentityToken. That implies that there is a dependency between the `<SecurityPolicies>` and the `<UserTokenPolicies>` sections of the configuration file, meaning that the `<SecurityPolicyUri>`s specified in the `<UserTokenPolicies>` section should be a subset of the `<SecurityPolicyUri>`s specified in the `<SecurityPolicies>` section of the configuration file.

A situation in which a `<SecurityPolicyUri>` is specified in the `<UserTokenPolicies>` section of the configuration file, but it is not specified in the `<SecurityPolicies>` section of the configuration file is logically incorrect and produces an invalid configuration.


## "Old" Client and Server application configuration format VS "New" Client and Server application configuration format

Client and server applications which use the "old" configuration, will continue to work as before, meaning that the server and client certificates will be RSA certificates and the `<ApplicationCertificate>` tag will be used to configure the RSA certificate. Server applications will have no ECC security policies specified in the `<SecurityPolicies>` section of the configuration file.
You should be aware that such applications will not be able to support ECC certificates therefore they will not be able to communicate with clients and servers which use ECC certificates.

Client and server applications which use the "new" configuration, are be able to support both RSA and ECC certificates. 
Server and Client applications are be able to support both RSA and ECC security policies by using the `<ApplicationCertificates>` tag to configure the RSA and ECC certificates. The `<SecurityPolicies>` section of the configuration file are still used to configure the supported security policies which include the new ECC policies.
Additionally the `<UserTokenPolicies>` section of the configuration file can be used to configure the supported security policies for the UserIdentityTokens which also include the new ECC policies.

Combining the "old" and the "new" configuration formats is not supported. That means that the `<ApplicationCertificate>` tag cannot be used in the same configuration file with the `<ApplicationCertificates>` tag.


## Configure GDS for use with ECC Certificates

To configure the Global Discovery Server for use with ECC Certificates the configuration needs to be updated.

```xml
  <Extensions>
    <ua:XmlElement>
      <GlobalDiscoveryServerConfiguration xmlns="http://opcfoundation.org/UA/GDS/Configuration.xsd">
        <CertificateGroups>
          <CertificateGroupConfiguration>
            <Id>Default</Id>
            <CertificateType>RsaSha256ApplicationCertificateType</CertificateType>
```

Replace the `<CertificateType>` node of the Default CertificateGroupConfiguration with the `<CertificateTypes>` node. 
This allows the Certificate Group to have multiple CA Certificates for the different Certificate types.

```xml
<Extensions>
    <ua:XmlElement>
      <GlobalDiscoveryServerConfiguration xmlns="http://opcfoundation.org/UA/GDS/Configuration.xsd">
        <CertificateGroups>
          <CertificateGroupConfiguration>
            <Id>Default</Id>
            <CertificateTypes>
              <ua:String>RsaSha256ApplicationCertificateType</ua:String>
              <ua:String>EccNistP256ApplicationCertificateType</ua:String>
              <ua:String>EccNistP384ApplicationCertificateType</ua:String>
            </CertificateTypes>
```

The old Configuration format is still supported but only supports either RSA or ECC Certificates for a single CertificateGroup.
The GDS checks on startup if a valid configuration was supplied.


## Known Limitations

Not all curves are supported by all OS platforms and not all .NET implementations offer cryptographic API support for all curve types.
Due to these limitations, the support for ECC profiles is available starting with the following target platforms: .NET 4.8, .NET standard 2.1 and .NET 5 and above.
The supported ECC curve types are the following:

 - `NistP256`               for ECC certificates with NIST P256 curve
 - `NistP384`               for ECC certificates with NIST P384 curve
 - `BrainpoolP256r1`        for ECC certificates with Brainpool P256r1 curve
 - `BrainpoolP384r1`        for ECC certificates with Brainpool P384r1 curve




        