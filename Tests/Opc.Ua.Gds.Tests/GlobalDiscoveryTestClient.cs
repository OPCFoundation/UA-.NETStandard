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
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{

    public class GlobalDiscoveryTestClient
    {
        public GlobalDiscoveryServerClient GDSClient => m_client;
        public static bool AutoAccept = false;

        public GlobalDiscoveryTestClient(bool autoAccept)
        {
            AutoAccept = autoAccept;
        }

        public IUserIdentity AppUser { get; private set; }
        public IUserIdentity AdminUser { get; private set; }
        public IUserIdentity Anonymous { get; private set; }
        public ApplicationTestData OwnApplicationTestData { get; private set; }
        public ApplicationConfiguration Configuration { get; private set; }
        #region public methods
        public async Task LoadClientConfiguration(int port = -1)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            m_application = new ApplicationInstance {
                ApplicationName = "Global Discovery Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryTestClient"
            };

#if USE_FILE_CONFIG
            // load the application configuration.
            Configuration = await m_application.LoadApplicationConfiguration(false).ConfigureAwait(false);
#else
            string root = Path.Combine("%LocalApplicationData%", "OPC");
            string pkiRoot = Path.Combine(root, "pki");
            var clientConfig = new GlobalDiscoveryTestClientConfiguration() {
                GlobalDiscoveryServerUrl = "opc.tcp://localhost:58810/GlobalDiscoveryTestServer",
                AppUserName = "appuser",
                AppPassword = "demo",
                AdminUserName = "appadmin",
                AdminPassword = "demo"
            };

            // build the application configuration.
            Configuration = await m_application
                .Build(
                    "urn:localhost:opcfoundation.org:GlobalDiscoveryTestClient",
                    "http://opcfoundation.org/UA/GlobalDiscoveryTestClient")
                .AsClient()
                .SetDefaultSessionTimeout(600000)
                .SetMinSubscriptionLifetime(10000)
                .AddSecurityConfiguration(
                    "CN=Global Discovery Test Client, O=OPC Foundation, DC=localhost",
                    pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(true)
                .SetMinimumCertificateKeySize(1024)
                .AddExtension<GlobalDiscoveryTestClientConfiguration>(null, clientConfig)
                .SetOutputFilePath(Path.Combine(root, "Logs", "Opc.Ua.Gds.Tests.log.txt"))
                .SetTraceMasks(519)
                .Create().ConfigureAwait(false);
#endif
            // check the application certificate.
            bool haveAppCertificate = await m_application.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            Configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);

            GlobalDiscoveryTestClientConfiguration gdsClientConfiguration = Configuration.ParseExtension<GlobalDiscoveryTestClientConfiguration>();
            m_client = new GlobalDiscoveryServerClient(Configuration, gdsClientConfiguration.GlobalDiscoveryServerUrl) {
                EndpointUrl = TestUtils.PatchOnlyGDSEndpointUrlPort(gdsClientConfiguration.GlobalDiscoveryServerUrl, port)
            };
            if (String.IsNullOrEmpty(gdsClientConfiguration.AppUserName))
            {
                AppUser = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                AppUser = new UserIdentity(gdsClientConfiguration.AppUserName, gdsClientConfiguration.AppPassword);
            }
            AdminUser = new UserIdentity(gdsClientConfiguration.AdminUserName, gdsClientConfiguration.AdminPassword);
            Anonymous = new UserIdentity();
        }

        /// <summary>
        /// Register the Test Client at the used GDS, needed to test the ApplicationSelfAdminPrivilege
        /// </summary>
        public bool RegisterTestClientAtGds()
        {
            try
            {
                OwnApplicationTestData = GetOwnApplicationData();

                m_client.AdminCredentials = AdminUser;
                //register
                NodeId id = Register(OwnApplicationTestData);
                if (id == null)
                {
                    return false;
                }
                OwnApplicationTestData.ApplicationRecord.ApplicationId = id;
                //start Key Pair Request
                NodeId req_id = StartNewKeyPair(OwnApplicationTestData);
                if (req_id == null)
                {
                    return false;
                }

                OwnApplicationTestData.CertificateRequestId = req_id;
                //Finish KeyPairRequest
                byte[] certificate, privateKey;
                FinishKeyPair(OwnApplicationTestData, out certificate, out privateKey);
                if (certificate == null || privateKey == null)
                {
                    return false;
                }
                //apply cert
                ApplyNewApplicationInstanceCertificateAsync(certificate, privateKey).Wait();
                OwnApplicationTestData.Certificate = certificate;
                OwnApplicationTestData.PrivateKey = privateKey;
                OwnApplicationTestData.CertificateRequestId = null;
            }
            catch (ArgumentException e)
            {
                Console.WriteLine("RegisterTestClientAtGds at GDS failed" + e.ToString());
                return false;
            }


            return true;
        }

        public void DisconnectClient()
        {
            Console.WriteLine("Disconnect Session. Waiting for exit...");

            if (m_client != null)
            {
                GlobalDiscoveryServerClient gdsClient = m_client;
                m_client = null;
                gdsClient.Disconnect();
            }
        }

        public string ReadLogFile()
        {
            return File.ReadAllText(Utils.ReplaceSpecialFolderNames(Configuration.TraceConfiguration.OutputFilePath));
        }
        #endregion
        #region Private Methods
        private async Task ApplyNewApplicationInstanceCertificateAsync(byte[] certificate, byte[] privateKey)
        {
            using (var x509 = new X509Certificate2(certificate))
            {
                var certWithPrivateKey = CertificateFactory.CreateCertificateWithPEMPrivateKey(x509, privateKey);
                m_client.Configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier(certWithPrivateKey);
                var store = m_client.Configuration.SecurityConfiguration.ApplicationCertificate.OpenStore();
                await store.Add(certWithPrivateKey).ConfigureAwait(false);
            }
        }

        private void FinishKeyPair(ApplicationTestData ownApplicationTestData, out byte[] certificate, out byte[] privateKey)
        {
            m_client.Connect(m_client.EndpointUrl).Wait();
            //get cert
            certificate = m_client.FinishRequest(
             ownApplicationTestData.ApplicationRecord.ApplicationId,
             ownApplicationTestData.CertificateRequestId,
             out privateKey,
             out _
             );
            m_client.Disconnect();
        }

        private NodeId StartNewKeyPair(ApplicationTestData ownApplicationTestData)
        {
            m_client.Connect(m_client.EndpointUrl).Wait();
            //request new Cert
            var req_id = m_client.StartNewKeyPairRequest(
             ownApplicationTestData.ApplicationRecord.ApplicationId,
             ownApplicationTestData.CertificateGroupId,
             ownApplicationTestData.CertificateTypeId,
             ownApplicationTestData.Subject,
             ownApplicationTestData.DomainNames,
             ownApplicationTestData.PrivateKeyFormat,
             ownApplicationTestData.PrivateKeyPassword
             );

            m_client.Disconnect();
            return req_id;
        }

        private NodeId Register(ApplicationTestData ownApplicationTestData)
        {
            m_client.Connect(m_client.EndpointUrl).Wait();
            var id = m_client.RegisterApplication(ownApplicationTestData.ApplicationRecord);
            m_client.Disconnect();
            return id;
        }
        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
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
            ApplicationTestData
                //fill application record data type with own Data
                ownApplicationTestData = new ApplicationTestData {
                    ApplicationRecord = new ApplicationRecordDataType {
                        ApplicationUri = m_client.Configuration.ApplicationUri,
                        ApplicationType = m_client.Configuration.ApplicationType,
                        ProductUri = m_client.Configuration.ProductUri,
                        ApplicationNames = new LocalizedTextCollection() { new LocalizedText(m_client.Configuration.ApplicationName) },
                        ApplicationId = new NodeId(Guid.NewGuid())
                    },
                    PrivateKeyFormat = "PEM",
                    Subject = $"CN={m_client.Configuration.ApplicationName},DC={Utils.GetHostName()},O=OPC Foundation",
                };
            return ownApplicationTestData;
        }

        #endregion


        private GlobalDiscoveryServerClient m_client;
        private ApplicationInstance m_application;

    }

    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Opc.Ua.Gds.Namespaces.OpcUaGds + "Configuration.xsd")]
    public class GlobalDiscoveryTestClientConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryTestClientConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        #region Public
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
        #endregion

        #region Private Members
        #endregion
    }

}
