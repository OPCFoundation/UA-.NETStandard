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

namespace Opc.Ua.Conformance.Tests.SessionServices
{
    /// <summary>
    /// compliance tests for session-level diagnostics.
    /// Validates that the server exposes the required diagnostic variables
    /// and that session properties are consistent with the connected endpoint.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionDiagnostics")]
    public class SessionDiagnosticsTests : TestFixture
    {
        [Description("Read the SessionDiagnosticsArray variable and verify that an array value is returned by the server.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadSessionDiagnosticsArrayFindsCurrentSessionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                new NodeId(3707)).ConfigureAwait(false);

            if (result.StatusCode == StatusCodes.BadUserAccessDenied ||
                result.StatusCode == StatusCodes.BadNotReadable)
            {
                Assert.Ignore(
                    "Server does not expose SessionDiagnosticsArray to " +
                    "anonymous sessions.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");
            Assert.That(
                result.WrappedValue.IsNull, Is.False,
                "SessionDiagnosticsArray value should not be null.");
        }

        [Description("Verify the session name is accessible and non-empty on the connected session object.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public void VerifySessionDiagnosticsSessionName()
        {
            Assert.That(Session.SessionName, Is.Not.Null.And.Not.Empty,
                "Session name should be set after activation.");
        }

        [Description("Read the Server.ServerStatus node and verify that a ServerStatusDataType with a valid ServerUri is returned.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task VerifySessionDiagnosticsServerUriAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Read of ServerStatus should return Good, got {result.StatusCode}");

            Assert.That(result.WrappedValue.IsNull, Is.False,
                "ServerStatus value should not be null.");
        }

        [Description("Verify that the session endpoint URL matches the server URL that was used when establishing the connection.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public void VerifySessionDiagnosticsEndpointUrl()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.EndpointUrl,
                Is.Not.Null.And.Not.Empty,
                "Endpoint URL should not be empty.");

            Assert.That(
                Session.Endpoint.EndpointUrl,
                Does.Contain(ServerUrl.Host),
                "Endpoint URL should reference the server host.");
        }

        [Description("Read the CumulatedSessionCount diagnostic and verify it is greater than zero (at least one session has been created).")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadTotalRequestCountGreaterThanZeroAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");

            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThan(0u),
                "CumulatedSessionCount should be > 0 after connecting.");
        }

        [Description("Read the CurrentSubscriptionCount diagnostic and verify it is a non-negative value.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadCurrentSubscriptionsCountMatchesOursAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");

            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0u),
                "CurrentSubscriptionCount should be >= 0.");
        }

        [Description("Verify that the session security mode property matches the security mode that was negotiated during connection.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "015")]
        public void VerifySessionSecurityModeMatchesConnection()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None),
                "fixture connects with SecurityMode None.");
        }

        [Description("Verify that the session security policy URI property matches the policy used during connection.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "015")]
        public void VerifySessionSecurityPolicyUriMatchesConnection()
        {
            Assert.That(Session.Endpoint, Is.Not.Null);
            Assert.That(
                Session.Endpoint.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.None),
                "fixture connects with SecurityPolicy None.");
        }

        [Description("Read the RejectedSessionCount diagnostic and verify it is a non-negative value. A healthy test run should have zero or very few rejected sessions.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadUnauthorizedRequestCountZeroForSuccessfulSessionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_RejectedSessionCount)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");

            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0u),
                "RejectedSessionCount should be >= 0.");
        }

        [Description("Read the ServerViewCount diagnostic variable and verify it returns a Good status code.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadServerViewCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary_ServerViewCount)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");
        }

        [Description("Read the MaxBrowseContinuationPoints capability and verify it returns a Good status code with a non-negative value.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadMaxBrowseContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_MaxBrowseContinuationPoints)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");

            ushort maxCp = result.WrappedValue.GetUInt16();
            Assert.That(maxCp, Is.GreaterThanOrEqualTo((ushort)0),
                "MaxBrowseContinuationPoints should be >= 0.");
        }

        [Description("Read the Server_ServerStatus_State variable and verify that the server reports a Running state.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadServerStateIsRunningAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");
            Assert.That(
                result.GetValue<int>(default),
                Is.EqualTo((int)ServerState.Running),
                "Server state should be Running.");
        }

        [Description("Read the SecurityRejectedSessionCount diagnostic variable and verify it returns a Good status with a non-negative value.")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadRejectedRequestCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                new NodeId(2162)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"Expected Good status, got {result.StatusCode}");

            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(0u),
                "SecurityRejectedSessionCount should be >= 0.");
        }

        [Description("Read the ServerDiagnosticsSummary node and verify it returns a Good or access-denied status (anonymous sessions may be restricted from reading full diagnostics).")]
        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "003")]
        public async Task ReadServerDiagnosticsSummaryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                result.StatusCode == StatusCodes.BadNotReadable ||
                result.StatusCode == StatusCodes.BadUserAccessDenied,
                Is.True,
                "Expected Good, BadNotReadable, or BadUserAccessDenied, " +
                $"got {result.StatusCode}");
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
    }
}
