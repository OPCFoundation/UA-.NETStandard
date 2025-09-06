/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    public class GlobalDiscoveryTestClient
    {
        public GlobalDiscoveryServerClient GDSClient { get; private set; }
        public static bool AutoAccept { get; set; }

        public GlobalDiscoveryTestClient(
            bool autoAccept,
            string storeType = CertificateStoreType.Directory)
        {
            AutoAccept = autoAccept;
            m_storeType = storeType;
        }

        public IUserIdentity AppUser { get; private set; }
        public IUserIdentity AdminUser { get; private set; }
        public IUserIdentity Anonymous { get; private set; }
        public ApplicationTestData OwnApplicationTestData { get; private set; }
        public ApplicationConfiguration Configuration { get; private set; }

        public async Task LoadClientConfigurationAsync(int port = -1, bool clean = true)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            string configSectionName = "Opc.Ua.GlobalDiscoveryTestClient";
            if (m_storeType == CertificateStoreType.X509Store)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    throw new PlatformNotSupportedException(
                        "X509 Store with crls is only supported on Windows");
                }
                configSectionName = "Opc.Ua.GlobalDiscoveryTestClientX509Stores";
            }

            m_application = new ApplicationInstance
            {
                ApplicationName = "Global Discovery Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = configSectionName
            };

#if USE_FILE_CONFIG
            // load the application configuration.
            Configuration = await m_application.LoadApplicationConfigurationAsync(false)
                .ConfigureAwait(false);
#else
            string root = Path.Combine("%LocalApplicationData%", "OPC");
            string pkiRoot = Path.Combine(root, "pki");
            var clientConfig = new GlobalDiscoveryTestClientConfiguration
            {
                GlobalDiscoveryServerUrl = "opc.tcp://localhost:58810/GlobalDiscoveryTestServer",
                AppUserName = "appuser",
                AppPassword = "demo",
                AdminUserName = "appadmin",
                AdminPassword = "demo"
            };

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=Global Discovery Test Client, O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    pkiRoot);

            // build the application configuration.
            Configuration = await m_application
                .Build(
                    "urn:localhost:opcfoundation.org:GlobalDiscoveryTestClient",
                    "http://opcfoundation.org/UA/GlobalDiscoveryTestClient")
                .AsClient()
                .SetDefaultSessionTimeout(600000)
                .SetMinSubscriptionLifetime(10000)
                .AddSecurityConfiguration(applicationCerts, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(true)
                .SetMinimumCertificateKeySize(1024)
                .AddExtension<GlobalDiscoveryTestClientConfiguration>(null, clientConfig)
                .SetOutputFilePath(Path.Combine(root, "Logs", "Opc.Ua.Gds.Tests.log.txt"))
                .SetTraceMasks(519)
                .CreateAsync()
                .ConfigureAwait(false);
#endif

            if (clean)
            {
                string thumbprint = Configuration.SecurityConfiguration.ApplicationCertificate.Thumbprint;
                if (thumbprint != null)
                {
                    using ICertificateStore store = Configuration.SecurityConfiguration
                        .ApplicationCertificate
                        .OpenStore();
                    await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                }

                // always start with clean cert store
                await TestUtils
                    .CleanupTrustListAsync(
                        Configuration.SecurityConfiguration.ApplicationCertificate.OpenStore())
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Configuration.SecurityConfiguration.TrustedIssuerCertificates.OpenStore())
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore())
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Configuration.SecurityConfiguration.RejectedCertificateStore.OpenStore())
                    .ConfigureAwait(false);
            }

            // check the application certificate.
            bool haveAppCertificate = await m_application
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            Configuration.CertificateValidator.CertificateValidation
                += new CertificateValidationEventHandler(
                CertificateValidator_CertificateValidation);

            GlobalDiscoveryTestClientConfiguration gdsClientConfiguration =
                Configuration.ParseExtension<GlobalDiscoveryTestClientConfiguration>();
            GDSClient = new GlobalDiscoveryServerClient(
                Configuration,
                gdsClientConfiguration.GlobalDiscoveryServerUrl)
            {
                EndpointUrl = TestUtils.PatchOnlyGDSEndpointUrlPort(
                    gdsClientConfiguration.GlobalDiscoveryServerUrl,
                    port)
            };

            await SetEndpointAsync(SecurityPolicies.Aes256_Sha256_RsaPss)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(gdsClientConfiguration.AppUserName))
            {
                AppUser = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                AppUser = new UserIdentity(
                    gdsClientConfiguration.AppUserName,
                    gdsClientConfiguration.AppPassword);
            }
            AdminUser = new UserIdentity(
                gdsClientConfiguration.AdminUserName,
                gdsClientConfiguration.AdminPassword);
            Anonymous = new UserIdentity();
        }

        /// <summary>
        /// Register the Test Client at the used GDS, needed to test the ApplicationSelfAdminPrivilege
        /// </summary>
        public async Task<bool> RegisterTestClientAtGdsAsync()
        {
            try
            {
                OwnApplicationTestData = GetOwnApplicationData();

                GDSClient.AdminCredentials = AdminUser;
                //register
                NodeId id = await RegisterAsync(OwnApplicationTestData).ConfigureAwait(false);
                if (id == null)
                {
                    return false;
                }
                OwnApplicationTestData.ApplicationRecord.ApplicationId = id;
                //start Key Pair Request
                NodeId req_id = await StartNewKeyPairAsync(OwnApplicationTestData).ConfigureAwait(
                    false);
                if (req_id == null)
                {
                    return false;
                }

                OwnApplicationTestData.CertificateRequestId = req_id;
                //Finish KeyPairRequest
                (byte[] certificate, byte[] privateKey) = await FinishKeyPairAsync(
                    OwnApplicationTestData).ConfigureAwait(false);
                if (certificate == null || privateKey == null)
                {
                    return false;
                }
                //apply cert
                await ApplyNewApplicationInstanceCertificateAsync(certificate, privateKey)
                    .ConfigureAwait(false);
                OwnApplicationTestData.Certificate = certificate;
                OwnApplicationTestData.PrivateKey = privateKey;
                OwnApplicationTestData.CertificateRequestId = null;
            }
            catch (ArgumentException e)
            {
                Console.WriteLine("RegisterTestClientAtGds at GDS failed" + e);
                return false;
            }

            return true;
        }

        public async Task DisconnectClientAsync()
        {
            Console.WriteLine("Disconnect Session. Waiting for exit...");

            if (GDSClient != null)
            {
                GlobalDiscoveryServerClient gdsClient = GDSClient;
                GDSClient = null;
                await gdsClient.DisconnectAsync().ConfigureAwait(false);
            }
        }

        public string ReadLogFile()
        {
            return File.ReadAllText(
                Utils.ReplaceSpecialFolderNames(Configuration.TraceConfiguration.OutputFilePath));
        }

        private async Task ApplyNewApplicationInstanceCertificateAsync(
            byte[] certificate,
            byte[] privateKey)
        {
            using X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(certificate);
            X509Certificate2 certWithPrivateKey = CertificateFactory
                .CreateCertificateWithPEMPrivateKey(
                    x509,
                    privateKey);
            GDSClient.Configuration.SecurityConfiguration.ApplicationCertificate
                = new CertificateIdentifier(
                certWithPrivateKey);
            ICertificateStore store = GDSClient.Configuration.SecurityConfiguration
                .ApplicationCertificate
                .OpenStore();
            await store.AddAsync(certWithPrivateKey).ConfigureAwait(false);
        }

        private async Task<(byte[] certificate, byte[] privateKey)> FinishKeyPairAsync(
            ApplicationTestData ownApplicationTestData)
        {
            GDSClient.ConnectAsync().GetAwaiter().GetResult();
            //get cert
            (byte[] certificate, byte[] privateKey, _) = await GDSClient.FinishRequestAsync(
                ownApplicationTestData.ApplicationRecord.ApplicationId,
                ownApplicationTestData.CertificateRequestId).ConfigureAwait(false);
            await GDSClient.DisconnectAsync().ConfigureAwait(false);

            return (certificate, privateKey);
        }

        private async Task<NodeId> StartNewKeyPairAsync(ApplicationTestData ownApplicationTestData)
        {
            await GDSClient.ConnectAsync().ConfigureAwait(false);
            //request new Cert
            NodeId req_id = await GDSClient.StartNewKeyPairRequestAsync(
                ownApplicationTestData.ApplicationRecord.ApplicationId,
                ownApplicationTestData.CertificateGroupId,
                ownApplicationTestData.CertificateTypeId,
                ownApplicationTestData.Subject,
                ownApplicationTestData.DomainNames,
                ownApplicationTestData.PrivateKeyFormat,
                ownApplicationTestData.PrivateKeyPassword).ConfigureAwait(false);

            await GDSClient.DisconnectAsync().ConfigureAwait(false);
            return req_id;
        }

        private async Task<NodeId> RegisterAsync(ApplicationTestData ownApplicationTestData)
        {
            await GDSClient.ConnectAsync().ConfigureAwait(false);
            NodeId id = await GDSClient.RegisterApplicationAsync(ownApplicationTestData.ApplicationRecord).ConfigureAwait(false);
            await GDSClient.DisconnectAsync().ConfigureAwait(false);
            return id;
        }

        private static void CertificateValidator_CertificateValidation(
            CertificateValidator validator,
            CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = AutoAccept;
                if (AutoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        private ApplicationTestData GetOwnApplicationData()
        {
            return new ApplicationTestData
            {
                ApplicationRecord = new ApplicationRecordDataType
                {
                    ApplicationUri = GDSClient.Configuration.ApplicationUri,
                    ApplicationType = GDSClient.Configuration.ApplicationType,
                    ProductUri = GDSClient.Configuration.ProductUri,
                    ApplicationNames = [new LocalizedText(GDSClient.Configuration.ApplicationName)],
                    ApplicationId = new NodeId(Guid.NewGuid())
                },
                PrivateKeyFormat = "PEM",
                Subject = $"CN={GDSClient.Configuration.ApplicationName},DC={Utils.GetHostName()},O=OPC Foundation"
            };
        }

        /// <summary>
        /// Sets the GDSClient's endpoint by SecurityPolicy URI.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task SetEndpointAsync(string securityPolicyUri)
        {
            if (Configuration == null)
            {
                throw new InvalidOperationException("Client configuration must be loaded before setting endpoint.");
            }
            if (GDSClient == null)
            {
                throw new InvalidOperationException("GDSClient must be initialized before setting endpoint.");
            }
            var endpointConfiguration = EndpointConfiguration.Create(Configuration);
            var discoveryClient = DiscoveryClient.Create(new Uri(GDSClient.EndpointUrl), endpointConfiguration);
            EndpointDescriptionCollection endpoints = await discoveryClient.GetEndpointsAsync(null).ConfigureAwait(false);
            await discoveryClient.CloseAsync().ConfigureAwait(false);
            EndpointDescription selectedEndpoint = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityPolicyUri == securityPolicyUri)
                {
                    selectedEndpoint = ep;
                    break;
                }
            }
            if (selectedEndpoint == null)
            {
                throw new ArgumentException($"No endpoint found for SecurityPolicyUri '{securityPolicyUri}'.");
            }
            GDSClient.Endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
        }

        private ApplicationInstance m_application;
        private readonly string m_storeType;
    }

    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public class GlobalDiscoveryTestClientConfiguration
    {
        [DataMember(Order = 1)]
        public string GlobalDiscoveryServerUrl { get; set; }

        [DataMember(Order = 2)]
        public string AppUserName { get; set; }

        [DataMember(Order = 3)]
        public string AppPassword { get; set; }

        [DataMember(Order = 4, IsRequired = true)]
        public string AdminUserName { get; set; }

        [DataMember(Order = 5, IsRequired = true)]
        public string AdminPassword { get; set; }
    }
}
