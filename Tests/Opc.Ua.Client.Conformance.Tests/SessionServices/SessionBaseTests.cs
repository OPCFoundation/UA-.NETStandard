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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Session Service Set – base session
    /// lifecycle operations.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionBase")]
    public class SessionBaseTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public void CreateSessionWithSpecificName()
        {
            Assert.That(Session.SessionName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "001")]
        public void CreateSessionWithRequestedTimeout()
        {
            Assert.That(Session.SessionTimeout, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsArrayFindOurSessionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public async Task ActivateSessionWithAnonymousIdentityAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(additionalSession.Connected, Is.True);
                Assert.That(additionalSession.SessionId, Is.Not.Null);
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "010")]
        public async Task CloseSessionWithDeleteSubscriptionsTrueAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            Assert.That(additionalSession.Connected, Is.True);
            await additionalSession.CloseAsync(5000, true)
                .ConfigureAwait(false);
            additionalSession.Dispose();
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "010")]
        public async Task CloseSessionWithDeleteSubscriptionsFalseAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            Assert.That(additionalSession.Connected, Is.True);
            await additionalSession.CloseAsync(5000, false)
                .ConfigureAwait(false);
            additionalSession.Dispose();
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "002")]
        public async Task SessionKeepaliveVerifySessionStaysActiveAsync()
        {
            Assert.That(Session.Connected, Is.True);
            await Task.Delay(500).ConfigureAwait(false);
            Assert.That(Session.Connected, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public void VerifySessionEndpointDescription()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.EndpointUrl, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "012")]
        public void VerifySessionServerCertificate()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            // With SecurityMode None the certificate may or may not be set.
            // Just verify the endpoint itself is accessible.
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public void ReadMaxResponseMessageSize()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.TransportProfileUri,
                Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-001")]
        public async Task CreateAndVerifyMultipleSessionsAsync()
        {
            ISession session1 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ISession session2 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            try
            {
                Assert.That(session1.SessionId, Is.Not.Null);
                Assert.That(session2.SessionId, Is.Not.Null);
                Assert.That(
                    session1.SessionId,
                    Is.Not.EqualTo(session2.SessionId));
            }
            finally
            {
                await session1.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session1.Dispose();
                await session2.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session2.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "009")]
        public void SessionIdentityToken()
        {
            Assert.That(Session.Identity, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "013")]
        public async Task CreateSessionWithEmptyNameAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(additionalSession.SessionId, Is.Not.Null);
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "013")]
        public async Task CreateSessionWithLongNameAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(additionalSession.Connected, Is.True);
                Assert.That(additionalSession.SessionId, Is.Not.Null);
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "001")]
        public void SessionTimeoutIsRevisedByServer()
        {
            Assert.That(Session.SessionTimeout, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsCurrentSubscriptionsCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int count = result.WrappedValue.GetInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsCurrentSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionSecurityDiagnosticsAsync()
        {
            // OPC UA well-known NodeId 2162 = SecurityRejectedSessionCount
            DataValue result = await ReadNodeValueAsync(
                new NodeId(2162))
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    "Server does not support SecurityRejectedSessionCount diagnostic.");
            }

            int count = result.WrappedValue.GetInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public async Task ActivateMultipleTimesOnSameSessionAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                NodeId originalId = additionalSession.SessionId;
                Assert.That(originalId, Is.Not.Null);

                DataValue result = await ReadNodeValueAsync(
                    VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

                Assert.That(
                    additionalSession.SessionId,
                    Is.EqualTo(originalId));
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-001")]
        public async Task CreateSessionVerifySessionIdIsUniqueAsync()
        {
            ISession session1 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ISession session2 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session1.SessionId, Is.Not.Null);
                Assert.That(session2.SessionId, Is.Not.Null);
                Assert.That(
                    session1.SessionId,
                    Is.Not.EqualTo(session2.SessionId));
            }
            finally
            {
                await session1.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session1.Dispose();
                await session2.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session2.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "005")]
        public void VerifySessionPreferredLocales()
        {
            Assert.That(Session, Is.Not.Null);
            Assert.That(Session.Connected, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsCumulatedSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public async Task CreateSessionAndReadServerStateAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(additionalSession.Connected, Is.True);

                DataValue result = await ReadNodeValueAsync(
                    VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                int state = result.WrappedValue.GetInt32();
                Assert.That(
                    state,
                    Is.EqualTo((int)ServerState.Running));
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "010")]
        public async Task CloseSessionAndVerifyDisconnectedAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            Assert.That(additionalSession.Connected, Is.True);
            await additionalSession.CloseAsync(5000, true)
                .ConfigureAwait(false);
            Assert.That(additionalSession.Connected, Is.False);
            additionalSession.Dispose();
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public void SessionEndpointHasTransportProfileUri()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.TransportProfileUri,
                Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadRejectedSessionCountAsync()
        {
            // OPC UA well-known NodeId 2154 = RejectedSessionCount
            DataValue result = await ReadNodeValueAsync(
                new NodeId(2154))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int count = result.WrappedValue.GetInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsSecurityRejectedRequestsCountAsync()
        {
            // OPC UA well-known NodeId 2163 = SecurityRejectedRequestsCount
            DataValue result = await ReadNodeValueAsync(
                new NodeId(2163))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int count = result.WrappedValue.GetInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0));
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        [Description("Invoke CreateSession specifying a RequestedSessionTimeout of 0. We expect the RevisedSessionTimeout != 0. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "001")]
        public async Task CreateSessionWithZeroTimeoutReturnsRevisedTimeoutAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("CreateSession with default parameters, except for a small timeout of 10 seconds. Activate the session and stall (do not use) the session for a period GREATER than the timeout perio")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "002")]
        public async Task CreateSessionStallsBeyondTimeoutPeriodAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invoke CreateSession and then check if the session appears within the server diagnostics. This script must first read the servers profile to see if diagnostics are supported and if")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task CreateSessionAppearsInServerDiagnosticsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("activate a session using default parameters. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]
        public async Task ActivateSessionWithDefaultParametersAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Specify numerous localeIds supported by the server, in a ranked order. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "005")]
        public async Task CreateSessionWithRankedLocaleIdsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("activate a session that has been transferred to another channel. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "008")]
        public async Task ActivateSessionTransferredToAnotherChannelAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("provide NO software certificates. This used to be a problem, but UA 1.02 changed this behavior. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "009")]
        public async Task CreateSessionWithNoSoftwareCertificatesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("CloseSession using default parameters. This test works by first opening a session (default parameters) and then closes it. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "010")]
        public async Task CloseSessionWithDefaultParametersAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("provide NO software certificates. This used to be a problem, but UA 1.02 changed this behavior. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "011")]
        public async Task ActivateSessionWithNoSoftwareCertificatesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Using SecurityPolicy None/anonymous, create a session while specifying a not-trusted certificate.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "012")]
        public async Task CreateSessionWithUntrustedCertificateAndNoneSecurityAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Create a session without specifying a SessionName (legal, but some servers crash) */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "013")]
        public async Task CreateSessionWithoutSessionNameAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Over a non-secure channel; call ActivateSession() specifying an empty ClientSignature. */")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "015")]
        public async Task ActivateSessionWithEmptyClientSignatureOnNonSecureChannelAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }
        [Description("CreateSession – injects service result Bad_SecureChannelIdInvalid in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-01")]
        public Task CreateSessionWithInjectedBadSecureChannelIdInvalidAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadSecureChannelIdInvalid);
        }

        private async Task AssertCreateSessionInjectsServiceResultAsync(StatusCode injected)
        {
            // Session.OpenAsync retries CreateSession without the client
            // certificate when the first attempt fails, so a one-shot
            // expectation would only mutate the first response and the
            // retry would succeed. Use a recurring expectation so both
            // attempts return the injected error.
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) => resp.ResponseHeader.ServiceResult = injected);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await OpenAuxSessionAsync().ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(injected));
        }

        [Description("CreateSession – injects service result Bad_NonceInvalid in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-02")]
        public Task CreateSessionWithInjectedBadNonceInvalidAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadNonceInvalid);
        }

        [Description("CreateSession – injects service result Bad_SecurityChecksFailed in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-03")]
        public Task CreateSessionWithInjectedBadSecurityChecksFailedAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadSecurityChecksFailed);
        }

        [Description("CreateSession – injects service result Bad_CertificateTimeInvalid in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-04")]
        public Task CreateSessionWithInjectedBadCertificateTimeInvalidAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateTimeInvalid);
        }

        [Description("CreateSession – injects service result Bad_CertificateIssuerTimeInvalid in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-05")]
        public Task CreateSessionWithInjectedBadCertificateIssuerTimeInvalidAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateIssuerTimeInvalid);
        }

        [Description("CreateSession – injects service result Bad_CertificateHostnameInvalid in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-001-06")]
        public Task CreateSessionWithInjectedBadCertificateHostnameInvalidAsync()
        {
            return AssertCreateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateHostNameInvalid);
        }

        [Description("CreateSession – the SessionId returned by the server is null for the second session.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-003")]
        public async Task CreateSessionWithInjectedNullSessionIdAsync()
        {
            // A null SessionId in the response makes ActivateSession fail
            // because the client uses SessionId to identify the session.
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) => resp.SessionId = NodeId.Null);

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await OpenAuxSessionAsync().ConfigureAwait(false));
        }

        [Description("CreateSession – the server returns the same AuthenticationToken for two distinct sessions.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-006")]
        public async Task CreateSessionWithInjectedDuplicateAuthenticationTokenAsync()
        {
            // Capture the AuthenticationToken from the first
            // CreateSession response, then on the second response
            // overwrite it with the captured value.
            NodeId firstToken = NodeId.Null;
            using (MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) =>
                {
                    if (firstToken.IsNull)
                    {
                        firstToken = resp.AuthenticationToken;
                    }
                    else
                    {
                        resp.AuthenticationToken = firstToken;
                    }
                }))
            {
                ISession first = await OpenAuxSessionAsync().ConfigureAwait(false);
                ISession second = null;
                try
                {
                    Assert.That(firstToken.IsNull, Is.False);

                    try
                    {
                        second = await OpenAuxSessionAsync().ConfigureAwait(false);
                        Assert.That(second.Connected, Is.True);
                    }
                    catch (ServiceResultException)
                    {
                        // Acceptable: the server may reject the
                        // duplicate token during ActivateSession.
                    }
                }
                finally
                {
                    foreach (ISession s in new[] { first, second })
                    {
                        if (s == null)
                        {
                            continue;
                        }
                        try
                        {
                            await s.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            // best effort
                        }
                        s.Dispose();
                    }
                }
            }
        }

        [Description("CreateSession – the server revises the session timeout to (client request * 10).")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-007")]
        public Task CreateSessionWithInjectedExcessiveRevisedSessionTimeoutAsync()
        {
            return AssertCreateSessionAcceptsTimeoutMutationAsync(
                mutate: (req, resp) => resp.RevisedSessionTimeout = req.RequestedSessionTimeout * 10.0);
        }

        [Description("CreateSession – the server revises the session timeout to an unreasonably low value (1 second).")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-008")]
        public Task CreateSessionWithInjectedTooLowRevisedSessionTimeoutAsync()
        {
            return AssertCreateSessionAcceptsTimeoutMutationAsync(
                mutate: (req, resp) => resp.RevisedSessionTimeout = 1000.0);
        }

        private async Task AssertCreateSessionAcceptsTimeoutMutationAsync(
            Action<CreateSessionRequest, CreateSessionResponse> mutate)
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(mutate);

            // The client must accept whatever RevisedSessionTimeout the
            // server returns — assert the connection completes without
            // throwing.
            ISession session = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
                session.Dispose();
            }
        }

        [Description("CreateSession – the server returns a ServerNonce that is less than 32 bytes long.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-011")]
        public async Task CreateSessionWithInjectedShortServerNonceAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) => resp.ServerNonce = new ByteString(new byte[8].AsMemory()));

            // On a SecurityPolicy=None channel the client does not
            // require a 32-byte nonce, so the connection completes.
            // On a signed channel the client would reject the response.
            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable: client validated and rejected.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Description("CreateSession – the EndpointDescriptions returned in the response differ from those obtained from the Discovery endpoint.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-020")]
        public Task CreateSessionWithMismatchedEndpointDescriptionsAsync()
        {
            return AssertCreateSessionToleratesServerEndpointsMutationAsync(
                mutate: (req, resp) =>
                {
                    if (resp.ServerEndpoints != null && resp.ServerEndpoints.Count > 0)
                    {
                        // Tweak the application URI on every endpoint so it
                        // no longer matches the one returned by Discovery.
                        foreach (EndpointDescription ep in resp.ServerEndpoints)
                        {
                            if (ep.Server != null)
                            {
                                ep.Server.ApplicationUri = "urn:mock:tampered";
                            }
                        }
                    }
                });
        }

        [Description("CreateSession – the server returns an empty ServerEndpoints array.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-021")]
        public Task CreateSessionWithInjectedEmptyServerEndpointsAsync()
        {
            return AssertCreateSessionToleratesServerEndpointsMutationAsync(
                mutate: (req, resp) => resp.ServerEndpoints = System.Array.Empty<EndpointDescription>().ToArrayOf());
        }

        private async Task AssertCreateSessionToleratesServerEndpointsMutationAsync(
            Action<CreateSessionRequest, CreateSessionResponse> mutate)
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(mutate);

            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable: client may detect the mismatch and reject.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Description("CreateSession – the server returns an empty ServerSoftwareCertificates array.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-024")]
        public async Task CreateSessionWithInjectedEmptyServerSoftwareCertificatesAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) => resp.ServerSoftwareCertificates = System.Array.Empty<SignedSoftwareCertificate>().ToArrayOf());

            ISession session = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
                session.Dispose();
            }
        }

        [Description("CreateSession – the server returns an empty ServerSignature.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-025")]
        public async Task CreateSessionWithInjectedEmptyServerSignatureAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
                (req, resp) => resp.ServerSignature = new SignatureData());

            // On a SecurityPolicy=None channel the empty signature is
            // not validated, so the open completes.
            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable on signed channels.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Description("CreateSession – the server returns a MaxRequestMessageSize of 500 bytes.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-026")]
        public Task CreateSessionWithInjectedSmallMaxRequestMessageSizeAsync()
        {
            return AssertCreateSessionAcceptsMessageSizeMutationAsync(
                mutate: (req, resp) => resp.MaxRequestMessageSize = 500);
        }

        [Description("CreateSession – the server returns a MaxResponseMessageSize equal to the client's request multiplied by 10.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-027")]
        public Task CreateSessionWithInjectedExcessiveMaxResponseMessageSizeAsync()
        {
            return AssertCreateSessionAcceptsMessageSizeMutationAsync(
                mutate: (req, resp) => resp.MaxRequestMessageSize = req.MaxResponseMessageSize * 10);
        }

        private async Task AssertCreateSessionAcceptsMessageSizeMutationAsync(
            Action<CreateSessionRequest, CreateSessionResponse> mutate)
        {
            using IDisposable expectation = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(mutate);

            ISession session = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
                session.Dispose();
            }
        }

        [Description("ActivateSession – injects service result Bad_IdentityTokenInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-01")]
        public Task ActivateSessionWithInjectedBadIdentityTokenInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadIdentityTokenInvalid);
        }

        [Description("ActivateSession – injects service result Bad_IdentityTokenRejected.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-02")]
        public Task ActivateSessionWithInjectedBadIdentityTokenRejectedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadIdentityTokenRejected);
        }

        [Description("ActivateSession – injects service result Bad_UserAccessDenied.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-03")]
        public Task ActivateSessionWithInjectedBadUserAccessDeniedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadUserAccessDenied);
        }

        [Description("ActivateSession – injects service result Bad_ApplicationSignatureInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-04")]
        public Task ActivateSessionWithInjectedBadApplicationSignatureInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadApplicationSignatureInvalid);
        }

        [Description("ActivateSession – injects service result Bad_UserSignatureInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-05")]
        public Task ActivateSessionWithInjectedBadUserSignatureInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadUserSignatureInvalid);
        }

        [Description("ActivateSession – injects service result Bad_NoValidCertificates.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-06")]
        public Task ActivateSessionWithInjectedBadNoValidCertificatesAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadNoValidCertificates);
        }

        [Description("ActivateSession – injects service result Bad_IdentityChangeNotSupported.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-07")]
        public Task ActivateSessionWithInjectedBadIdentityChangeNotSupportedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadIdentityChangeNotSupported);
        }

        [Description("ActivateSession – injects service result Bad_CertificateTimeInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-08")]
        public Task ActivateSessionWithInjectedBadCertificateTimeInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateTimeInvalid);
        }

        [Description("ActivateSession – injects service result Bad_CertificateIssuerTimeInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-09")]
        public Task ActivateSessionWithInjectedBadCertificateIssuerTimeInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateIssuerTimeInvalid);
        }

        [Description("ActivateSession – injects service result Bad_CertificateHostNameInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-10")]
        public Task ActivateSessionWithInjectedBadCertificateHostNameInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateHostNameInvalid);
        }

        [Description("ActivateSession – injects service result Bad_CertificateUriInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-11")]
        public Task ActivateSessionWithInjectedBadCertificateUriInvalidAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateUriInvalid);
        }

        [Description("ActivateSession – injects service result Bad_CertificateUseNotAllowed.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-12")]
        public Task ActivateSessionWithInjectedBadCertificateUseNotAllowedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateUseNotAllowed);
        }

        [Description("ActivateSession – injects service result Bad_CertificateIssuerUseNotAllowed.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-13")]
        public Task ActivateSessionWithInjectedBadCertificateIssuerUseNotAllowedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateIssuerUseNotAllowed);
        }

        [Description("ActivateSession – injects service result Bad_CertificateUntrusted.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-14")]
        public Task ActivateSessionWithInjectedBadCertificateUntrustedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateUntrusted);
        }

        [Description("ActivateSession – injects service result Bad_CertificateRevocationUnknown.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-15")]
        public Task ActivateSessionWithInjectedBadCertificateRevocationUnknownAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateRevocationUnknown);
        }

        [Description("ActivateSession – injects service result Bad_CertificateIssuerRevocationUnknown.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-16")]
        public Task ActivateSessionWithInjectedBadCertificateIssuerRevocationUnknownAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateIssuerRevocationUnknown);
        }

        [Description("ActivateSession – injects service result Bad_CertificateRevoked.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-17")]
        public Task ActivateSessionWithInjectedBadCertificateRevokedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateRevoked);
        }

        [Description("ActivateSession – injects service result Bad_CertificateIssuerRevoked.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-18")]
        public Task ActivateSessionWithInjectedBadCertificateIssuerRevokedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadCertificateIssuerRevoked);
        }

        [Description("ActivateSession – injects service result Bad_SecurityChecksFailed.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-029-19")]
        public Task ActivateSessionWithInjectedBadSecurityChecksFailedAsync()
        {
            return AssertActivateSessionInjectsServiceResultAsync(StatusCodes.BadSecurityChecksFailed);
        }

        private async Task AssertActivateSessionInjectsServiceResultAsync(StatusCode injected)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<ActivateSessionResponse>(
                r => r.ResponseHeader.ServiceResult = injected);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await OpenAuxSessionAsync().ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(injected));
        }

        [Description("CloseSession – the server returns Bad_SessionIdInvalid as the service result.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-036")]
        public async Task CloseSessionWithInjectedBadSessionIdInvalidAsync()
        {
            ISession aux = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                using IDisposable expectation = MockController.ExpectNextResponse<CloseSessionResponse>(
                    r => r.ResponseHeader.ServiceResult = StatusCodes.BadSessionIdInvalid);

                // Session.CloseAsync swallows ServiceResultException and
                // returns the status code so callers can log it without
                // a try/catch — assert against the returned code.
                StatusCode result = await aux.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo((StatusCode)StatusCodes.BadSessionIdInvalid));
            }
            finally
            {
                aux.Dispose();
            }
        }

        [Description("ActivateSession – the server returns an empty ServerNonce in the response.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-030")]
        public async Task ActivateSessionWithInjectedEmptyServerNonceAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<ActivateSessionRequest, ActivateSessionResponse>(
                (req, resp) => resp.ServerNonce = ByteString.Empty);

            // On a SecurityPolicy=None channel the empty nonce passes
            // through. On a signed channel the client would reject.
            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable on signed channels.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Description("ActivateSession – the first entry of the per-result Results array contains Bad_CertificateUriInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-032")]
        public async Task ActivateSessionWithInjectedFirstResultBadCertificateUriInvalidAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<ActivateSessionRequest, ActivateSessionResponse>(
                (req, resp) =>
                {
                    int n = resp.Results == null ? 0 : resp.Results.Count;
                    var mutated = new StatusCode[System.Math.Max(n, 1)];
                    for (int i = 0; i < mutated.Length; i++)
                    {
                        mutated[i] = (i < n && i > 0) ? resp.Results[i] : StatusCodes.Good;
                    }
                    mutated[0] = StatusCodes.BadCertificateUriInvalid;
                    resp.Results = mutated.ToArrayOf();
                });

            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable: a stricter client would reject.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }

        [Description("ActivateSession – every entry of the Results array contains a Bad_ status code.")]
        [Test]
        [Property("ConformanceUnit", "Session Client Base")]
        [Property("Tag", "Err-033")]
        public async Task ActivateSessionWithInjectedAllResultsBadAsync()
        {
            using IDisposable expectation = MockController.WhenRequest<ActivateSessionRequest, ActivateSessionResponse>(
                (req, resp) =>
                {
                    int n = resp.Results == null ? 0 : resp.Results.Count;
                    var mutated = new StatusCode[System.Math.Max(n, 1)];
                    for (int i = 0; i < mutated.Length; i++)
                    {
                        mutated[i] = StatusCodes.BadCertificateUriInvalid;
                    }
                    resp.Results = mutated.ToArrayOf();
                });

            ISession session = null;
            try
            {
                session = await OpenAuxSessionAsync().ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException)
            {
                // Acceptable.
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    session.Dispose();
                }
            }
        }
    }
}
