/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// The discovery endpoints an endpoint was selected from are compared
    /// with the endpoints the server returns in CreateSession.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [NonParallelizable]
    public sealed class SessionSelectionEndpointsTests : ClientTestFramework
    {
        public SessionSelectionEndpointsTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SingleSession = false;
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// An endpoint selected through discovery keeps the discovery
        /// endpoints, and they match the endpoints of CreateSession.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task SelectedEndpointKeepsItsDiscoveryEndpointsAsync(bool updateBeforeConnect, bool clone)
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ArrayOf<EndpointDescription> honest = await ClientFixture
                .GetEndpointsAsync(ServerUrl).ConfigureAwait(false);

            EndpointDescription? selected = await CoreClientUtils.SelectEndpointAsync(
                config, ServerUrl.ToString(), useSecurity: true, Telemetry).ConfigureAwait(false);
            Assert.That(selected, Is.Not.Null);

            // an application may copy the description before it connects.
            var configured = new ConfiguredEndpoint(
                null, clone ? CoreUtils.Clone(selected)! : selected!, EndpointConfiguration.Create(config));
            Assert.That(configured.DiscoveryEndpoints.Count, Is.EqualTo(honest.Count));

            ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                config, configured, updateBeforeConnect, false, "selection-endpoints",
                60000, new UserIdentity(), default).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(selected!.SecurityMode));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// When the endpoints the selection was made from differ from the
        /// endpoints of the server, the session is not created: either the
        /// discovery update before connect or CreateSession reports
        /// BadSecurityChecksFailed.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task EndpointsThatDifferFromTheSelectionAreRefusedAsync(bool updateBeforeConnect, bool clone)
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ArrayOf<EndpointDescription> honest = await ClientFixture
                .GetEndpointsAsync(ServerUrl).ConfigureAwait(false);

            // a discovery reply without the SignAndEncrypt endpoints.
            ArrayOf<EndpointDescription> partial = honest.ToArray()!
                .Where(e => e.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                .Select(e => CoreUtils.Clone(e)!)
                .ToArray();
            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                config, ServerUrl, partial, useSecurity: true, Telemetry);
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));

            // what SelectEndpointAsync records when discovery returned that reply.
            MethodInfo setSelection = typeof(ConfiguredEndpoint).GetMethod(
                "SetSelectionEndpoints",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.That(setSelection, Is.Not.Null);
            setSelection.Invoke(null, [selected, partial]);

            var configured = new ConfiguredEndpoint(
                null, clone ? CoreUtils.Clone(selected)! : selected, EndpointConfiguration.Create(config));

            ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                    config, configured, updateBeforeConnect, false, "selection-mismatch",
                    60000, new UserIdentity(), default).ConfigureAwait(false);
                session.Dispose();
            }))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// Only the endpoints with the transport profile of the current channel are
        /// compared (OPC 10000-4 §5.7.2.2). A server whose endpoints on another
        /// transport differ from its opc.tcp endpoints, here an https endpoint that
        /// only discovery lists, still connects over opc.tcp.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EndpointsOfOtherTransportsAreNotComparedAsync(bool updateBeforeConnect)
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ArrayOf<EndpointDescription> honest = await ClientFixture
                .GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            Assert.That(honest.ToArray()!.All(e => e!.TransportProfileUri == Profiles.UaTcpTransport), Is.True);

            // discovery also lists an https endpoint, described differently.
            EndpointDescription https = CoreUtils.Clone(honest[0])!;
            https.EndpointUrl = "https://localhost:62541/";
            https.TransportProfileUri = Profiles.HttpsBinaryTransport;
            https.SecurityLevel = 200;
            https.UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "https-anon" }];
            ArrayOf<EndpointDescription> mixed = [.. honest.ToArray()!.Select(e => CoreUtils.Clone(e)!), https];

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                config, ServerUrl, mixed, useSecurity: true, Telemetry);
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
            SetSelectionEndpoints(selected, mixed);

            var configured = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(config));
            ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                config, configured, updateBeforeConnect, false, "selection-other-transport",
                60000, new UserIdentity(), default).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// <see cref="ClientConfiguration.VerifyServerEndpointsWithDiscovery"/> turns
        /// the comparison off: a client with configured security options ignores the
        /// list (OPC 10000-4 §5.7.2.2), also when it differs from the selection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EndpointComparisonCanBeTurnedOffAsync(bool updateBeforeConnect)
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ArrayOf<EndpointDescription> honest = await ClientFixture
                .GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ArrayOf<EndpointDescription> partial = honest.ToArray()!
                .Where(e => e.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                .Select(e => CoreUtils.Clone(e)!)
                .ToArray();
            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                config, ServerUrl, partial, useSecurity: true, Telemetry);
            Assert.That(selected, Is.Not.Null);
            SetSelectionEndpoints(selected!, partial);
            var configured = new ConfiguredEndpoint(null, selected!, EndpointConfiguration.Create(config));

            ClientConfiguration clientConfiguration = config.ClientConfiguration!;
            bool verify = clientConfiguration.VerifyServerEndpointsWithDiscovery;
            clientConfiguration.VerifyServerEndpointsWithDiscovery = false;
            try
            {
                ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                    config, configured, updateBeforeConnect, false, "selection-not-verified",
                    60000, new UserIdentity(), default).ConfigureAwait(false);
                try
                {
                    Assert.That(session.Connected, Is.True);
                    Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
                }
                finally
                {
                    await session.CloseAsync().ConfigureAwait(false);
                    session.Dispose();
                }
            }
            finally
            {
                clientConfiguration.VerifyServerEndpointsWithDiscovery = verify;
            }
        }

        private static void SetSelectionEndpoints(EndpointDescription selected, ArrayOf<EndpointDescription> endpoints)
        {
            MethodInfo setSelection = typeof(ConfiguredEndpoint).GetMethod(
                "SetSelectionEndpoints",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.That(setSelection, Is.Not.Null);
            setSelection.Invoke(null, [selected, endpoints]);
        }

        /// <summary>
        /// A selection list whose user token policies differ from the server's
        /// is refused as well, also when the channel security options match.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UserTokenPoliciesThatDifferFromTheSelectionAreRefusedAsync(bool updateBeforeConnect)
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ArrayOf<EndpointDescription> honest = await ClientFixture
                .GetEndpointsAsync(ServerUrl).ConfigureAwait(false);

            // the same endpoints, each with one user token policy more.
            EndpointDescription[] changed = honest.ToArray()!
                .Select(e => CoreUtils.Clone(e)!)
                .ToArray();
            foreach (EndpointDescription endpoint in changed)
            {
                endpoint.UserIdentityTokens =
                [
                    .. endpoint.UserIdentityTokens.ToArray()!,
                    new UserTokenPolicy(UserTokenType.UserName)
                    {
                        PolicyId = "not-offered",
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ];
            }
            ArrayOf<EndpointDescription> partial = changed;
            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                config, ServerUrl, partial, useSecurity: true, Telemetry);
            Assert.That(selected, Is.Not.Null);

            typeof(ConfiguredEndpoint).GetMethod(
                "SetSelectionEndpoints",
                BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [selected, partial]);

            var configured = new ConfiguredEndpoint(
                null, selected!, EndpointConfiguration.Create(config));

            ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                    config, configured, updateBeforeConnect, false, "selection-token-mismatch",
                    60000, new UserIdentity(), default).ConfigureAwait(false);
                session.Dispose();
            }))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }
    }
}
