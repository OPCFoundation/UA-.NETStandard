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

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Server;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end Session-less Service invocation (OPC 10000-4 §6.3) over
    /// opc.tcp: requests carry an Access Token in the RequestHeader and are
    /// sent on a SecureChannel without CreateSession / ActivateSession.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("SessionlessInvocation")]
    [NonParallelizable]
    public class SessionlessServiceInvocationTests : TestFixture
    {
        private const string kAccessToken = "e2e.session-less.access-token";
        private const string kAdminAccessToken = "e2e.session-less.admin-token";
        private const int kTimeout = 30_000;

        private AccessTokenAuthenticator m_authenticator;
        private SessionManager m_sessionManager;

        [OneTimeSetUp]
        public void EnableSessionlessInvocation()
        {
            m_sessionManager = (SessionManager)ReferenceServer.CurrentInstance.SessionManager;
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions();
            m_authenticator = new AccessTokenAuthenticator(kAccessToken, kAdminAccessToken);
            ReferenceServer.CurrentInstance.IdentityRegistry.Register(m_authenticator);
        }

        [OneTimeTearDown]
        public void DisableSessionlessInvocation()
        {
            if (m_sessionManager != null)
            {
                m_sessionManager.SessionlessInvocation = null;
            }
            if (m_authenticator != null)
            {
                ReferenceServer?.CurrentInstance?.IdentityRegistry.Unregister(m_authenticator);
            }
        }

        [Test]
        public async Task ReadWithAnAccessTokenNeedsNoSessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);
            int calls = m_authenticator.Calls;

            ReadResponse response = await client.ReadAsync(
                AccessTokenHeader(),
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                response.Results[0].StatusCode.ToString());
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out int state), Is.True);
            Assert.That((ServerState)state, Is.EqualTo(ServerState.Running));
            Assert.That(m_authenticator.Calls, Is.GreaterThan(calls),
                "the Access Token must be validated by the identity registry");
        }

        [Test]
        public async Task AnAccessTokenOnAnUnencryptedChannelIsRejectedAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.None)
                .ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(
                    AccessTokenHeader(),
                    0,
                    TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task ARequestWithoutAnIdentityIsRejectedAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(
                    new RequestHeader { TimeoutHint = kTimeout },
                    0,
                    TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task SubscriptionsStillNeedASessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CreateSubscriptionAsync(
                    AccessTokenHeader(),
                    1000,
                    100,
                    10,
                    0,
                    true,
                    0,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public async Task ViewServicesRunWithoutASessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            BrowseResponse browse = await client.BrowseAsync(
                AccessTokenHeader(),
                null,
                0,
                [
                    new BrowseDescription
                    {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(browse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(browse.Results[0].StatusCode), Is.True);
            Assert.That(
                browse.Results[0].References.ToArray()!.Any(r => r.NodeId == ObjectIds.Server),
                Is.True,
                "the Objects folder must reference the Server object");

            TranslateBrowsePathsToNodeIdsResponse translate = await client.TranslateBrowsePathsToNodeIdsAsync(
                AccessTokenHeader(),
                [
                    new BrowsePath
                    {
                        StartingNode = ObjectIds.Server,
                        RelativePath = new RelativePath(new QualifiedName(BrowseNames.ServerStatus))
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(translate.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(translate.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task AttributeServicesRunWithoutASessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            // ServerStatus is read-only: the Service runs and reports the
            // operation result.
            WriteResponse write = await client.WriteAsync(
                AccessTokenHeader(),
                [
                    new WriteValue
                    {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((int)ServerState.Running))
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(write.Results.Count, Is.EqualTo(1));
            AssertOperationResult(write.Results[0]);
            Assert.That(StatusCode.IsBad(write.Results[0]), Is.True);

            HistoryReadResponse history = await client.HistoryReadAsync(
                AccessTokenHeader(),
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = DateTime.UtcNow.AddMinutes(-1),
                    EndTime = DateTime.UtcNow,
                    NumValuesPerNode = 10
                }),
                TimestampsToReturn.Both,
                false,
                [new HistoryReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime }],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(history.Results.Count, Is.EqualTo(1));
            AssertOperationResult(history.Results[0].StatusCode);
            Assert.That(history.Results[0].ContinuationPoint.IsEmpty, Is.True,
                "a request without a Session gets no continuation point");
        }

        [Test]
        public async Task MethodServicesRunWithoutASessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            CallResponse response = await client.CallAsync(
                AccessTokenHeader(),
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = [new Variant(uint.MaxValue)]
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            AssertOperationResult(response.Results[0].StatusCode);
        }

        [Test]
        public async Task NodeManagementServicesRunWithoutASessionAsync()
        {
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            AddNodesResponse response = await client.AddNodesAsync(
                AccessTokenHeader(),
                [
                    new AddNodesItem
                    {
                        ParentNodeId = ObjectIds.ObjectsFolder,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        BrowseName = new QualifiedName("SessionlessNode"),
                        NodeClass = NodeClass.Object,
                        TypeDefinition = ObjectTypeIds.BaseObjectType,
                        NodeAttributes = new ExtensionObject(new ObjectAttributes
                        {
                            DisplayName = new LocalizedText("SessionlessNode")
                        })
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            AssertOperationResult(response.Results[0].StatusCode);
        }

        [Test]
        public async Task ARequestWithoutATokenIsUnsupportedWhileTheFeatureIsOffAsync()
        {
            SessionlessInvocationOptions options = m_sessionManager.SessionlessInvocation!;
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);
            try
            {
                m_sessionManager.SessionlessInvocation = null;

                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReadAsync(
                        new RequestHeader { TimeoutHint = kTimeout },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        CancellationToken.None).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));

                // a token that names no Session is still an invalid Session
                error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReadAsync(
                        new RequestHeader { AuthenticationToken = new NodeId(4711u, 0), TimeoutHint = kTimeout },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        CancellationToken.None).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            }
            finally
            {
                m_sessionManager.SessionlessInvocation = options;
            }
        }

        [Test]
        public async Task SessionlessRequestsRunInABudgetOfTheirOwnAsync()
        {
            SessionlessInvocationOptions options = m_sessionManager.SessionlessInvocation!;
            int maxRequests = options!.MaxConcurrentRequests;
            int maxPerChannel = options.MaxConcurrentRequestsPerChannel;
            SessionClient? client1 = null;
            SessionClient? client2 = null;
            SessionClient? client3 = null;
            try
            {
                options.MaxConcurrentRequests = 2;
                options.MaxConcurrentRequestsPerChannel = 0;
                client1 = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                    .ConfigureAwait(false);
                client2 = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                    .ConfigureAwait(false);
                client3 = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                    .ConfigureAwait(false);

                // two requests wait in the validation of their Access Token and hold their places
                m_authenticator.HoldRequests();
                Task<ReadResponse> first = ReadStateAsync(client1);
                Task<ReadResponse> second = ReadStateAsync(client2);
                await m_authenticator.WaitUntilHeldAsync(2).ConfigureAwait(false);

                ServiceResultException busy = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await ReadStateAsync(client3).ConfigureAwait(false));
                Assert.That(busy.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

                m_authenticator.ReleaseRequests();
                Assert.That(StatusCode.IsGood((await first.ConfigureAwait(false)).Results[0].StatusCode), Is.True);
                Assert.That(StatusCode.IsGood((await second.ConfigureAwait(false)).Results[0].StatusCode), Is.True);

                // completed requests have returned their places
                for (int i = 0; i < 5; i++)
                {
                    ReadResponse response = await ReadStateAsync(client3).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
                }

                // a request the Service rejects returns its place too
                for (int i = 0; i < 5; i++)
                {
                    ReadResponse response = await client3.ReadAsync(
                        AccessTokenHeader(),
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = new NodeId(4711u, 0), AttributeId = Attributes.Value }],
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
                }
                ReadResponse last = await ReadStateAsync(client3).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(last.Results[0].StatusCode), Is.True);

                // a channel can be limited on its own
                options.MaxConcurrentRequests = 0;
                options.MaxConcurrentRequestsPerChannel = 1;
                m_authenticator.HoldRequests();
                Task<ReadResponse> held = ReadStateAsync(client1);
                await m_authenticator.WaitUntilHeldAsync(1).ConfigureAwait(false);

                ServiceResultException channelBusy = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await ReadStateAsync(client1).ConfigureAwait(false));
                Assert.That(channelBusy.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

                Task<ReadResponse> otherChannel = ReadStateAsync(client2);
                await m_authenticator.WaitUntilHeldAsync(2).ConfigureAwait(false);

                m_authenticator.ReleaseRequests();
                Assert.That(StatusCode.IsGood((await held.ConfigureAwait(false)).Results[0].StatusCode), Is.True);
                Assert.That(
                    StatusCode.IsGood((await otherChannel.ConfigureAwait(false)).Results[0].StatusCode),
                    Is.True);
                ReadResponse again = await ReadStateAsync(client1).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(again.Results[0].StatusCode), Is.True);
            }
            finally
            {
                m_authenticator.ReleaseRequests();
                client1?.Dispose();
                client2?.Dispose();
                client3?.Dispose();
                options.MaxConcurrentRequests = maxRequests;
                options.MaxConcurrentRequestsPerChannel = maxPerChannel;
            }
        }

        [Test]
        public async Task ARoleMappedNodeIsReadWithTheRolesMappedFromTheAccessTokenAsync()
        {
            // The summary of the server diagnostics is readable for the SecurityAdmin role only.
            IRoleManager roleManager = ReferenceServer.CurrentInstance.RoleManager;
            var rule = new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.Role,
                Criteria = "SecurityAdmin"
            };
            using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                .ConfigureAwait(false);

            // the claim alone grants nothing: the role mapping of the server decides
            StatusCode unmapped = await ReadDiagnosticsSummaryAsync(client, kAdminAccessToken)
                .ConfigureAwait(false);
            Assert.That(unmapped, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(ObjectIds.WellKnownRole_SecurityAdmin, rule)), Is.True);
            try
            {
                StatusCode mapped = await ReadDiagnosticsSummaryAsync(client, kAdminAccessToken)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(mapped), Is.True, mapped.ToString());

                // a token without the claim stays denied
                StatusCode other = await ReadDiagnosticsSummaryAsync(client, kAccessToken)
                    .ConfigureAwait(false);
                Assert.That(other, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                roleManager.RemoveIdentity(ObjectIds.WellKnownRole_SecurityAdmin, rule);
            }
        }

        [Test]
        public async Task ACancelledAccessTokenValidationAnswersTheTimeoutAndReturnsItsPlaceAsync()
        {
            SessionlessInvocationOptions options = m_sessionManager.SessionlessInvocation!;
            int maxRequests = options!.MaxConcurrentRequests;
            try
            {
                options.MaxConcurrentRequests = 1;
                using SessionClient client = await OpenClientAsync(MessageSecurityMode.SignAndEncrypt)
                    .ConfigureAwait(false);

                // the validation waits until the request times out on the server
                m_authenticator.HoldRequests();
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReadAsync(
                        new RequestHeader
                        {
                            AuthenticationToken = new NodeId(kAccessToken, 0),
                            TimeoutHint = 1000
                        },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        CancellationToken.None).ConfigureAwait(false));
                m_authenticator.ReleaseRequests();

                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));

                // the only place of the budget is free again
                ReadResponse response = await ReadStateAsync(client).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            }
            finally
            {
                m_authenticator.ReleaseRequests();
                options.MaxConcurrentRequests = maxRequests;
            }
        }

        private static async Task<StatusCode> ReadDiagnosticsSummaryAsync(SessionClient client, string accessToken)
        {
            ReadResponse response = await client.ReadAsync(
                new RequestHeader { AuthenticationToken = new NodeId(accessToken, 0), TimeoutHint = kTimeout },
                0,
                TimestampsToReturn.Neither,
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary,
                        AttributeId = Attributes.Value
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0].StatusCode;
        }

        private static Task<ReadResponse> ReadStateAsync(SessionClient client)
        {
            return client.ReadAsync(
                AccessTokenHeader(),
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                CancellationToken.None).AsTask();
        }

        private static RequestHeader AccessTokenHeader()
        {
            return new RequestHeader
            {
                AuthenticationToken = new NodeId(kAccessToken, 0),
                TimeoutHint = kTimeout
            };
        }

        /// <summary>
        /// The Service ran for the operation: a failure is the operation's
        /// own result, not a missing Session or an internal error.
        /// </summary>
        private static void AssertOperationResult(StatusCode statusCode)
        {
            Assert.That(statusCode, Is.Not.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(statusCode, Is.Not.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(statusCode, Is.Not.EqualTo(StatusCodes.BadInternalError));
        }

        private async Task<SessionClient> OpenClientAsync(MessageSecurityMode securityMode)
        {
            ArrayOf<EndpointDescription> endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl)
                .ConfigureAwait(false);
            EndpointDescription endpoint = endpoints.ToArray()!.FirstOrDefault(e =>
                e.EndpointUrl!.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal) &&
                e.SecurityMode == securityMode &&
                ClientFixture.Config.SecurityConfiguration.SupportedSecurityPolicies.Contains(e.SecurityPolicyUri!))!;
            if (endpoint == null)
            {
                Assert.Ignore($"The server offers no {securityMode} endpoint.");
            }

            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            endpointConfiguration.OperationTimeout = kTimeout;
            ITransportChannel channel = await ClientFixture
                .CreateChannelAsync(new ConfiguredEndpoint(null, endpoint, endpointConfiguration), false)
                .ConfigureAwait(false);
            return new SessionClient(channel, Telemetry);
        }

        /// <summary>
        /// Accepts one Access Token, as an identity provider would issue it.
        /// </summary>
        private sealed class AccessTokenAuthenticator : IUserTokenAuthenticator
        {
            public AccessTokenAuthenticator(string accessToken, string adminAccessToken)
            {
                m_accessToken = accessToken;
                m_adminAccessToken = adminAccessToken;
            }

            public UserTokenType TokenType => UserTokenType.IssuedToken;

            public string IssuedTokenProfileUri => Profiles.JwtUserToken;

            public int Calls => m_calls;

            /// <summary>
            /// Makes the validation of every Access Token wait until
            /// <see cref="ReleaseRequests"/>.
            /// </summary>
            public void HoldRequests()
            {
                Volatile.Write(
                    ref m_gate,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            public void ReleaseRequests()
            {
                Interlocked.Exchange(ref m_gate!, null)?.TrySetResult(true);
            }

            public async Task WaitUntilHeldAsync(int count)
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(kTimeout);
                while (Volatile.Read(ref m_held) < count)
                {
                    Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the requests did not reach the validation");
                    await Task.Delay(10).ConfigureAwait(false);
                }
            }

            public async ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_calls);
                TaskCompletionSource<bool> gate = Volatile.Read(ref m_gate);
                if (gate != null)
                {
                    Interlocked.Increment(ref m_held);
                    try
                    {
                        await Task.WhenAny(gate.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref m_held);
                    }
                }
                if (context.TokenHandler is IssuedIdentityTokenHandler issued &&
                    issued.DecryptedTokenData != null &&
                    Encoding.UTF8.GetString(issued.DecryptedTokenData) == m_accessToken)
                {
                    return AuthenticationResult.Accept(new UserIdentity(issued));
                }
                if (context.TokenHandler is IssuedIdentityTokenHandler admin &&
                    admin.DecryptedTokenData != null &&
                    Encoding.UTF8.GetString(admin.DecryptedTokenData) == m_adminAccessToken)
                {
                    // a token that asserts the SecurityAdmin role claim
                    string[] roles = ["SecurityAdmin"];
                    var claims = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["sub"] = "admin-user",
                        ["roles"] = roles
                    };
                    return AuthenticationResult.Accept(
                        new JwtUserIdentity(admin, claims, [], roles, "urn:test:issuer", "admin-user"));
                }
                return AuthenticationResult.NotHandled;
            }

            private readonly string m_accessToken;
            private readonly string m_adminAccessToken;
            private TaskCompletionSource<bool> m_gate = null!;
            private int m_calls;
            private int m_held;
        }
    }
}
