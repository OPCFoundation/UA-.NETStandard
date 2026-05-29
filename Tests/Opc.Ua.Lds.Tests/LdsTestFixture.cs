/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Lds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// Base fixture for in-process Local Discovery Server (LDS) conformance tests.
    /// Starts an <see cref="LdsServer"/> on an ephemeral port. Unlike
    /// <see cref="TestFixture"/>, it does NOT open a UA Session because the
    /// LDS only implements the discovery service set; tests must create a
    /// <see cref="DiscoveryClient"/> or <see cref="RegistrationClient"/> via
    /// the supplied helpers.
    /// </summary>
    /// <remarks>
    /// Multicast (LDS-ME) is OFF by default. Tests that need the network
    /// surface should derive from <see cref="LdsMeTestFixture"/>, which enables
    /// loopback-only mDNS announcement.
    /// </remarks>
    public abstract class LdsTestFixture
    {
        public ServerFixture<LdsServer> ServerFixture { get; private set; }
        public ClientFixture ClientFixture { get; private set; }
        public Uri ServerUrl { get; private set; }
        public LdsServer Lds { get; private set; }
        public ITelemetryContext Telemetry { get; }

        protected virtual bool EnableMulticast => false;

        protected LdsTestFixture()
        {
            Telemetry = NUnitTelemetryContext.Create();
            m_logger = Telemetry.CreateLogger<LdsTestFixture>();
        }

        [OneTimeSetUp]
        public async Task LdsOneTimeSetUpAsync()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            m_logger.LogInformation("LDS PkiRoot: {PkiRoot}", m_pkiRoot);

            ServerFixture = new ServerFixture<LdsServer>(t =>
            {
                var server = new LdsServer(t);
                if (EnableMulticast)
                {
                    server.MulticastFactory = lds => new MulticastDiscovery(
                        lds.Store,
                        loopbackOnly: true,
                        logger: t.CreateLogger<MulticastDiscovery>());
                }
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true,
                AllNodeManagers = false,
                OperationLimits = false
            };

            await ServerFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            Lds = await ServerFixture.StartAsync().ConfigureAwait(false);

            ServerUrl = new Uri(
                Utils.UriSchemeOpcTcp + "://localhost:" +
                ServerFixture.Port.ToString(CultureInfo.InvariantCulture));

            m_logger.LogInformation("LDS started at {Url}", ServerUrl);

            ClientFixture = new ClientFixture(telemetry: Telemetry);
            await ClientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task LdsOneTimeTearDownAsync()
        {
            if (ServerFixture != null)
            {
                try
                {
                    await ServerFixture.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error stopping LDS.");
                }
            }

            ClientFixture?.Dispose();

            try
            {
                if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [SetUp]
        public void ResetLdsStore()
        {
            Lds?.Store.Clear();
        }

        protected Task<DiscoveryClient> CreateDiscoveryClientAsync(CancellationToken ct = default)
        {
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            return DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: ct);
        }

        protected async Task<RegistrationClient> CreateRegistrationClientAsync(
            string securityPolicy = SecurityPolicies.Basic256Sha256,
            MessageSecurityMode securityMode = MessageSecurityMode.Sign,
            CancellationToken ct = default)
        {
            using DiscoveryClient discovery = await CreateDiscoveryClientAsync(ct).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery
                .GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);

            EndpointDescription matching = null;
            foreach (EndpointDescription e in endpoints)
            {
                if (string.Equals(e.SecurityPolicyUri, securityPolicy, StringComparison.Ordinal)
                    && e.SecurityMode == securityMode)
                {
                    matching = e;
                    break;
                }
            }

            if (matching == null)
            {
                throw new InvalidOperationException(
                    $"LDS does not expose endpoint with policy={securityPolicy} mode={securityMode}.");
            }

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);

            Certificate instanceCertificate = ClientFixture.Config.CertificateManager?
                .GetInstanceCertificate(matching.SecurityPolicyUri ?? SecurityPolicies.None)?
                .Certificate?
                .AddRef();

            return await RegistrationClient
                .CreateAsync(
                    ClientFixture.Config,
                    matching,
                    endpointConfiguration,
                    instanceCertificate,
                    ct: ct)
                .ConfigureAwait(false);
        }

        private string m_pkiRoot;
        private readonly ILogger m_logger;
    }

    public abstract class LdsMeTestFixture : LdsTestFixture
    {
        protected override bool EnableMulticast => true;
    }
}
