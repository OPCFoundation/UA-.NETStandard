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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client fixture for tests.
    /// </summary>
    public class ClientFixture
    {
        public ApplicationConfiguration Config { get; private set; }
        public ConfiguredEndpoint Endpoint { get; private set; }
        public string EndpointUrl { get; private set; }
        public string ReverseConnectUri { get; private set; }
        public ReverseConnectManager ReverseConnectManager { get; private set; }
        public uint SessionTimeout { get; set; } = 10000;
        public int OperationTimeout { get; set; } = 10000;
        public int TraceMasks { get; set; } = Utils.TraceMasks.Error | Utils.TraceMasks.Security;

        #region Public Methods
        /// <summary>
        /// Load the default client configuration.
        /// </summary>
        public async Task LoadClientConfiguration(string pkiRoot = null, string clientName = "TestClient")
        {
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = clientName
            };

            pkiRoot = pkiRoot ?? Path.Combine("%LocalApplicationData%", "OPC", "pki");

            // build the application configuration.
            Config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:" + clientName,
                    "http://opcfoundation.org/UA/" + clientName)
                .AsClient()
                .AddSecurityConfiguration(
                    "CN=" + clientName + ", O=OPC Foundation, DC=localhost",
                    pkiRoot)
                .SetApplicationCertificateTypes("RSA,nistP256,nistP384,brainpoolP256r1,brainpoolP384r1")
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetMinimumCertificateKeySize(1024)
                .SetOutputFilePath(Path.Combine(pkiRoot, "Logs", "Opc.Ua.Client.Tests.log.txt"))
                .SetTraceMasks(TraceMasks)
                .Create().ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            ReverseConnectManager = new ReverseConnectManager();
        }

        /// <summary>
        /// Start a host for reverse connections on random port.
        /// </summary>
        public async Task StartReverseConnectHost()
        {
            Random m_random = new Random();
            int testPort = ServerFixtureUtils.GetNextFreeIPPort();
            bool retryStartServer = false;
            int serverStartRetries = 25;
            do
            {
                try
                {
                    var reverseConnectUri = new Uri("opc.tcp://localhost:" + testPort);
                    ReverseConnectManager.AddEndpoint(reverseConnectUri);
                    ReverseConnectManager.StartService(Config);
                    ReverseConnectUri = reverseConnectUri.ToString();
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (serverStartRetries == 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    testPort = m_random.Next(ServerFixtureUtils.MinTestPort, ServerFixtureUtils.MaxTestPort);
                    retryStartServer = true;
                }
                await Task.Delay(m_random.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);
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
                    EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(Config, endpointUrl, true);
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
        /// Connects the url endpoint with specified security profile.
        /// </summary>
        public async Task<Session> ConnectAsync(Uri url, string securityProfile, EndpointDescriptionCollection endpoints = null)
        {
            return await ConnectAsync(await GetEndpointAsync(url, securityProfile, endpoints).ConfigureAwait(false)).ConfigureAwait(false);
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

            var session = await Session.Create(
                Config, endpoint, false, false,
                Config.ApplicationName, SessionTimeout, null, null).ConfigureAwait(false);

            Endpoint = session.ConfiguredEndpoint;

            session.KeepAlive += Session_KeepAlive;

            session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            EndpointUrl = session.ConfiguredEndpoint.EndpointUrl.ToString();

            return session;
        }

        /// <summary>
        /// Get configured endpoint from url with security profile.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="securityPolicy"></param>
        /// <param name="endpoints"></param>
        public async Task<ConfiguredEndpoint> GetEndpointAsync(
            Uri url,
            string securityPolicy,
            EndpointDescriptionCollection endpoints = null)
        {
            if (endpoints == null)
            {
                endpoints = await GetEndpoints(url).ConfigureAwait(false);
            }
            var endpointDescription = SelectEndpoint(Config, endpoints, url, securityPolicy);
            if (endpointDescription == null)
            {
                Assert.Ignore("The endpoint is not supported by the server.");
            }
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(Config);
            endpointConfiguration.OperationTimeout = OperationTimeout;
            return new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        }

        /// <summary>
        /// Select a security endpoint from description.
        /// </summary>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration configuration,
            EndpointDescriptionCollection endpoints,
            Uri url,
            string securityPolicy)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
            foreach (var endpoint in endpoints)
            {
                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme))
                {
                    // skip unsupported security policies
                    if (!configuration.SecurityConfiguration.SupportedSecurityPolicies.
                            Contains(endpoint.SecurityPolicyUri))
                    {
                        continue;
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                        continue;
                    }

                    if (selectedEndpoint?.SecurityMode < endpoint.SecurityMode &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }
            // return the selected endpoint.
            return selectedEndpoint;
        }

        /// <summary>
        /// Get endpoints from discovery endpoint.
        /// </summary>
        /// <param name="url">The url of the discovery endpoint.</param>
        public async Task<EndpointDescriptionCollection> GetEndpoints(Uri url)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = OperationTimeout;

            using (var client = DiscoveryClient.Create(url, endpointConfiguration))
            {
                return await client.GetEndpointsAsync(null).ConfigureAwait(false);
            }
        }
        #endregion

        #region Private Methods
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                session?.Dispose();
            }
        }
        #endregion
    }
}
