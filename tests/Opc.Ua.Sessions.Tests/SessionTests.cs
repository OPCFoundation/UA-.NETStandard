/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// compliance tests for Session Service Set.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Session")]
    public class SessionTests : TestFixture
    {
        [Description("Verify the shared session is connected.")]
        [Test]
        public void Session001VerifySessionConnected()
        {
            Assert.That(Session.Connected, Is.True, "Session should be connected.");
            Assert.That(Session.SessionId, Is.Not.Null, "SessionId should not be null.");
        }

        [Description("Read ServerState via session and verify it is Running.")]
        [Test]
        public async Task Session002ReadServerStatusAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of ServerState should return Good.");

            int stateValue = response.Results[0].GetValue(0);
            Assert.That(stateValue, Is.EqualTo((int)ServerState.Running),
                "Server should be in Running state.");
        }

        [Description("Session.SessionId should not be NodeId.Null.")]
        [Test]
        public void Session003SessionId()
        {
            Assert.That(Session.SessionId, Is.Not.EqualTo(NodeId.Null),
                "SessionId should not be NodeId.Null.");
        }

        [Description("Session.SessionName should be set.")]
        [Test]
        public void Session004SessionName()
        {
            Assert.That(Session.SessionName, Is.Not.Null.And.Not.Empty,
                "SessionName should be set by the client fixture.");
        }

        [Description("Create an additional session, verify it connects, then close it.")]
        [Test]
        public async Task Session005CreateAndCloseAdditionalSessionAsync()
        {
            ISession? additionalSession = null;
            try
            {
                additionalSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);

                Assert.That(additionalSession, Is.Not.Null);
                Assert.That(additionalSession.Connected, Is.True,
                    "Additional session should be connected.");
            }
            finally
            {
                if (additionalSession != null)
                {
                    await additionalSession.CloseAsync(5000, true).ConfigureAwait(false);
                    additionalSession.Dispose();
                }
            }
        }

        [Description("Create 3 parallel sessions, verify all work, then close them.")]
        [Test]
        public async Task Session006MultipleParallelSessionsAsync()
        {
            const int sessionCount = 3;
            var sessions = new ISession[sessionCount];
            try
            {
                for (int i = 0; i < sessionCount; i++)
                {
                    sessions[i] = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.None)
                        .ConfigureAwait(false);
                }

                foreach (ISession s in sessions)
                {
                    Assert.That(s.Connected, Is.True);

                    // Verify each session can read
                    ReadResponse response = await s.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Both,
                        new ReadValueId[]
                        {
                            new() {
                                NodeId = VariableIds.Server_ServerStatus_State,
                                AttributeId = Attributes.Value
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
                }
            }
            finally
            {
                foreach (ISession s in sessions)
                {
                    if (s != null)
                    {
                        await s.CloseAsync(5000, true).ConfigureAwait(false);
                        s.Dispose();
                    }
                }
            }
        }

        [Description("Session timeout should be a positive value.")]
        [Test]
        public void Session007SessionTimeout()
        {
            Assert.That(Session.SessionTimeout, Is.GreaterThan(0),
                "SessionTimeout should be positive.");
        }

        [Description("Server application URI should be set.")]
        [Test]
        public void Session008ServerUri()
        {
            string applicationUri = Session.Endpoint.Server.ApplicationUri!;
            Assert.That(applicationUri, Is.Not.Null.And.Not.Empty,
                "Server ApplicationUri should be set.");
        }

        [Description("NamespaceUris should have at least 2 entries.")]
        [Test]
        public void Session009NamespaceUris()
        {
            Assert.That(Session.NamespaceUris.Count, Is.GreaterThanOrEqualTo(2),
                "NamespaceUris should contain at least ns0 (OPC UA) and ns1 (server).");
        }

        [Description("Read MaxNodesPerRead from OperationLimits.")]
        [Test]
        public async Task Session010ReadOperationLimitsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of MaxNodesPerRead should return Good.");

            uint maxNodes = response.Results[0].GetValue<uint>(0);
            Assert.That(maxNodes, Is.GreaterThan((uint)0),
                "MaxNodesPerRead should be a positive value.");
        }

        [Description("Endpoint URL should contain the expected server port.")]
        [Test]
        public void Session011VerifyEndpointUrl()
        {
            string endpointUrl = Session.Endpoint.EndpointUrl!;
            Assert.That(endpointUrl, Is.Not.Null.And.Not.Empty);
            Assert.That(endpointUrl,
                Does.Contain(ServerUrl.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "Endpoint URL should contain the expected server port.");
        }

        [Description("Read ServerDiagnostics EnabledFlag.")]
        [Test]
        public async Task Session012ServerDiagnosticsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerDiagnostics_EnabledFlag,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of EnabledFlag should return Good.");
            bool enabledFlag = response.Results[0].GetValue(false);
            Assert.That(enabledFlag, Is.InstanceOf<bool>(),
                "EnabledFlag should be a boolean value.");
        }
    }
}
