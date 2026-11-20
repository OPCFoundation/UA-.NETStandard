/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration test that drives a real, fully-started <see cref="StandardServer"/> whose
    /// <see cref="StandardServer.GetEndpointsDirector"/> is a configured <see cref="ServerLoadDirector"/>, and verifies
    /// end to end that a <c>GetEndpoints</c> request on the balancing URL is answered with a peer's endpoints while a
    /// normal request serves the local server. Closes the seam-wiring gap the director unit tests cannot exercise.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [NonParallelizable]
    public class LoadDirectionServerIntegrationTests
    {
        private const string BalancingUrl = "opc.tcp://balance.invalid:4840";

        private InMemorySharedKeyValueStore m_kv = null!;
        private LoadDirectionOptions m_options = null!;
        private ServerLoadDirector m_director = null!;
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_kv = new InMemorySharedKeyValueStore();
            m_options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            m_director = new ServerLoadDirector(
                new ConstantServiceLevelProvider(100), // local is a Degraded standby
                new ConstantLoadWeightProvider(200),
                m_options);

            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                GetEndpointsDirector = m_director
            });
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);

            IServiceMessageContext context = m_server.CurrentInstance.MessageContext;
            string localServerUri = m_server.CurrentInstance.ServerUris.ToArray()[0];

            var view = new SharedPeerDirectionView(
                m_kv, context, NullRecordProtector.Instance, m_options, TimeProvider.System);
            var policy = new BandedServerDirectionPolicy(view, m_options, _ => 0);
            var directory = new SharedPeerEndpointDirectory(m_kv, context, NullRecordProtector.Instance, m_options);
            var publisher = new SharedPeerEndpointPublisher(
                m_kv, context, NullRecordProtector.Instance, m_options, localServerUri);
            m_director.Configure(policy, directory, publisher, localServerUri);

            // Seed peer B: healthy, unloaded, with endpoints.
            var bDirection = new SharedPeerDirectionPublisher(
                m_kv, context, NullRecordProtector.Instance, m_options, TimeProvider.System, "urn:B");
            await bDirection.PublishServiceLevelAsync(255).ConfigureAwait(false);
            await bDirection.PublishLoadWeightAsync(0).ConfigureAwait(false);
            var bEndpoints = new SharedPeerEndpointPublisher(
                m_kv, context, NullRecordProtector.Instance, m_options, "urn:B");
            await bEndpoints.PublishAsync([MakeEndpoint("opc.tcp://b.example:4840", "urn:B")]).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            m_kv?.Dispose();
        }

        [Test]
        public async Task GetEndpointsOnBalancingUrlRedirectsToHealthierPeerAsync()
        {
            GetEndpointsResponse response = await m_server.GetEndpointsAsync(
                CreateChannelContext(), new RequestHeader(), BalancingUrl, default, default, RequestLifetime.None).ConfigureAwait(false);

            EndpointDescription[] endpoints = response.Endpoints.ToArray()!;
            Assert.That(endpoints, Has.Length.EqualTo(1));
            Assert.That(endpoints[0].Server?.ApplicationUri, Is.EqualTo("urn:B"));
        }

        [Test]
        public async Task GetEndpointsOnNormalUrlServesLocalServerAsync()
        {
            string normalUrl = m_server.GetEndpoints().ToArray()![0].EndpointUrl!;

            GetEndpointsResponse response = await m_server.GetEndpointsAsync(
                CreateChannelContext(), new RequestHeader(), normalUrl, default, default, RequestLifetime.None).ConfigureAwait(false);

            EndpointDescription[] endpoints = response.Endpoints.ToArray()!;
            Assert.That(endpoints, Is.Not.Empty, "a normal request returns the local server's own endpoints");
            Assert.That(
                endpoints.Any(e => string.Equals(e.Server?.ApplicationUri, "urn:B", StringComparison.Ordinal)),
                Is.False,
                "a normal request must not be redirected to a peer");
        }

        private SecureChannelContext CreateChannelContext()
        {
            EndpointDescription endpoint = m_server.GetEndpoints().ToArray()![0];
            return new SecureChannelContext("loaddir-test", endpoint, RequestEncoding.Binary, null, null, null);
        }

        private static EndpointDescription MakeEndpoint(string url, string serverUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    ApplicationType = ApplicationType.Server
                }
            };
        }
    }
}
