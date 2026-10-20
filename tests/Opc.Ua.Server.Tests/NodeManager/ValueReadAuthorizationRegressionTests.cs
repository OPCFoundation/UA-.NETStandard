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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies asynchronous subscriber authorization and channel restrictions at the real notification consumer.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class ValueReadAuthorizationRegressionTests
    {
        /// <summary>
        /// Retiring a lease invalidates a suspended approval and remains fail closed until explicitly replaced.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27DisposedReadAuthorizerCannotReleaseInFlightValueAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable lease = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            await harness.EnqueueAsync().ConfigureAwait(false);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                lease.Dispose();
                lease.Dispose();
            }
            finally
            {
                release.TrySetResult(true);
            }
            await harness.WaitAsync().ConfigureAwait(false);
            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadShutdown);

            using IDisposable replacement = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, static (_, _) => new ValueTask<ServiceResult>(ServiceResult.Good));
            await harness.EnqueueAsync().ConfigureAwait(false);
            await harness.WaitAsync().ConfigureAwait(false);
            AssertValue(harness.First.Values.ToArray()[1]);
        }

        /// <summary>
        /// Manager disposal cancels and drains an admitted asynchronous policy before releasing its resources.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27ManagerDisposalCancelsAndDrainsReadAuthorizerAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable lease = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            await harness.EnqueueAsync().ConfigureAwait(false);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await harness.Owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await harness.WaitAsync().ConfigureAwait(false);
                AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadShutdown);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// A policy failure on one subscriber is reported without suppressing another subscriber's authorized value.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27FaultedAuthorizerDoesNotBlockOtherSubscribersAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Receiver second = harness.AddReceiver(2);
            using IDisposable lease = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (context, _) =>
                {
                    await Task.Yield();
                    if (ReferenceEquals(((ServerSystemContext)context).OperationContext!.Session,
                        harness.First.Session.Object))
                    {
                        throw new InvalidOperationException("test policy failure");
                    }
                    return ServiceResult.Good;
                });

            await harness.EnqueueAsync().ConfigureAwait(false);
            await harness.WaitAsync().ConfigureAwait(false);

            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadUnexpectedError);
            AssertValue(second.Values.ToArray()[0]);
        }

        /// <summary>
        /// A system-context reporter cannot provide encryption to a subscriber that has no endpoint.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29NotificationWithoutSubscriberEndpointFailsClosedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            harness.First.Session.SetupGet(value => value.EndpointDescription).Returns((EndpointDescription)null!);
            Receiver second = harness.AddReceiver(2);

            await harness.EnqueueAsync().ConfigureAwait(false);
            await harness.WaitAsync().ConfigureAwait(false);

            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadSecurityModeInsufficient);
            AssertValue(second.Values.ToArray()[0]);
        }

        /// <summary>
        /// Enabling a monitored item cannot lend the service or system caller's transport to its subscriber.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29ImmediateMonitoringRefreshUsesSubscriberEncryptionAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            harness.First.Endpoint.SecurityMode = MessageSecurityMode.Sign;

            await harness.QueueImmediateAsync().ConfigureAwait(false);

            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadSecurityModeInsufficient);
        }

        /// <summary>
        /// A channel downgrade while an asynchronous policy is suspended invalidates its earlier approval.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29InFlightEndpointDowngradeCannotReleaseValueAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable lease = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            await harness.EnqueueAsync().ConfigureAwait(false);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                harness.First.Endpoint.SecurityMode = MessageSecurityMode.Sign;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await harness.WaitAsync().ConfigureAwait(false);

            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadSecurityModeInsufficient);
        }

        /// <summary>
        /// Replacement routing cannot grant access to the retained source during an immediate monitoring refresh.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR44RetainedItemReenableUsesOriginalGenerationReadAclAsync(bool reusedNodeId)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.RolePermissions =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Anonymous, Permissions = (uint)PermissionType.Read }
            ];
            await harness.QueueImmediateAsync().ConfigureAwait(false);
            AssertValue(harness.First.Values.ToArray()[0]);

            harness.RouteVisibleNode(reusedNodeId
                ? new BaseDataVariableState(null) { NodeId = harness.Node.NodeId, Value = 73 }
                : null);
            harness.Node.RolePermissions =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_AuthenticatedUser, Permissions = (uint)PermissionType.Read }
            ];
            harness.Node.Value = 99;
            int reads = 0;
            harness.Node.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
            {
                reads++;
                return ServiceResult.Good;
            };

            await harness.QueueImmediateAsync().ConfigureAwait(false);

            Assert.That(reads, Is.Zero);
            Assert.That(harness.First.Values, Has.Count.EqualTo(2));
            AssertDenied(harness.First.Values.ToArray()[1], StatusCodes.BadUserAccessDenied);
        }

        /// <summary>
        /// An authorized retained subscriber receives the original source, never the replacement's value.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR44AuthorizedRetainedItemReadsExactOwnedSourceAsync(bool reusedNodeId)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.RolePermissions =
            [
                new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Anonymous, Permissions = (uint)PermissionType.Read }
            ];
            harness.RouteVisibleNode(reusedNodeId
                ? new BaseDataVariableState(null) { NodeId = harness.Node.NodeId, Value = 73 }
                : null);
            int authorizations = 0;
            using IDisposable registration = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, (_, _) =>
                {
                    authorizations++;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            await harness.QueueImmediateAsync().ConfigureAwait(false);

            Assert.That(authorizations, Is.EqualTo(2), "Both sides of the source read must use its policy.");
            Assert.That(harness.First.Values, Has.Count.EqualTo(1));
            AssertValue(harness.First.Values.ToArray()[0]);
        }

        /// <summary>
        /// A retained source cannot lend a routing-based grant to a queued or suspended notification.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR44RetainedInitialAndNotificationReadsCannotBorrowVisibleRoutingAsync(bool immediate)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.RouteVisibleNode(null);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            ValueTask delivery = immediate ? harness.QueueImmediateAsync() : harness.EnqueueAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                harness.Node.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.Read
                    }
                ];
                harness.Node.Value = 99;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await delivery.ConfigureAwait(false);
            await harness.WaitAsync().ConfigureAwait(false);

            Assert.That(harness.First.Values, Has.Count.EqualTo(1));
            AssertDenied(harness.First.Values.ToArray()[0], StatusCodes.BadUserAccessDenied);
        }

        /// <summary>
        /// Queued and future protected values survive safe rebinds but cannot move to a plaintext channel.
        /// </summary>
        [TestCase(Profiles.HttpsBinaryTransport)]
        [TestCase(Profiles.UaWssTransport)]
        [TestCase(Profiles.UaTcpTransport)]
        [Category("SecurityRegression")]
        public async Task SecurityF29ReactivationUsesAcceptedTransportForProtectedNotificationsAsync(string profile)
        {
            bool plaintext = profile == Profiles.UaTcpTransport;
            await using var harness = new Harness();
            await harness.InitializeAsync(attachReceiver: false).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            harness.Server.SetupGet(value => value.Telemetry).Returns(telemetry);
            harness.Server.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
            harness.Server.SetupGet(value => value.IdentityRegistry)
                .Returns(new ServerIdentityRegistry(new AnonymousAuthenticator()));
            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics.Setup(value => value.CreateSessionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(), It.IsAny<SessionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(), It.IsAny<SessionSecurityDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(new NodeId("https-session", 1)));
            harness.Server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1_000,
                    MaxSessionTimeout = 60_000,
                    MaxSessionCount = 10
                }
            };
            using var sessions = new SessionManager(harness.Server.Object, configuration);
            harness.Server.SetupGet(value => value.SessionManager).Returns(sessions);
            using Certificate certificate = CertificateBuilder.Create("CN=F29 Transport Evidence")
                .SetRSAKeySize(2048).CreateForRSA();
            EndpointDescription originalEndpoint = Endpoint(Profiles.HttpsBinaryTransport);
            var originalChannel = new SecureChannelContext("https-original", originalEndpoint, RequestEncoding.Binary);
            using var creation = new OperationContext(
                new RequestHeader(), originalChannel, RequestType.CreateSession, RequestLifetime.None);
            CreateSessionResult created = await sessions.CreateSessionAsync(
                creation, certificate, "f29-transport", default, new ApplicationDescription(),
                originalEndpoint.EndpointUrl, null, [], 60_000, 65_536).ConfigureAwait(false);
            try
            {
                await ActivateAsync(originalChannel).ConfigureAwait(false);
                Assert.That(created.Session.ClientCertificate, Is.Null);
                Assert.That(originalChannel.ClientChannelCertificate.IsEmpty, Is.True);
                harness.Node.DataType = DataTypeIds.ByteString;
                harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                ByteString first = ByteString.From("protected-before-rebind"u8);
                harness.Node.Value = first;
                using var originalOperation = new OperationContext(
                    new RequestHeader(), originalChannel, RequestType.CreateMonitoredItems,
                    RequestLifetime.None, created.Session);
                var ids = new MonitoredItemIdFactory();
                var errors = new ServiceResult[1];
                var filters = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];
                await harness.Owner.CreateMonitoredItemsAsync(
                    originalOperation, 1, 100, TimestampsToReturn.Both, [Request()],
                    errors, filters, items, false, ids).ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                var item = (MonitoredItem)items[0];
                Bind(item);

                EndpointDescription reboundEndpoint = Endpoint(profile);
                var reboundChannel = new SecureChannelContext("rebound", reboundEndpoint, RequestEncoding.Binary);
                if (plaintext)
                {
                    ServiceResultException rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await ActivateAsync(reboundChannel).ConfigureAwait(false))
                        ?? throw new AssertionException("Expected a service failure.");
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
                    Assert.That(created.Session.SecureChannelId, Is.EqualTo(originalChannel.SecureChannelId));
                    ServiceResultException rejectedPublish = await Assert.ThrowsAsync<ServiceResultException>(
                        async () =>
                    {
                        using OperationContext denied = await sessions.ValidateRequestAsync(
                            new RequestHeader { AuthenticationToken = created.AuthenticationToken },
                            reboundChannel, RequestType.Publish, RequestLifetime.None).ConfigureAwait(false);
                        _ = Publish(item, denied);
                    }) ?? throw new AssertionException("Expected a service failure.");
                    Assert.That(rejectedPublish.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
                    ServiceResultException rejectedAdmission = await Assert.ThrowsAsync<ServiceResultException>(
                        async () =>
                    {
                        using OperationContext denied = await sessions.ValidateRequestAsync(
                            new RequestHeader { AuthenticationToken = created.AuthenticationToken },
                            reboundChannel, RequestType.CreateMonitoredItems, RequestLifetime.None).ConfigureAwait(false);
                    }) ?? throw new AssertionException("Expected a service failure.");
                    Assert.That(rejectedAdmission.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
                }
                else
                {
                    await ActivateAsync(reboundChannel).ConfigureAwait(false);
                    Assert.That(created.Session.SecureChannelId, Is.EqualTo("rebound"));
                }
                Assert.That(created.Session.EndpointDescription, Is.SameAs(originalEndpoint));
                Assert.That(reboundChannel.ClientChannelCertificate.IsEmpty, Is.True);
                using OperationContext publication = await sessions.ValidateRequestAsync(
                    new RequestHeader { AuthenticationToken = created.AuthenticationToken },
                    plaintext ? originalChannel : reboundChannel, RequestType.Publish, RequestLifetime.None)
                    .ConfigureAwait(false);
                AssertPayload(Publish(item, publication), first);
                ByteString next = ByteString.From("protected-after-rebind"u8);
                harness.Node.Value = next;
                await harness.Node.ClearChangeMasksAsync(harness.Owner.SystemContext, false).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await harness.Owner.MonitoredNodes[harness.Node.NodeId]
                    .WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
                AssertPayload(Publish(item, publication), next);

                var laterErrors = new ServiceResult[1];
                var laterItems = new IMonitoredItem[1];
                await harness.Owner.CreateMonitoredItemsAsync(
                    publication, 1, 100, TimestampsToReturn.Both, [Request()],
                    laterErrors, filters, laterItems, false, ids).ConfigureAwait(false);
                Assert.That(laterErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                var later = (MonitoredItem)laterItems[0];
                Bind(later);
                AssertPayload(Publish(later, publication), next);

                async ValueTask ActivateAsync(SecureChannelContext channel)
                {
                    using var operation = new OperationContext(
                        new RequestHeader(), channel, RequestType.ActivateSession, RequestLifetime.None);
                    await sessions.ActivateSessionAsync(
                        operation, created.AuthenticationToken, new SignatureData(),
                        new ExtensionObject(new AnonymousIdentityToken { PolicyId = "anonymous" }),
                        new SignatureData(), []).ConfigureAwait(false);
                }

                MonitoredItemCreateRequest Request()
                {
                    return new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = harness.Node.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, SamplingInterval = 0, QueueSize = 1, DiscardOldest = true
                        }
                    };
                }

                void Bind(MonitoredItem monitoredItem)
                {
                    var subscription = new Mock<ISubscription>();
                    subscription.SetupGet(value => value.Session).Returns(created.Session);
                    subscription.SetupGet(value => value.EffectiveIdentity)
                        .Returns(() => created.Session.EffectiveIdentity);
                    monitoredItem.SubscriptionCallback = subscription.Object;
                }
            }
            finally
            {
                await sessions.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
            }

            static EndpointDescription Endpoint(string transportProfile)
            {
                return new EndpointDescription
                {
                    EndpointUrl = transportProfile switch
                    {
                        Profiles.UaTcpTransport => "opc.tcp://localhost/f29",
                        Profiles.UaWssTransport => "wss://localhost/f29",
                        _ => "https://localhost/f29"
                    },
                    TransportProfileUri = transportProfile,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous", TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
            }

            static DataValue Publish(MonitoredItem item, OperationContext context)
            {
                var values = new Queue<MonitoredItemNotification>();
                item.Publish(context, values, new Queue<DiagnosticInfo>(), 1, NullLogger.Instance);
                Assert.That(values, Has.Count.EqualTo(1));
                return values.Dequeue().Value;
            }
        }

        /// <summary>
        /// Verifies an authorized transport receives the exact protected bytes.
        /// </summary>
        private static void AssertPayload(in DataValue value, ByteString expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ByteString actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Asserts an authorization error contains no value from the reporter's snapshot.
        /// </summary>
        private static void AssertDenied(in DataValue value, StatusCode expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(expected));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// Asserts the authorized receiver obtained the exact report-time value.
        /// </summary>
        private static void AssertValue(in DataValue value)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(42));
        }

        /// <summary>
        /// Wires a real asynchronous node manager and notification consumer to deterministic in-memory services.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates the real node, manager, and notification pipeline without starting a server host.
            /// </summary>
            public Harness()
            {
                Server = DeterministicServerMock.Create(out m_queues);
                Owner = new TestableAsyncCustomNodeManager(
                    Server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                Node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("PolicyValue", Owner.NamespaceIndex),
                    BrowseName = new QualifiedName("PolicyValue", Owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = 42
                };
                Mock.Get(Server.Object.NodeManager)
                    .Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, CancellationToken>((id, _) =>
                        new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                            id == Node.NodeId ? (new NodeHandle(id, Node), Owner) : (null, null)));
                m_monitored = new MonitoredNode2(Owner, Server.Object, Node);
            }

            /// <summary>
            /// Gets the owner exposing the new registration API.
            /// </summary>
            public TestableAsyncCustomNodeManager Owner { get; }

            /// <summary>
            /// Gets the in-memory services for real SessionManager composition.
            /// </summary>
            public Mock<IServerInternal> Server { get; }

            /// <summary>
            /// Gets the shared-snapshot variable.
            /// </summary>
            public BaseDataVariableState Node { get; }

            /// <summary>
            /// Gets the original subscriber and its observed values.
            /// </summary>
            public Receiver First { get; private set; } = null!;

            /// <summary>
            /// Registers the variable in the real node manager.
            /// </summary>
            public async ValueTask InitializeAsync(bool attachReceiver = true)
            {
                await Owner.AddNodeAsync(Owner.SystemContext, NodeId.Null, Node).ConfigureAwait(false);
                if (attachReceiver)
                {
                    First = AddReceiver(1);
                }
            }

            /// <summary>
            /// Adds an independent current subscriber with its own session and encrypted endpoint.
            /// </summary>
            public Receiver AddReceiver(uint id)
            {
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
                var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(id, 1));
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                session.SetupGet(value => value.EndpointDescription).Returns(endpoint);
                var item = new Mock<IDataChangeMonitoredItem2>();
                item.SetupGet(value => value.Id).Returns(id);
                item.SetupGet(value => value.NodeId).Returns(Node.NodeId);
                item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
                item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
                item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
                item.SetupGet(value => value.Session).Returns(session.Object);
                item.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                var values = new ConcurrentQueue<DataValue>();
                item.Setup(value => value.QueueValue(It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>()))
                    .Callback(new QueueValueCallback((in value, _) => values.Enqueue(value)));
                m_monitored.Add(item.Object);
                return new Receiver(session, item, endpoint, values);
            }

            /// <summary>
            /// Reports a value in the owner's trusted system context rather than a subscriber context.
            /// </summary>
            public ValueTask EnqueueAsync()
            {
                return m_monitored.OnMonitoredNodeChangedAsync(
                    Owner.SystemContext, Node, NodeStateChangeMasks.Value);
            }

            /// <summary>
            /// Models shadow replacement routing independently of the original monitored source.
            /// </summary>
            public void RouteVisibleNode(NodeState? visible)
            {
                Mock.Get(Server.Object.NodeManager)
                    .Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, CancellationToken>((id, _) =>
                        new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                            visible != null && id == visible.NodeId
                                ? (new NodeHandle(id, visible), Owner) : (null, null)));
            }

            /// <summary>
            /// Uses the same immediate-refresh path as enabling an existing monitored item.
            /// </summary>
            public ValueTask QueueImmediateAsync()
            {
                return m_monitored.QueueValueAsync(
                    Owner.SystemContext, Node, First.Item.Object, CancellationToken.None);
            }

            /// <summary>
            /// Observes completed delivery without sleeps.
            /// </summary>
            public async ValueTask WaitAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await m_monitored.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Drains notifications before disposing node-manager and queue resources.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                Owner.Dispose();
                await m_monitored.DisposeAsync().ConfigureAwait(false);
                await Owner.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            /// <summary>The test-owned queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;

            /// <summary>The production notification consumer.</summary>
            private readonly MonitoredNode2 m_monitored;
        }

        /// <summary>
        /// Captures one modeled subscriber's current endpoint and observed notifications.
        /// </summary>
        private sealed record Receiver(
            Mock<ISession> Session,
            Mock<IDataChangeMonitoredItem2> Item,
            EndpointDescription Endpoint,
            ConcurrentQueue<DataValue> Values);

        /// <summary>
        /// Matches the ref-readonly queue callback without boxing the reported value.
        /// </summary>
        private delegate void QueueValueCallback(in DataValue value, ServiceResult? error);
    }
}
