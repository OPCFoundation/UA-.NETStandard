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
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;


namespace Opc.Ua.Gds.Tests
{

    public class GlobalDiscoveryTestClient
    {
        public GlobalDiscoveryServerClient GDSClient => _client;
        public static bool AutoAccept = false;

        public GlobalDiscoveryTestClient(bool autoAccept)
        {
            AutoAccept = autoAccept;
        }

        public IUserIdentity AppUser { get; private set; }
        public IUserIdentity AdminUser { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public async Task LoadClientConfiguration(int port = -1)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = "Global Discovery Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryTestClient"
            };

            // load the application configuration.
            Config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(true, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            Config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);

            GlobalDiscoveryTestClientConfiguration gdsClientConfiguration = application.ApplicationConfiguration.ParseExtension<GlobalDiscoveryTestClientConfiguration>();
            _client = new GlobalDiscoveryServerClient(application, gdsClientConfiguration.GlobalDiscoveryServerUrl) {
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
        }

        public void DisconnectClient()
        {
            Console.WriteLine("Disconnect Session. Waiting for exit...");

            if (_client != null)
            {
                GlobalDiscoveryServerClient gdsClient = _client;
                _client = null;
                gdsClient.Disconnect();
            }
        }

        public string ReadLogFile()
        {
            return File.ReadAllText(Utils.ReplaceSpecialFolderNames(Config.TraceConfiguration.OutputFilePath));
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

        private GlobalDiscoveryServerClient _client;

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
