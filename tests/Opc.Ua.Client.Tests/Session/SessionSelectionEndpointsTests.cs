/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
