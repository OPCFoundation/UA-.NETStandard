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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Base class for GDS compliance tests. Starts an in-process
    /// ReferenceServer with the GDS node manager enabled.
    /// </summary>
    public abstract class GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            m_logger.LogInformation("GDS Test PkiRoot: {PkiRoot}", m_pkiRoot);

            string databaseStorePath = Path.Combine(m_pkiRoot, "gds", "gdsdb.json");
            var gdsConfig = new GlobalDiscoveryServerConfiguration
            {
                DatabaseStorePath = databaseStorePath
            };

            ServerFixture = new ServerFixture<ReferenceServer>(
                t =>
                {
                    var server = new ReferenceServer(t);
                    server.UserDatabase.CreateUser(
                        "sysadmin",
                        "demo"u8,
                        [
                            GdsRole.DiscoveryAdmin,
                            GdsRole.CertificateAuthorityAdmin,
                            GdsRole.RegistrationAuthorityAdmin,
                            Role.SecurityAdmin,
                            Role.AuthenticatedUser
                        ]);
                    server.AddNodeManager(new GdsNodeManagerFactory(gdsConfig));
                    return server;
                })
            {
                AutoAccept = true,
                SecurityNone = true,
                OperationLimits = true
            };

            await ServerFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength =
                ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;

            // Enable username token policy so sysadmin can authenticate
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies =
                new UserTokenPolicy[] {
                    new(UserTokenType.Anonymous),
                    new(UserTokenType.UserName)
                }.ToArrayOf();

            ReferenceServer = await ServerFixture.StartAsync().ConfigureAwait(false);

            ServerUrl = new Uri(
                Utils.UriSchemeOpcTcp +
                "://localhost:" +
                ServerFixture.Port.ToString(CultureInfo.InvariantCulture));

            m_logger.LogInformation("GDS Server started at {Url}", ServerUrl);

            ClientFixture = new ClientFixture(telemetry: Telemetry);
            await ClientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ClientFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ClientFixture.Config.TransportQuotas.MaxByteStringLength =
                ClientFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;

            Session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256,
                    default, new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);

            Assert.That(Session, Is.Not.Null, "Failed to create session");

            // Ensure the session factory knows about GDS types
            if (!Session.Factory.ContainsEncodeableType(
                DataTypeIds.ApplicationRecordDataType))
            {
                Session.Factory.Builder.AddOpcUaGds().Commit();
            }

            // Also ensure the session's message context factory knows
            // about GDS types so the binary decoder can decode them.
            if (!Session.MessageContext.Factory.ContainsEncodeableType(
                DataTypeIds.ApplicationRecordDataType))
            {
                Session.MessageContext.Factory.Builder.AddOpcUaGds().Commit();
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (Session != null)
            {
                try
                {
                    await Session.CloseAsync(5000, true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error closing session during teardown.");
                }
                Session.Dispose();
                Session = null;
            }

            if (ServerFixture != null)
            {
                try
                {
                    await ServerFixture.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error stopping server during teardown.");
                }
                await Task.Delay(100).ConfigureAwait(false);
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

        public const int TransportQuotaMaxMessageSize = 4 * 1024 * 1024;
        public const int TransportQuotaMaxStringLength = 1 * 1024 * 1024;

        public ServerFixture<ReferenceServer> ServerFixture { get; private set; }
        public ClientFixture ClientFixture { get; private set; }
        public ISession Session { get; private set; }
        public Uri ServerUrl { get; private set; }
        public ReferenceServer ReferenceServer { get; private set; }
        public ITelemetryContext Telemetry { get; }

        private string m_pkiRoot;

        protected GdsTestFixture()
        {
            Telemetry = NUnitTelemetryContext.Create();
            m_logger = Telemetry.CreateLogger<GdsTestFixture>();
        }

        /// <summary>
        /// Helper to resolve an ExpandedNodeId to a NodeId using the session namespace table.
        /// </summary>
        protected NodeId ToNodeId(ExpandedNodeId expandedNodeId)
        {
            return ExpandedNodeId.ToNodeId(expandedNodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Helper to browse children of a given node using hierarchical references.
        /// </summary>
        protected async Task<ReferenceDescription[]> BrowseChildrenAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[] {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"Browse of {nodeId} failed: {response.Results[0].StatusCode}");

            return [.. response.Results[0].References];
        }

        /// <summary>
        /// Helper to find a child reference by browse name.
        /// </summary>
        protected async Task<ReferenceDescription> FindChildAsync(
            NodeId parentId,
            string browseName,
            CancellationToken ct = default)
        {
            ReferenceDescription[] refs = await BrowseChildrenAsync(parentId, ct).ConfigureAwait(false);
            foreach (ReferenceDescription r in refs)
            {
                if (r.BrowseName.Name == browseName)
                {
                    return r;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a test ApplicationRecordDataType for registration.
        /// </summary>
        protected static ApplicationRecordDataType CreateTestApplicationRecord(
            string suffix = "1",
            ApplicationType appType = ApplicationType.Server)
        {
            var record = new ApplicationRecordDataType
            {
                ApplicationUri = $"urn:opcfoundation.org:tests:test:app:{suffix}",
                ApplicationType = appType,
                ApplicationNames = new LocalizedText[] {
                    new("en-US", $"Test Application {suffix}")
                }.ToArrayOf(),
                ProductUri = $"urn:opcfoundation.org:tests:test:product:{suffix}"
            };

            // GDS rejects DiscoveryUrls for Client type applications
            if (appType != ApplicationType.Client)
            {
                record.DiscoveryUrls = new string[] {
                    $"opc.tcp://localhost:4840/ConformanceTestApp{suffix}"
                }.ToArrayOf();
                record.ServerCapabilities = new string[] {
                    "DA"
                }.ToArrayOf();
            }

            return record;
        }

        private readonly ILogger m_logger;
    }
}
