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

    public class ServerConfigurationPushTestClient
    {
        public ServerPushConfigurationClient PushClient => _client;
        public static bool AutoAccept = false;

        public ServerConfigurationPushTestClient(bool autoAccept)
        {
            AutoAccept = autoAccept;
        }

        public IUserIdentity AppUser { get; private set; }
        public IUserIdentity SysAdminUser { get; private set; }
        public string TempStorePath { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public async Task LoadClientConfiguration(int port = -1)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = "Server Configuration Push Test Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "Opc.Ua.ServerConfigurationPushTestClient"
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

            ServerConfigurationPushTestClientConfiguration clientConfiguration = application.ApplicationConfiguration.ParseExtension<ServerConfigurationPushTestClientConfiguration>();
            _client = new ServerPushConfigurationClient(application) {
                EndpointUrl = TestUtils.PatchOnlyGDSEndpointUrlPort(clientConfiguration.ServerUrl, port)
            };
            if (String.IsNullOrEmpty(clientConfiguration.AppUserName))
            {
                AppUser = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                AppUser = new UserIdentity(clientConfiguration.AppUserName, clientConfiguration.AppPassword);
            }
            SysAdminUser = new UserIdentity(clientConfiguration.SysAdminUserName, clientConfiguration.SysAdminPassword);
            TempStorePath = clientConfiguration.TempStorePath;
        }

        public void DisconnectClient()
        {
            Console.WriteLine("Disconnect Session. Waiting for exit...");

            if (_client != null)
            {
                ServerPushConfigurationClient pushClient = _client;
                _client = null;
                pushClient.Disconnect();
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

        private ServerPushConfigurationClient _client;

    }

    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaConfig)]
    public class ServerConfigurationPushTestClientConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerConfigurationPushTestClientConfiguration()
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
        [DataMember(Order = 1, IsRequired = true)]
        public string ServerUrl { get; set; }
        [DataMember(Order = 2)]
        public string AppUserName { get; set; }
        [DataMember(Order = 3)]
        public string AppPassword { get; set; }
        [DataMember(Order = 4, IsRequired = true)]
        public string SysAdminUserName { get; set; }
        [DataMember(Order = 5, IsRequired = true)]
        public string SysAdminPassword { get; set; }
        [DataMember(Order = 6, IsRequired = true)]
        public string TempStorePath { get; set; }
        #endregion

        #region Private Members
        #endregion
    }
}
