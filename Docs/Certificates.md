## Certificates

All required application certificates for OPC UA are created at the first start of each application in a directory or OS-level certificate store and remain in use until deleted from the store.

The UA stack allows also for using CA issued application certificates and remote certificate store and trust list management with a *Global Discovery Server* using *Server Push*.

### Certificate stores

The layout of the certificate stores for sample applications which store the certificates in the file system follow the recommended layout in the [specification](https://reference.opcfoundation.org/v104/GDS/docs/F.1/), where certificates are stored in a `certs` folder, private keys under a `private` folder and revocation lists under a `crl` folder with a `<root>` folder called `pki`. 

The UA .NET Standard stack supports the following certificate stores:

- The **Application** store  `<root>/own`which contains private keys used by the application.

- The **Issuer** store  `<root>/issuer`which contains certificates which are needed for validation, for example to complete the validation of a certificate chain. A certificate in the *Issuer* store is *not* trusted! 

- The **Trusted** store  `<root>/trusted`which contains certificates which are trusted by the application. The certificates in this store can either be self signed, leaf, root CA or sub CA certificates. 
  The most common use case is to add a self signed application certificate to the *Trusted* store to establish trust with that application. 
  If the application certificate is the leaf of a chain, the trust can be established by adding the root CA, a sub CA or the leaf certificate itself to the *Trusted* store. Each of the options enables a different set of trusted certificates. A trusted Root CA or Sub CA certificate is used as the trust anchor for the certificate chain, which means any leaf certificate with a chain which contains the Root CA and Sub CA certificate is trusted, but the specification still mandates the validation of the whole chain. For the chain validation any certificate in the chain except the leaf certificate must be available from the *Issuer* store.

  If only the leaf certificate is in the *Trusted* store and the rest of the chain is stored in the *Issuer* store, then only the leaf certificate is trusted. 
  As an example, to trust an application certificate that is issued by a Root CA, only the Root CA certificate is required in the *Trusted* store to establish trust to all application certificates issued by the CA. This option can greatly simplify the management of OPC UA Clients and Servers because only one certificate needs to be distributed across all systems.

- The **Rejected** store  `<root>/rejected` which contains certificates which have been rejected. This store is provided as a convenience for the administrator of an application to allow to copy an untrusted certificate from the *Rejected* to the *Trusted* store to establish trust with that application.

- The **Issuer User** store  `<root>/issuerUser` which contains user certificates which are used to validate user certificates.

- The **Trusted User** store  `<root>/trustedUser` which contains user certificates which are trusted by an application. To establish trust, the same rules apply as explained for the *Trusted* and the *Issuer* store.

- The **Issuer Https** store  `<root>/issuerHttps` which contains https certificates which are used to validate https connection certificates.

- The **Trusted Https** store  `<root>/trustedHttps` which contains https certificates which are trusted by an application. To establish trust, the same rules apply as explained for the *Trusted* and the *Issuer* store.

### Windows .NET applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\pki** with a root folder which is specified by the `SpecialFolder` variable **%CommonApplicationData%**. On Windows 7/8/8.1/10 this is usually the invisible folder **C:\ProgramData**. 

### Windows UWP applications
By default the self signed certificates are stored in a **X509Store** called **CurrentUser\\UA_MachineDefault**. The certificates can be viewed or deleted with the Windows Certificate Management Console (certmgr.msc). 

The *trusted*, *issuer* and *rejected* stores remain in a folder called **OPC Foundation\pki** in the **LocalState** folder of the installed universal windows package. Deleting the application state also deletes the certificate stores.

### .NET Core applications on Windows, Linux, iOS etc.
The self signed certificates are stored in a folder called **OPC Foundation/pki/own** with a root folder which is specified by the `SpecialFolder` variable **%LocalApplicationData%** or in a **X509Store** called **CurrentUser\\My**, depending on the configuration. For best cross platform support the personal store **CurrentUser\\My** was chosen to support all platforms with the same configuration. Some platforms, like macOS, do not support arbitrary certificate stores.

The *trusted*, *issuer* and *rejected* stores remain in a shared folder called **OPC Foundation\pki** with a root folder specified by the `SpecialFolder` variable **%LocalApplicationData%**. Depending on the target platform, this folder maps to a hidden locations under the user home directory.