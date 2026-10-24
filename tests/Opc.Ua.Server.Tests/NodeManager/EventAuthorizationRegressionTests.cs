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
    /// Verifies event authorization across live role reconciliation without changing the event's cache key.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class EventAuthorizationRegressionTests
    {
        /// <summary>
        /// Revocation must protect the very first event generated before a client has refreshed its roles.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR36PostRevocationEventCannotReachFirstPublishAsync(bool serverNotifier)
        {
            await using var harness = new EventHarness(serverNotifier);
            harness.UseConfidentialTransport();
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.ReportAsync("event-42").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo<string[]>(["event-42"]));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo<string[]>(["event-42"]));

            harness.SetAliceRole(false);
            await harness.ReportAsync("secret-after-revoke").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo<string[]>(["event-42"]),
                "No request-driven role refresh has happened since revocation.");
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo<string[]>(["event-42", "secret-after-revoke"]));
            await harness.PublishAsync().ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo<string[]>(["event-42"]));

            harness.SetAliceRole(true);
            await harness.ReportAsync("newly-authorized").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo<string[]>(["event-42", "newly-authorized"]));
        }

        /// <summary>
        /// A suspended successful decision cannot survive revocation merely because no client request has arrived.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR36RevocationDuringBackgroundAuthorizationInvalidatesQueuedEpochAsync(
            bool serverNotifier)
        {
            await using var harness = new EventHarness(serverNotifier);
            harness.UseConfidentialTransport();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.BlockAliceValidation = true;
            await harness.EnqueueAsync("secret-after-revoke").ConfigureAwait(false);
            try
            {
                await harness.ValidationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                harness.SetAliceRole(false);
            }
            finally
            {
                harness.ReleaseValidation.TrySetResult(true);
            }
            await harness.WaitForDeliveryAsync().ConfigureAwait(false);
            Assert.That(harness.AliceEvents, Is.Empty);
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo<string[]>(["secret-after-revoke"]));
            await harness.PublishAsync().ConfigureAwait(false);
            Assert.That(harness.AliceEvents, Is.Empty);
        }

        /// <summary>
        /// Publish refreshes a revoked role, stops only its receiver, and resumes delivery after the role is regranted.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF44RevokedRoleCannotReuseEventVerdictAsync(bool serverNotifier)
        {
            await using var harness = new EventHarness(serverNotifier);
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.ReportAsync("before-revocation").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_beforeRevocation));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_beforeRevocation));

            harness.SetAliceRole(false);
            Assert.That(harness.Alice.Session.IsIdentityStale, Is.True);
            await harness.PublishAsync().ConfigureAwait(false);
            Assert.That(harness.Alice.Session.IsIdentityStale, Is.False);
            Assert.That(harness.Alice.Session.EffectiveIdentity.GrantedRoleIds.Contains(
                ObjectIds.WellKnownRole_Observer), Is.False);

            await harness.ReportAsync("after-revocation").ConfigureAwait(false);

            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_beforeRevocation));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_afterRevocation));

            harness.SetAliceRole(true);
            await harness.PublishAsync().ConfigureAwait(false);
            await harness.ReportAsync("after-regrant").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_regrantedSubscriber));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_regrantUnchangedSubscriber));
        }

        /// <summary>
        /// Changes to either event ACL invalidate both successful and denied verdicts without changing the notifier.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF44TypeAndSourcePermissionChangesRevalidateSameEventAsync(
            bool eventType, bool userPermissions)
        {
            await using var harness = new EventHarness(true);
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.ReportAsync("before-acl-change").ConfigureAwait(false);
            NodeState restricted = eventType ? harness.EventType : harness.Source;
            ArrayOf<RolePermissionType> original = restricted.RolePermissions;
            ArrayOf<RolePermissionType> reduced =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)PermissionType.ReceiveEvents
                }
            ];
            if (userPermissions)
            {
                restricted.UserRolePermissions = reduced;
            }
            else
            {
                restricted.RolePermissions = reduced;
            }

            await harness.ReportAsync("after-acl-revocation").ConfigureAwait(false);

            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_beforeAclChange));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_afterAclRevocation));
            if (userPermissions)
            {
                restricted.UserRolePermissions = default;
            }
            else
            {
                restricted.RolePermissions = original;
            }
            await harness.ReportAsync("after-acl-regrant").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_aclRegrantedSubscriber));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_aclUnchangedSubscriber));
        }

        /// <summary>
        /// An approval suspended before queueing must not survive a live role refresh or source ACL change.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF44InFlightRevocationCannotQueueOldApprovalAsync(
            bool serverNotifier, bool sourcePermissions)
        {
            await using var harness = new EventHarness(serverNotifier);
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.BlockAliceValidation = true;
            await harness.EnqueueAsync("in-flight-approval").ConfigureAwait(false);
            try
            {
                await harness.ValidationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (sourcePermissions)
                {
                    harness.Source.UserRolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Operator,
                            Permissions = (uint)PermissionType.ReceiveEvents
                        }
                    ];
                }
                else
                {
                    harness.SetAliceRole(false);
                    await harness.PublishAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                harness.ReleaseValidation.TrySetResult(true);
            }
            await harness.WaitForDeliveryAsync().ConfigureAwait(false);

            Assert.That(harness.AliceEvents, Is.Empty);
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_inFlightApproval));
            if (sourcePermissions)
            {
                harness.Source.UserRolePermissions = default;
            }
            else
            {
                harness.SetAliceRole(true);
                await harness.PublishAsync().ConfigureAwait(false);
            }
            await harness.ReportAsync("current-approval").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_currentApproval));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_inFlightAndCurrent));
        }

        /// <summary>
        /// A role change between the freshness check and context capture must not drop an unaffected subscriber's event.
        /// </summary>
        /// <param name="serverNotifier">Whether the event is reported through the Server notifier.</param>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF44RoleChangeDuringAuthorizationCaptureRetainsUnaffectedSubscriberAsync(
            bool serverNotifier)
        {
            await using var harness = new EventHarness(serverNotifier);
            await harness.InitializeAsync().ConfigureAwait(false);
            int sessionReads = 0;
            harness.BobReceiver.SetupGet(value => value.Session).Returns(() =>
            {
                if (Interlocked.Increment(ref sessionReads) == 2)
                {
                    harness.SetAliceRole(false);
                }
                return harness.Bob.Session;
            });

            await harness.ReportAsync("role-change-during-capture").ConfigureAwait(false);

            Assert.That(sessionReads, Is.GreaterThanOrEqualTo(2));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo<string[]>(["role-change-during-capture"]));

            await harness.ReportAsync("after-revocation").ConfigureAwait(false);

            Assert.That(harness.AliceEvents, Does.Not.Contain("after-revocation"));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo<string[]>(
                ["role-change-during-capture", "after-revocation"]));
        }

        /// <summary>
        /// Reactivation changes the effective principal without allowing its predecessor's event verdict to persist.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF44ReactivatedIdentityCannotReuseEventVerdictAsync()
        {
            await using var harness = new EventHarness(true);
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.ReportAsync("alice-approved").ConfigureAwait(false);

            await harness.ActivateAsync(harness.Alice, "mallory").ConfigureAwait(false);
            Assert.That(harness.Alice.Session.EffectiveIdentity.DisplayName, Is.EqualTo("mallory"));
            await harness.ReportAsync("mallory-denied").ConfigureAwait(false);

            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_aliceApproved));
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_malloryDenied));
            await harness.ActivateAsync(harness.Alice, "alice").ConfigureAwait(false);
            await harness.ReportAsync("alice-restored").ConfigureAwait(false);
            Assert.That(harness.AliceEvents.ToArray(), Is.EqualTo(s_aliceRestored));
        }

        /// <summary>
        /// A failing receiver cannot suppress a separately authorized receiver in either fanout mode.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF44FaultedReceiverDoesNotBlockAuthorizedFanoutAsync(bool serverNotifier)
        {
            await using var harness = new EventHarness(serverNotifier);
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.AliceReceiver.Setup(value => value.QueueEvent(It.IsAny<IFilterTarget>()))
                .Throws(new InvalidOperationException("test receiver failure"));

            await harness.ReportAsync("independent-receiver").ConfigureAwait(false);

            Assert.That(harness.AliceEvents, Is.Empty);
            Assert.That(harness.BobEvents.ToArray(), Is.EqualTo(s_independentReceiver));
        }

        /// <summary>Events admitted before a role mapping changes.</summary>
        private static readonly string[] s_beforeRevocation = ["before-revocation"];

        /// <summary>Events retained by an unaffected subscriber after role revocation.</summary>
        private static readonly string[] s_afterRevocation = ["before-revocation", "after-revocation"];

        /// <summary>Events admitted to the revoked subscriber before revocation and after regrant.</summary>
        private static readonly string[] s_regrantedSubscriber = ["before-revocation", "after-regrant"];

        /// <summary>All events admitted to the unaffected subscriber during role reconciliation.</summary>
        private static readonly string[] s_regrantUnchangedSubscriber =
            ["before-revocation", "after-revocation", "after-regrant"];

        /// <summary>Events admitted before a source or event-type ACL changes.</summary>
        private static readonly string[] s_beforeAclChange = ["before-acl-change"];

        /// <summary>Events retained by an unaffected subscriber after an ACL revocation.</summary>
        private static readonly string[] s_afterAclRevocation = ["before-acl-change", "after-acl-revocation"];

        /// <summary>Events admitted to the revoked subscriber before and after an ACL regrant.</summary>
        private static readonly string[] s_aclRegrantedSubscriber = ["before-acl-change", "after-acl-regrant"];

        /// <summary>All events retained by the unaffected subscriber during ACL reconciliation.</summary>
        private static readonly string[] s_aclUnchangedSubscriber =
            ["before-acl-change", "after-acl-revocation", "after-acl-regrant"];

        /// <summary>The event whose first receiver's authorization was suspended.</summary>
        private static readonly string[] s_inFlightApproval = ["in-flight-approval"];

        /// <summary>The first event admitted after the suspended receiver regains permission.</summary>
        private static readonly string[] s_currentApproval = ["current-approval"];

        /// <summary>Both events retained by the independent subscriber across the suspended validation.</summary>
        private static readonly string[] s_inFlightAndCurrent = ["in-flight-approval", "current-approval"];

        /// <summary>The event admitted under the original principal.</summary>
        private static readonly string[] s_aliceApproved = ["alice-approved"];

        /// <summary>Events retained by the independent subscriber while the first principal changes.</summary>
        private static readonly string[] s_malloryDenied = ["alice-approved", "mallory-denied"];

        /// <summary>Events admitted to the first subscriber before and after restoring its original principal.</summary>
        private static readonly string[] s_aliceRestored = ["alice-approved", "alice-restored"];

        /// <summary>The event retained despite the other receiver's queue failure.</summary>
        private static readonly string[] s_independentReceiver = ["independent-receiver"];

        /// <summary>
        /// Composes real session, role, event-permission, and fanout code over the existing socket-free seams.
        /// </summary>
        private sealed class EventHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates two distinct modeled users with independent roles on one fixed event type and source.
            /// </summary>
            public EventHarness(bool serverNotifier)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_server = DeterministicServerMock.Create(out m_queues);
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry);
                m_server.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
                m_server.SetupGet(value => value.IdentityRegistry).Returns(new ServerIdentityRegistry(
                    new UserNamePasswordAuthenticator((handler, _) =>
                        new ValueTask<IUserIdentity>(new UserIdentity(handler)))));
                m_roles = new RoleManager();
                m_server.SetupGet(value => value.RoleManager).Returns(m_roles);
                SetAliceRole(true);
                Assert.That(m_roles.AddIdentity(ObjectIds.WellKnownRole_Operator, new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "bob"
                }).StatusCode, Is.EqualTo(StatusCodes.Good));
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1_000,
                        MaxSessionTimeout = 60_000,
                        MaxSessionCount = 10
                    }
                };
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                int nextSession = 0;
                diagnostics.Setup(value => value.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeId>(new NodeId((uint)Interlocked.Increment(ref nextSession), 1)));
                m_server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
                m_sessions = new SessionManager(m_server.Object, configuration);
                m_server.SetupGet(value => value.SessionManager).Returns(m_sessions);
                m_nodes = new TestableAsyncCustomNodeManager(
                    m_server.Object, configuration, false, NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri);
                Mock.Get(m_server.Object.NodeManager)
                    .Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, CancellationToken>((id, _) =>
                    {
                        NodeState? node = m_nodes.Find(id);
                        return new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                            node == null ? (null, null) : (new NodeHandle(id, node), m_nodes));
                    });
                ArrayOf<RolePermissionType> permissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)PermissionType.ReceiveEvents
                    },
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Operator,
                        Permissions = (uint)PermissionType.ReceiveEvents
                    }
                ];
                EventType = new BaseObjectTypeState
                {
                    NodeId = new NodeId("RestrictedEventType", m_nodes.NamespaceIndex),
                    BrowseName = new QualifiedName("RestrictedEventType", m_nodes.NamespaceIndex),
                    RolePermissions = permissions
                };
                Source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("RestrictedSource", m_nodes.NamespaceIndex),
                    BrowseName = new QualifiedName("RestrictedSource", m_nodes.NamespaceIndex),
                    RolePermissions = permissions,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                m_notifier = new BaseObjectState(null)
                {
                    NodeId = serverNotifier ? ObjectIds.Server : new NodeId("Notifier", m_nodes.NamespaceIndex)
                };
                var deliveryManager = new Mock<IAsyncNodeManager>();
                deliveryManager.Setup(value => value.ValidateEventRolePermissionsAsync(
                        It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IEventMonitoredItem item, IFilterTarget target, CancellationToken ct) =>
                    {
                        ServiceResult result = await m_nodes.ValidateEventRolePermissionsAsync(item, target, ct)
                            .ConfigureAwait(false);
                        if (item.Id == 1 && BlockAliceValidation && Interlocked.Exchange(ref m_blocked, 1) == 0)
                        {
                            ValidationEntered.TrySetResult(true);
                            await ReleaseValidation.Task.WaitAsync(ct).ConfigureAwait(false);
                        }
                        return result;
                    });
                m_monitored = new MonitoredNode2(deliveryManager.Object, m_server.Object, m_notifier);
                m_certificate = CertificateBuilder.Create("CN=Security F44 Test").SetRSAKeySize(2048).CreateForRSA();
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost/security-f44",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "username",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
                m_channel = new SecureChannelContext("security-f44", endpoint, RequestEncoding.Binary);
            }

            /// <summary>
            /// Gets the subscriber whose Observer mapping can be revoked.
            /// </summary>
            public CreateSessionResult Alice { get; private set; } = null!;

            /// <summary>
            /// Gets the independently authorized Operator subscriber.
            /// </summary>
            public CreateSessionResult Bob { get; private set; } = null!;

            /// <summary>
            /// Gets the first receiver for per-item failure injection.
            /// </summary>
            public Mock<IEventMonitoredItem> AliceReceiver { get; private set; } = null!;

            /// <summary>
            /// Gets the independently authorized receiver for permission-capture race injection.
            /// </summary>
            public Mock<IEventMonitoredItem> BobReceiver { get; private set; } = null!;

            /// <summary>
            /// Gets the exact fields delivered to the first receiver.
            /// </summary>
            public ConcurrentQueue<string> AliceEvents { get; } = new();

            /// <summary>
            /// Gets the exact fields delivered to the unaffected receiver.
            /// </summary>
            public ConcurrentQueue<string> BobEvents { get; } = new();

            /// <summary>
            /// Gets the fixed event type whose ACL can change independently of the notifier.
            /// </summary>
            public BaseObjectTypeState EventType { get; }

            /// <summary>
            /// Gets the fixed source whose ACL can change independently of the notifier.
            /// </summary>
            public BaseObjectState Source { get; }

            /// <summary>
            /// Gets or sets whether the first real permission result is suspended before it can be queued.
            /// </summary>
            public bool BlockAliceValidation { get; set; }

            /// <summary>
            /// Signals that the suspended permission check has already computed its result.
            /// </summary>
            public TaskCompletionSource<bool> ValidationEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Releases the suspended permission check after a modeled role change.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseValidation { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Registers the ACL-bearing nodes and creates and activates two real sessions.
            /// </summary>
            public async ValueTask InitializeAsync()
            {
                await m_nodes.AddPredefinedNodePublicAsync(m_nodes.SystemContext, EventType).ConfigureAwait(false);
                await m_nodes.AddNodeAsync(m_nodes.SystemContext, NodeId.Null, Source).ConfigureAwait(false);
                Alice = await CreateSessionAsync("alice").ConfigureAwait(false);
                Bob = await CreateSessionAsync("bob").ConfigureAwait(false);
                AliceReceiver = CreateReceiver(1, Alice.Session, AliceEvents);
                BobReceiver = CreateReceiver(2, Bob.Session, BobEvents);
                m_monitored.Add(AliceReceiver.Object);
                m_monitored.Add(BobReceiver.Object);
                await PublishAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Keeps the real username/session path while giving the physical modeled channel TLS confidentiality.
            /// </summary>
            public void UseConfidentialTransport()
            {
                m_channel.EndpointDescription!.EndpointUrl = "https://localhost/security-r36";
                m_channel.EndpointDescription.TransportProfileUri = Profiles.HttpsBinaryTransport;
            }

            /// <summary>
            /// Changes the identity mapping through the real RoleManager and its normal session-staleness event.
            /// </summary>
            public void SetAliceRole(bool grant)
            {
                var rule = new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                };
                ServiceResult result = grant
                    ? m_roles.AddIdentity(ObjectIds.WellKnownRole_Observer, rule)
                    : m_roles.RemoveIdentity(ObjectIds.WellKnownRole_Observer, rule);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            }

            /// <summary>
            /// Runs actual Publish request admission so lazy live-role reconciliation is exercised.
            /// </summary>
            public async ValueTask PublishAsync()
            {
                foreach (CreateSessionResult created in new[] { Alice, Bob })
                {
                    using OperationContext context = await m_sessions.ValidateRequestAsync(
                        new RequestHeader
                        {
                            AuthenticationToken = created.AuthenticationToken,
                            Timestamp = DateTimeUtc.Now
                        }, m_channel, RequestType.Publish, RequestLifetime.None).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Reactivates a session with a new principal without manually clearing any monitored-node cache.
            /// </summary>
            public async ValueTask ActivateAsync(CreateSessionResult created, string userName)
            {
                using var context = new OperationContext(
                    new RequestHeader(), m_channel, RequestType.ActivateSession, RequestLifetime.None);
                var token = new UserNameIdentityToken
                {
                    PolicyId = "username",
                    UserName = userName,
                    Password = new ByteString(new byte[] { 1 })
                };
                await m_sessions.ActivateSessionAsync(
                    context, created.AuthenticationToken, new SignatureData(), new ExtensionObject(token),
                    new SignatureData(), []).ConfigureAwait(false);
            }

            /// <summary>
            /// Reports a new payload on the exact same item, type, and source tuples and observes completed fanout.
            /// </summary>
            public async ValueTask ReportAsync(string message)
            {
                await EnqueueAsync(message).ConfigureAwait(false);
                await WaitForDeliveryAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Enqueues a fully modeled event without waiting for an intentionally suspended consumer.
            /// </summary>
            public ValueTask EnqueueAsync(string message)
            {
                var evt = new BaseEventState(null);
                evt.EventType = new PropertyState<NodeId>.Implementation<VariantBuilder>(evt)
                {
                    Value = EventType.NodeId
                };
                evt.SourceNode = new PropertyState<NodeId>.Implementation<VariantBuilder>(evt)
                {
                    Value = Source.NodeId
                };
                evt.Message = new PropertyState<LocalizedText>.Implementation<VariantBuilder>(evt)
                {
                    Value = new LocalizedText(message)
                };
                return m_monitored.OnReportEventAsync(m_server.Object.DefaultSystemContext, m_notifier, evt);
            }

            /// <summary>
            /// Fences completed delivery without sleeps or changing the event's authorization inputs.
            /// </summary>
            public async ValueTask WaitForDeliveryAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await m_monitored.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Releases gates and drains notifications before closing sessions, nodes, queues, and certificate handles.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                ReleaseValidation.TrySetResult(true);
                await m_monitored.DisposeAsync().ConfigureAwait(false);
                foreach (ISession session in m_sessions.GetSessions())
                {
                    await m_sessions.CloseSessionAsync(session.Id).ConfigureAwait(false);
                }
                await m_nodes.DisposeAsync().ConfigureAwait(false);
                m_sessions.Dispose();
                m_roles.Dispose();
                m_certificate.Dispose();
                m_queues.Dispose();
            }

            /// <summary>
            /// Observes real queue callbacks while retaining the subscriber's current session and effective identity.
            /// </summary>
            private static Mock<IEventMonitoredItem> CreateReceiver(
                uint id, ISession session, ConcurrentQueue<string> events)
            {
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(id);
                item.SetupGet(value => value.Session).Returns(session);
                item.SetupGet(value => value.EffectiveIdentity).Returns(() => session.EffectiveIdentity);
                item.Setup(value => value.QueueEvent(It.IsAny<IFilterTarget>()))
                    .Callback<IFilterTarget>(target => events.Enqueue(((BaseEventState)target).Message!.Value.Text!));
                return item;
            }

            /// <summary>
            /// Creates an independently managed session and authenticates its modeled user.
            /// </summary>
            private async ValueTask<CreateSessionResult> CreateSessionAsync(string userName)
            {
                using var context = new OperationContext(
                    new RequestHeader(), m_channel, RequestType.CreateSession, RequestLifetime.None);
                CreateSessionResult created = await m_sessions.CreateSessionAsync(
                    context, m_certificate, userName, default, new ApplicationDescription(),
                    m_channel.EndpointDescription!.EndpointUrl, null, [], 60_000, 65_536).ConfigureAwait(false);
                await ActivateAsync(created, userName).ConfigureAwait(false);
                return created;
            }

            /// <summary>
            /// Supplies in-memory server services.
            /// </summary>
            private readonly Mock<IServerInternal> m_server;

            /// <summary>
            /// Owns the deterministic server's queue factory.
            /// </summary>
            private readonly MonitoredItemQueueFactory m_queues;

            /// <summary>
            /// Resolves the actual live role mappings.
            /// </summary>
            private readonly RoleManager m_roles;

            /// <summary>
            /// Owns and reconciles the actual subscriber sessions.
            /// </summary>
            private readonly SessionManager m_sessions;

            /// <summary>
            /// Evaluates actual event-type and source-node permissions.
            /// </summary>
            private readonly TestableAsyncCustomNodeManager m_nodes;

            /// <summary>
            /// Identifies the notifier independently from the event's ACL-bearing source.
            /// </summary>
            private readonly BaseObjectState m_notifier;

            /// <summary>
            /// Runs the production notification consumer and per-item permission cache.
            /// </summary>
            private readonly MonitoredNode2 m_monitored;

            /// <summary>
            /// Owns the in-memory certificate referenced by the real sessions.
            /// </summary>
            private readonly Certificate m_certificate;

            /// <summary>
            /// Models request and activation admission without a listening socket.
            /// </summary>
            private readonly SecureChannelContext m_channel;

            /// <summary>
            /// Ensures the permission gate suspends exactly one result.
            /// </summary>
            private int m_blocked;
        }
    }
}
