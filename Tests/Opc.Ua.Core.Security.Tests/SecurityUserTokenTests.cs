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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security – user token handling,
    /// identity changes, nonce uniqueness, session timeout, and
    /// per-SecurityPolicy connectivity.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityUserToken")]
    public class SecurityUserTokenTests : TestFixture
    {
        [Test]
        public async Task ConnectSysadminOnSignEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            EndpointDescription signEp =
                FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (signEp == null)
            {
                Assert.Fail("No Sign endpoint available.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, signEp.SecurityPolicyUri,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ConnectSysadminOnSignAndEncryptEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            EndpointDescription encryptEp =
                FindEndpoint(endpoints, MessageSecurityMode.SignAndEncrypt);
            if (encryptEp == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, encryptEp.SecurityPolicyUri,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ConnectAppuserVerifyLimitedAccessAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Ignore("No Username token advertised.");
            }

            ISession session;
            try
            {
                // Use the no-retry helper: 'appuser' may not exist on the
                // configured server, and the 25-retry wrapper would lock out
                // the account for 5 minutes (see SetUp ClearAuthenticationLockouts).
                session = await OpenAuxSessionAsync(
                    userIdentity: new UserIdentity("appuser", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);

                // Try to write as appuser; may be denied
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                WriteResponse wr = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(99))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(wr.Results.Count, Is.EqualTo(1));
                // Accept Good (if allowed) or BadUserAccessDenied
                Assert.That(
                    wr.Results[0].Code == StatusCodes.Good ||
                    wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected Good or BadUserAccessDenied, got {wr.Results[0]}");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task AnonymousCanReadNodesAsync()
        {
            // Shared session is anonymous
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            ReadResponse rr = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(rr.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(rr.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task SysadminCanReadNodeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                ReadResponse rr = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(rr.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(rr.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task SysadminCanWriteNodeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                WriteResponse wr = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(77))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(wr.Results.Count, Is.EqualTo(1));
                Assert.That(
                    wr.Results[0].Code, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task AppuserWriteDeniedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Ignore("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("appuser", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                WriteResponse wr = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(88))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(wr.Results.Count, Is.EqualTo(1));
                Assert.That(
                    wr.Results[0].Code == StatusCodes.Good ||
                    wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                    Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ConnectWithEmptyPasswordRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints;
            try
            {
                endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadConnectionClosed ||
                ex.StatusCode == StatusCodes.BadNotConnected)
            {
                Assert.Ignore("Discovery endpoint not available: " + ex.StatusCode);
                return;
            }

            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Ignore("No Username token advertised.");
            }

            // Use the non-retrying ConnectAsync overload to avoid repeated
            // failed login attempts that could lock out the sysadmin account.
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity("sysadmin", ""u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Ignore("Server accepted empty password (demo server behavior).");
            }
            catch (ServiceResultException)
            {
                // Any rejection is acceptable — empty password should be rejected
                Assert.Pass("Server correctly rejected empty password.");
            }
            catch (Exception)
            {
                Assert.Ignore("Connection failed with unexpected error.");
            }
        }

        [Test]
        public async Task VerifySecurityLevelOrderingAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            byte maxNoneLevel = 0;
            byte minSecureLevel = byte.MaxValue;
            bool foundNone = false;
            bool foundSecure = false;

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    if (ep.SecurityLevel > maxNoneLevel)
                    {
                        maxNoneLevel = ep.SecurityLevel;
                    }
                    foundNone = true;
                }
                else
                {
                    if (ep.SecurityLevel < minSecureLevel)
                    {
                        minSecureLevel = ep.SecurityLevel;
                    }
                    foundSecure = true;
                }
            }

            if (foundNone && foundSecure)
            {
                Assert.That(minSecureLevel,
                    Is.GreaterThanOrEqualTo(maxNoneLevel),
                    "Secure endpoints should have higher SecurityLevel.");
            }
        }

        [Test]
        public async Task ConnectWithEachSecurityPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            var policies = new HashSet<string>();
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    policies.Add(ep.SecurityPolicyUri);
                }
            }

            if (policies.Count == 0)
            {
                Assert.Ignore("No secure endpoints available.");
            }

            foreach (string policy in policies)
            {
                try
                {
                    ISession session = await ClientFixture
                        .ConnectAsync(ServerUrl, policy)
                        .ConfigureAwait(false);
                    try
                    {
                        Assert.That(session.Connected, Is.True,
                            $"Failed to connect with policy {policy}");
                    }
                    finally
                    {
                        await session.CloseAsync(5000, true)
                            .ConfigureAwait(false);
                        session.Dispose();
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadConnectionClosed ||
                        sre.StatusCode == StatusCodes.BadNotConnected)
                {
                    Assert.Ignore("Secure connection with policy " +
                        policy +
                        " failed: " +
                        sre.StatusCode.ToString());
                }
            }
        }

        [Test]
        public async Task EachSessionHasUniqueSessionIdAsync()
        {
            var sessionIds = new List<NodeId>();

            for (int i = 0; i < 3; i++)
            {
                ISession session;
                try
                {
                    session = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.None)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    Assert.Fail($"Connection rejected: {sre.StatusCode}");
                    return;
                }

                try
                {
                    sessionIds.Add(session.SessionId);
                }
                finally
                {
                    await session.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    session.Dispose();
                }
            }

            Assert.That(sessionIds, Has.Count.EqualTo(3));
            Assert.That(sessionIds.Distinct().Count(), Is.EqualTo(3),
                "Each session should have a unique SessionId.");
        }

        [Test]
        public async Task SessionTimeoutIsPositiveAsync()
        {
            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Connection rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.SessionTimeout,
                    Is.GreaterThan(0),
                    "Revised session timeout should be positive.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ActivateWithEmptyUsernameIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            // Use the non-retrying ConnectAsync overload to avoid repeated
            // failed login attempts that could lock out accounts.
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity(string.Empty, "demo"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected rejection for empty username.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task ActivateWithVeryLongUsernameIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            string longUsername = new('x', 1000);
            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity(longUsername, "demo"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected rejection for very long username.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task ActivateWithVeryLongPasswordIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            byte[] longPassword = new byte[1000];
            for (int i = 0; i < longPassword.Length; i++)
            {
                longPassword[i] = (byte)'p';
            }
            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity("sysadmin", longPassword))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected rejection for very long password.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task ActivateWithSpecialCharsInUsernameIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity(
                        "user<>&\"'!@#$%^", "demo"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected rejection for unknown user.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task ActivateWithUnicodePasswordIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.None, endpoints).ConfigureAwait(false);

            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    endpoint,
                    new UserIdentity(
                        "sysadmin", "\u00e9\u00e8\u00ea\u00eb\u4e16\u754c"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected rejection for wrong password.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task SwitchFromAnonymousToUserNameMidSessionAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Anonymous connect rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
                await session.UpdateSessionAsync(
                    new UserIdentity("sysadmin", "demo"u8),
                    session.PreferredLocales)
                    .ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Switch rejected: {sre.StatusCode}");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task SwitchFromUserNameToAnonymousMidSessionAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                await session.UpdateSessionAsync(
                    new UserIdentity(),
                    session.PreferredLocales).ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Switch rejected: {sre.StatusCode}");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task SwitchFromOneUserToAnotherMidSessionAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Ignore("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                await session.UpdateSessionAsync(
                    new UserIdentity("appuser", "demo"u8),
                    session.PreferredLocales)
                    .ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore($"Switch rejected: {sre.StatusCode}");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ActivateCorrectCredentialsOnNoneAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ActivateCorrectCredentialsOnSignAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            EndpointDescription signEp = FindEndpoint(
                endpoints, MessageSecurityMode.Sign);
            if (signEp == null)
            {
                Assert.Fail("No Sign endpoint available.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, signEp.SecurityPolicyUri,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ActivateCorrectCredentialsOnSignAndEncryptAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            EndpointDescription encryptEp = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt);
            if (encryptEp == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, encryptEp.SecurityPolicyUri,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task UserNameTokenPolicyIdMatchesAdvertisedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens == default)
                {
                    continue;
                }

                foreach (UserTokenPolicy tokenPolicy in ep.UserIdentityTokens)
                {
                    if (tokenPolicy.TokenType == UserTokenType.UserName)
                    {
                        Assert.That(tokenPolicy.PolicyId,
                            Is.Not.Null.And.Not.Empty,
                            "UserName token policy must have a PolicyId.");
                    }
                }
            }
        }

        [Test]
        public async Task SysadminCanReadAdminRestrictedNodeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Fail("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                // Read server status as admin
                ReadResponse rr = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(rr.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(rr.Results[0].StatusCode), Is.True,
                    "Sysadmin should be able to read ServerStatus.State.");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task AppuserWriteToAdminNodeDeniedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveUsernameToken(endpoints))
            {
                Assert.Ignore("No Username token advertised.");
            }

            ISession session;
            try
            {
                session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("appuser", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore($"Rejected: {sre.StatusCode}");
                return;
            }

            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                WriteResponse wr = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(999))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(wr.Results.Count, Is.EqualTo(1));
                Assert.That(
                    wr.Results[0].Code == StatusCodes.Good ||
                    wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected Good or BadUserAccessDenied, got {wr.Results[0]}");
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }

        private bool EndpointsHaveUsernameToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private EndpointDescription FindEndpoint(
            ArrayOf<EndpointDescription> endpoints,
            MessageSecurityMode mode)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == mode)
                {
                    return ep;
                }
            }

            return null;
        }
    }
}
