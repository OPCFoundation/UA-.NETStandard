/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Sample
{
    public partial class Helpers
    {   
        public const string DefaultHttpUrl = "http://localhost:51211/UA/SampleServer";        
        public const string DefaultTcpUrl = "opc.tcp://localhost:51210/UA/SampleServer";
        public const string InstanceNamespaceUri = "http://tempuri.org/UA/Workshop/";
        public const string TypeNamespaceUri = "http://tempuri.org/UA/Workshop/Types/";

        /// <summary>
        /// Creates a minimal application configuration for a client.
        /// </summary>
        /// <remarks>
        /// In most cases the application configuration will be loaded from an XML file. 
        /// This example populates the configuration in code.
        /// </remarks>
        public static async Task<ApplicationConfiguration> CreateClientConfiguration()
        {
            // The application configuration can be loaded from any file.
            // ApplicationConfiguration.Load() method loads configuration by looking up a file path in the App.config.
            // This approach allows applications to share configuration files and to update them.
            ApplicationConfiguration configuration = new ApplicationConfiguration();

            // Step 1 - Specify the server identity.
            configuration.ApplicationName = "My Client Name";
            configuration.ApplicationType = ApplicationType.Client;
            configuration.ApplicationUri = "http://localhost/VendorId/ApplicationId/InstanceId";
            configuration.ProductUri = "http://VendorId/ProductId/VersionId";

            configuration.SecurityConfiguration = new SecurityConfiguration();

            // Step 2 - Specify the server's application instance certificate.

            // Application instance certificates must be placed in a windows certficate store because that is 
            // the best way to protect the private key. Certificates in a store are identified with 4 parameters:
            // StoreLocation, StoreName, SubjectName and Thumbprint.
            //
            // In this example the following values are used:
            // 
            //   LocalMachine    - use the machine wide certificate store.
            //   Personal        - use the store for individual certificates.
            //   ApplicationName - use the application name as a search key.   

            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier();
            configuration.SecurityConfiguration.ApplicationCertificate.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.ApplicationCertificate.StorePath = "LocalMachine\\My";
            configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = configuration.ApplicationName;

            // trust all applications installed on the same machine.
            configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = "LocalMachine\\My";

            // find the certificate in the store.
            X509Certificate2 clientCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            // create a new certificate if one not found.
            if (clientCertificate == null)
            {
                // this code would normally be called as part of the installer - called here to illustrate.
                // create a new certificate an place it in the LocalMachine/Personal store.
                clientCertificate = CertificateFactory.CreateCertificate(
                    configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                    configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                    configuration.ApplicationUri,
                    configuration.ApplicationName,
                    null,
                    null,
                    1024,
                    300);

                Console.WriteLine("Created client certificate: {0}", clientCertificate.Subject);
            }

            // Step 4 - Specify the supported transport quotas.

            // The transport quotas are used to set limits on the contents of messages and are
            // used to protect against DOS attacks and rogue clients. They should be set to
            // reasonable values.
            configuration.TransportQuotas = new TransportQuotas();
            configuration.TransportQuotas.MaxArrayLength = Int32.MaxValue;
            configuration.TransportQuotas.MaxByteStringLength = Int32.MaxValue;
            configuration.TransportQuotas.MaxStringLength = Int32.MaxValue;
            configuration.TransportQuotas.MaxMessageSize = Int32.MaxValue;
            configuration.TransportQuotas.OperationTimeout = 600000;

            configuration.ServerConfiguration = new ServerConfiguration();

            // Step 5 - Specify the client specific configuration.
            configuration.ClientConfiguration = new ClientConfiguration();
            configuration.ClientConfiguration.DefaultSessionTimeout = 30000;

            // Step 6 - Validate the configuration.

            // This step checks if the configuration is consistent and assigns a few internal variables
            // that are used by the SDK. This is called automatically if the configuration is loaded from
            // a file using the ApplicationConfiguration.Load() method.          
            await configuration.Validate(ApplicationType.Client);

            return configuration;
        }

        /// <summary>
        /// Creates a minimal application configuration for a server.
        /// </summary>
        /// <remarks>
        /// In many cases the application configuration will be loaded from an XML file. 
        /// This example populates the configuration in code.
        /// </remarks>
        public static async Task<ApplicationConfiguration> CreateServerConfiguration()
        {
            // The application configuration can be loaded from any file.
            // ApplicationConfiguration.Load() method loads configuration by looking up a file path in the App.config.
            // This approach allows applications to share configuration files and to update them.
            ApplicationConfiguration configuration = new ApplicationConfiguration();

            // Step 1 - Specify the server identity.
            configuration.ApplicationName = "My Server Name";
            configuration.ApplicationType = ApplicationType.Server;
            configuration.ApplicationUri = "http://localhost/VendorId/ApplicationId/InstanceId";
            configuration.ProductUri = "http://VendorId/ProductId/VersionId";

            configuration.SecurityConfiguration = new SecurityConfiguration();

            // Step 2 - Specify the server's application instance certificate.

            // Application instance certificates must be placed in a windows certficate store because that is 
            // the best way to protect the private key. Certificates in a store are identified with 4 parameters:
            // StoreLocation, StoreName, SubjectName and Thumbprint.
            //
            // In this example the following values are used:
            // 
            //   LocalMachine    - use the machine wide certificate store.
            //   Personal        - use the store for individual certificates.
            //   ApplicationName - use the application name as a search key.   

            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier();
            configuration.SecurityConfiguration.ApplicationCertificate.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.ApplicationCertificate.StorePath = "LocalMachine\\My";
            configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = configuration.ApplicationName;

            // trust all applications installed on the same machine.
            configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = "LocalMachine\\My";

            // find the certificate in the store.
            X509Certificate2 serverCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            // create a new certificate if one not found.
            if (serverCertificate == null)
            {
                // this code would normally be called as part of the installer - called here to illustrate.
                // create a new certificate an place it in the LocalMachine/Personal store.
                serverCertificate = CertificateFactory.CreateCertificate(
                    configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                    configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                    configuration.ApplicationUri,
                    configuration.ApplicationName,
                    null,
                    null,
                    1024,
                    300);

                Console.WriteLine("Created server certificate: {0}", serverCertificate.Subject);
            }

            // Step 4 - Specify the supported transport quotas.

            // The transport quotas are used to set limits on the contents of messages and are
            // used to protect against DOS attacks and rogue clients. They should be set to
            // reasonable values.
            configuration.TransportQuotas = new TransportQuotas();
            configuration.TransportQuotas.OperationTimeout = 60000;

            configuration.ServerConfiguration = new ServerConfiguration();

            // turn off registration with the discovery server.
            configuration.ServerConfiguration.MaxRegistrationInterval = 0;

            // Step 5 - Specify the based addresses - one per binding specified above.
            configuration.ServerConfiguration.BaseAddresses.Add(DefaultHttpUrl);
            configuration.ServerConfiguration.BaseAddresses.Add(DefaultTcpUrl);

            // Step 6 - Specify the security policies.

            // Security policies control what security must be used to connect to the server.
            // The SDK will automatically create EndpointDescriptions for each combination of 
            // security policy and base address. 
            //
            // Note that some bindings only allow one policy per URL so the SDK will append 
            // text to the base addresses in order to ensure that each policy has a unique URL.
            // The first policy specified in the configuration is assigned the base address.

            // this policy requires signing and encryption.
            ServerSecurityPolicy policy1 = new ServerSecurityPolicy();

            policy1.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            policy1.SecurityPolicyUri = SecurityPolicies.Basic128Rsa15;

            configuration.ServerConfiguration.SecurityPolicies.Add(policy1);

            // this policy does not require any security.
            ServerSecurityPolicy policy2 = new ServerSecurityPolicy();

            policy2.SecurityMode = MessageSecurityMode.None;
            policy2.SecurityPolicyUri = SecurityPolicies.None;

            configuration.ServerConfiguration.SecurityPolicies.Add(policy2);

            // specify the supported user token types.
            configuration.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Anonymous));
            configuration.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName));

            // Step 6 - Validate the configuration.

            // This step checks if the configuration is consistent and assigns a few internal variables
            // that are used by the SDK. This is called automatically if the configuration is loaded from
            // a file using the ApplicationConfiguration.Load() method.          
            await configuration.Validate(ApplicationType.Server);

            return configuration;
        }

        /// <summary>
        /// Creates a minimal endpoint description which allows a client to connect to a server.
        /// </summary>
        /// <remarks>
        /// In most cases the client will use the server's discovery endpoint to fetch the information
        /// constained in this structure.
        /// </remarks>
        public static async Task<EndpointDescription> CreateEndpointDescription()
        {
            // create the endpoint description.
            EndpointDescription endpointDescription = new EndpointDescription();

            // endpointDescription.EndpointUrl = Utils.Format("http://{0}:61211/UA/SampleClient", System.Net.Dns.GetHostName());
            endpointDescription.EndpointUrl = Utils.Format("opc.tcp://{0}:51210/UA/SampleServer", System.Net.Dns.GetHostName());

            // specify the security policy to use.
            endpointDescription.SecurityPolicyUri = SecurityPolicies.Basic128Rsa15;
            endpointDescription.SecurityMode = MessageSecurityMode.SignAndEncrypt;

            // specify the transport profile.
            // endpointDescription.TransportProfileUri = Profiles.HttpsBinaryTransport;
            endpointDescription.TransportProfileUri = Profiles.UaTcpTransport;

            endpointDescription.Server.DiscoveryUrls.Add(Utils.Format("http://{0}:61211/UA/SampleClient/discovery", System.Net.Dns.GetHostName()));

            // load the the server certificate from the local certificate store.
            CertificateIdentifier certificateIdentifier = new CertificateIdentifier();

            certificateIdentifier.StoreType = CertificateStoreType.X509Store;
            certificateIdentifier.StorePath = "LocalMachine\\My";
            certificateIdentifier.SubjectName = "UA Sample Client";

            X509Certificate2 serverCertificate = await certificateIdentifier.Find();

            if (serverCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, "Could not find server certificate: {0}", certificateIdentifier.SubjectName);
            }

            endpointDescription.ServerCertificate = serverCertificate.RawData;

            return endpointDescription;
        }
    }
}
