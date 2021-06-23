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
using System.Threading.Tasks;
using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client fixture for tests.
    /// </summary>
    public class ClientFixture
    {

        public ApplicationConfiguration Config { get; private set; }
        public ConfiguredEndpoint Endpoint { get; private set; }
        public string EndpointUrl { get; set; }
        public Session Session { get; private set; }

        #region Public Methods
        /// <summary>
        /// Load the default client configuration.
        /// </summary>
        public async Task LoadClientConfiguration(string clientName = "TestClient")
        {
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = clientName
            };

            string pkiRoot = "%LocalApplicationData%/OPC/pki";

            // build the application configuration.
            Config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:" + clientName,
                    "http://opcfoundation.org/UA/" + clientName)
                .AsClient()
                .AddSecurityConfiguration(
                    "CN=" + clientName + ", O=OPC Foundation, DC=localhost",
                    pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetMinimumCertificateKeySize(1024)
                .SetOutputFilePath(pkiRoot + "/Logs/Opc.Ua.Client.Tests.log.txt")
                .SetTraceMasks(519)
                .Create().ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        public async Task<Session> Connect(string endpointUrl)
        {
            if (String.IsNullOrEmpty(endpointUrl))
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException(endpointUrl + " is not a valid URL.", nameof(endpointUrl));
            }

            bool serverHalted;
            do
            {
                serverHalted = false;
                try
                {
                    EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(endpointUrl, true);
                    EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(Config);
                    ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                    return await ConnectAsync(endpoint).ConfigureAwait(false);
                }
                catch (ServiceResultException e)
                {
                    if (e.StatusCode == StatusCodes.BadServerHalted)
                    {
                        serverHalted = true;
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (serverHalted);

            throw new ServiceResultException(StatusCodes.BadNoCommunication);
        }

        /// <summary>
        /// Connects the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        public async Task<Session> ConnectAsync(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null)
            {
                endpoint = Endpoint;
                if (endpoint == null)
                {
                    throw new ArgumentNullException(nameof(endpoint));
                }
            }

            if (Session != null)
            {
                Session.Dispose();
                Session = null;
            }

            Session = await Session.Create(
                Config,
                endpoint,
                false,
                false,
                Config.ApplicationName,
                10000,
                null,
                null).ConfigureAwait(false);

            Endpoint = Session.ConfiguredEndpoint;

            Session.KeepAlive += Session_KeepAlive;

            Session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            EndpointUrl = Session.ConfiguredEndpoint.EndpointUrl.ToString();

            return Session;
        }

        /// <summary>
        /// Disconnect the client connection.
        /// </summary>
        public void Disconnect()
        {
            if (Session != null)
            {
                Session.Close();
                Session = null;
            }
        }
        #endregion

        #region Private Methods
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                Session?.Dispose();
                Session = null;
            }
        }
        #endregion
    }
}
