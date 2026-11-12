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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Validation of the user token security policy by normalized name, and
    /// the bound of the best effort monitored item rollback.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [Parallelizable]
    public sealed class ClientSecurityValidationTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// A user token policy that names the None policy by its short name is
        /// treated like the full None URI: without the opt-in a UserName token
        /// is not sent on a channel that does not encrypt.
        /// </summary>
        [TestCase("None")]
        [TestCase(SecurityPolicies.None)]
        public async Task TokenPolicyIsValidatedByNormalizedNameAsync(string tokenPolicyUri)
        {
            using var cts = new CancellationTokenSource(s_timeout);
            await using var harness = new SessionChannelHarness();

            ConfiguredEndpoint endpoint = CreateUserNameEndpoint(tokenPolicyUri);
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            bool activateSent = false;
            channel.RequestHandler = (request, _) =>
            {
                activateSent |= request is ActivateSessionRequest;
                return new ValueTask<IServiceResponse>(channel.CreateResponse(request));
            };

            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance,
                timeProvider: harness.TimeProvider);

            Assert.That(
                harness.Configuration.SecurityConfiguration.AllowUnencryptedUserNameTokens,
                Is.False);

            var identity = new UserIdentity("user1", Encoding.UTF8.GetBytes("password"))
            {
                PolicyId = "un-policy"
            };

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.OpenAsync(
                    "token-policy-name", 60000, identity, default, false, false, cts.Token)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(
                    ex.Message,
                    Does.Contain(nameof(SecurityConfiguration.AllowUnencryptedUserNameTokens)));
                Assert.That(activateSent, Is.False);
            });
        }

        /// <summary>
        /// A user token policy the client does not know cannot protect a
        /// UserName token, and an encrypting token policy needs a server
        /// certificate: the session is not activated in either case.
        /// </summary>
        [TestCase("http://example.org/UA/SecurityPolicy#Unknown")]
        [TestCase("Basic256Sha256")]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        public async Task UnusableTokenPolicyIsRefusedAsync(string tokenPolicyUri)
        {
            using var cts = new CancellationTokenSource(s_timeout);
            await using var harness = new SessionChannelHarness();

            ConfiguredEndpoint endpoint = CreateUserNameEndpoint(tokenPolicyUri);
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            bool activateSent = false;
            channel.RequestHandler = (request, _) =>
            {
                activateSent |= request is ActivateSessionRequest;
                return new ValueTask<IServiceResponse>(channel.CreateResponse(request));
            };

            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance,
                timeProvider: harness.TimeProvider);

            var identity = new UserIdentity("user1", Encoding.UTF8.GetBytes("password"))
            {
                PolicyId = "un-policy"
            };

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.OpenAsync(
                    "token-policy-unusable", 60000, identity, default, false, false, cts.Token)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(activateSent, Is.False);
            });
        }

        /// <summary>
        /// When a later batch of a batched CreateMonitoredItems call fails, the
        /// best effort deletion of the items the earlier batches created is
        /// bounded: it cannot keep the call from completing.
        /// </summary>
        [Test]
        public async Task BatchedCreateRollbackIsBoundedAsync()
        {
            using SessionMock sessionMock = SessionMock.Create();
            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 1;
            sessionMock.Channel.SetupGet(c => c.OperationTimeout).Returns(250);

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new MonitoredItemCreateResult
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 7
                        }
                    ]
                })
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadConnectionClosed));

            // The rollback request does not complete until it is cancelled, as
            // a request waiting for a channel that does not recover.
            var deleteSent = new TaskCompletionSource<ArrayOf<uint>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken ct) =>
                    WaitForCancellationAsync(
                        ((DeleteMonitoredItemsRequest)request).MonitoredItemIds, deleteSent, ct));

            ArrayOf<MonitoredItemCreateRequest> items = Enumerable.Range(1, 2)
                .Select(i => new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = new NodeId((uint)i),
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting
                })
                .ToArrayOf();

            Task<CreateMonitoredItemsResponse> create = sessionMock.CreateMonitoredItemsAsync(
                null,
                1u,
                TimestampsToReturn.Both,
                items,
                CancellationToken.None).AsTask();

            Task completed = await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(30)))
                .ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(create), "the rollback must be bounded");

            ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await create.ConfigureAwait(false)))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));

            Assert.That(deleteSent.Task.IsCompleted, Is.True, "the rollback was attempted");
            Assert.That(deleteSent.Task.Result.ToArray(), Is.EqualTo(new uint[] { 7 }));
        }

        private static async ValueTask<IServiceResponse> WaitForCancellationAsync(
            ArrayOf<uint> monitoredItemIds,
            TaskCompletionSource<ArrayOf<uint>> sent,
            CancellationToken ct)
        {
            sent.TrySetResult(monitoredItemIds);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }

        private static ConfiguredEndpoint CreateUserNameEndpoint(string tokenPolicyUri)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/None",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                ServerCertificate = ByteString.Empty,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "un-policy",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = tokenPolicyUri
                    }
                ]
            };
            description.Server.ApplicationUri = "urn:localhost:server";
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(
                null, description, new EndpointConfiguration { OperationTimeout = 6000 })
            {
                UpdateBeforeConnect = false
            };
        }
    }
}
