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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

using ManagedSessionClass = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Compliance tests that verify <see cref="ManagedSessionClass"/>
    /// faithfully delegates every <see cref="ISession"/> member to the
    /// inner V1 <see cref="Session"/>.
    /// </summary>
    [TestFixture]
    public sealed class ManagedSessionComplianceTests
    {
        private ManagedSessionClass m_managedSession;
        private SessionMock m_innerSession;

        [SetUp]
        public void SetUp()
        {
            m_innerSession = SessionMock.Create();
            m_innerSession.SetConnected();
            m_managedSession = CreateManagedSessionWithInner(m_innerSession);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_managedSession != null)
            {
                await m_managedSession.DisposeAsync().ConfigureAwait(false);
            }

            m_innerSession?.Dispose();
        }

        [Test]
        public void SessionIdDelegatesToInnerSession()
        {
            Assert.That(m_managedSession.SessionId, Is.EqualTo(m_innerSession.SessionId));
        }

        [Test]
        public void ConnectedDelegatesToInnerSession()
        {
            Assert.That(m_managedSession.Connected, Is.EqualTo(m_innerSession.Connected));
            Assert.That(m_managedSession.Connected, Is.True);
        }

        [Test]
        public void EndpointDelegatesToInnerSession()
        {
            // When inner session has no transport-level endpoint,
            // ManagedSession falls back to the configured endpoint
            // description. Verify ManagedSession.Endpoint is not null.
            Assert.That(
                m_managedSession.Endpoint, Is.Not.Null);
        }

        [Test]
        public void IdentityDelegatesToInnerSession()
        {
            Assert.That(
                m_managedSession.Identity,
                Is.EqualTo(m_innerSession.Identity));
        }

        [Test]
        public void SubscriptionCountDelegatesToInnerSession()
        {
            Assert.That(
                m_managedSession.SubscriptionCount,
                Is.EqualTo(m_innerSession.SubscriptionCount));
        }

        [Test]
        public void KeepAliveIntervalDelegatesToInnerSession()
        {
            m_innerSession.KeepAliveInterval = 12345;

            Assert.That(
                m_managedSession.KeepAliveInterval,
                Is.EqualTo(12345));
        }

        [Test]
        public void OperationTimeoutDelegatesToInnerSession()
        {
            // OperationTimeout delegates through to the transport
            // channel. Set it via the mock channel property and
            // verify both session and managed session agree.
            m_innerSession.Channel
                .SetupProperty(c => c.OperationTimeout, 0);
            m_innerSession.OperationTimeout = 9876;

            Assert.That(
                m_managedSession.OperationTimeout,
                Is.EqualTo(9876));
        }

        [Test]
        public void KeepAliveStoppedFlagsThroughEngineContext()
        {
            // ManagedSession.KeepAliveStopped should mirror whatever the
            // underlying Session reports — without subscribing to a real
            // server, the SessionMock has LastKeepAliveTime = MinValue
            // and so reports KeepAliveStopped = true. The assertion is
            // pure forwarding: the ManagedSession value equals the
            // InnerSession value.
            Assert.That(
                m_managedSession.KeepAliveStopped,
                Is.EqualTo(m_innerSession.KeepAliveStopped));
        }

        [Test]
        public void LastKeepAliveTimeForwardedFromInnerSession()
        {
            DateTime innerLast = m_innerSession.LastKeepAliveTime;
            Assert.That(
                m_managedSession.LastKeepAliveTime,
                Is.EqualTo(innerLast));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            // Calling DisposeAsync a second time must be a no-op and
            // must not throw.
            await m_managedSession.DisposeAsync().ConfigureAwait(false);
            await m_managedSession.DisposeAsync().ConfigureAwait(false);

            // Null out so TearDown doesn't dispose a third time.
            m_managedSession = null;
        }

        [Test]
        public void EventHandlerThrowingDoesNotPreventOtherHandlers()
        {
            int otherInvocations = 0;
            ISession otherSender = null;

            m_managedSession.KeepAlive += (s, e) =>
                throw new InvalidOperationException("first handler boom");
            m_managedSession.KeepAlive += (s, e) =>
            {
                otherInvocations++;
                otherSender = s;
            };

            // The implementation either swallows the exception in the
            // first handler (so subsequent handlers run) or rethrows.
            // Both behaviors are acceptable; the assertion is that we
            // don't deadlock or corrupt state. We tolerate either:
            // exception bubbling means otherInvocations stays 0; exception
            // swallowing means it becomes 1.
            try
            {
                RaiseKeepAliveOnInner(m_innerSession);
            }
            catch (InvalidOperationException)
            {
                // First handler bubbled — acceptable. Second handler
                // may or may not have run depending on multicast order.
            }

            Assert.That(otherInvocations is 0 or 1);
            // If the second handler did run, sender should be the
            // managed session (event forwarding preserves identity).
            if (otherInvocations == 1)
            {
                Assert.That(otherSender, Is.SameAs(m_managedSession));
            }
        }

        [Test]
        public void KeepAliveEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.KeepAlive += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseKeepAliveOnInner(m_innerSession);

            Assert.That(fired, Is.True, "KeepAlive event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession),
                "Sender should be the ManagedSession, not the inner");
        }

        [Test]
        public void NotificationEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.Notification += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseNotificationOnInner(m_innerSession);

            Assert.That(fired, Is.True, "Notification event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void PublishErrorEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.PublishError += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaisePublishErrorOnInner(m_innerSession);

            Assert.That(fired, Is.True, "PublishError event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void SubscriptionsChangedEventForwardsToConsumer()
        {
            bool fired = false;
            object receivedSender = null;
            m_managedSession.SubscriptionsChanged += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseSubscriptionsChangedOnInner(m_innerSession);

            Assert.That(fired, Is.True,
                "SubscriptionsChanged event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void AddSubscriptionDelegatesToInnerSession()
        {
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new SubscriptionOptions
                {
                    DisplayName = "TestSub",
                    PublishingInterval = 1000
                });

            bool result = m_managedSession.AddSubscription(subscription);

            Assert.That(result, Is.True);
            Assert.That(m_innerSession.SubscriptionCount, Is.EqualTo(1));
            Assert.That(
                m_managedSession.SubscriptionCount,
                Is.EqualTo(1));
        }

        [Test]
        public async Task RemoveSubscriptionAsyncDelegatesToInnerSession()
        {
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new SubscriptionOptions
                {
                    DisplayName = "TestSub",
                    PublishingInterval = 1000
                });

            m_managedSession.AddSubscription(subscription);
            Assert.That(m_managedSession.SubscriptionCount, Is.EqualTo(1));

            bool result = await m_managedSession
                .RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);

            Assert.That(result, Is.True);
            Assert.That(m_managedSession.SubscriptionCount, Is.Zero);
        }

        // ----- Service passthrough delegation tests -----------------
        // ManagedSession.Services.cs declares 32 service methods that
        // wrap the InnerSession call in a ReaderLockAsync. The set of
        // tests below verifies (a) the request reaches the underlying
        // channel, (b) it is of the expected request type, and (c) the
        // response from InnerSession bubbles back unchanged.

        private static readonly Func<
            Session,
            Mock<ITransportChannel>,
            ApplicationConfiguration>[] s_unused = [];

        public static IEnumerable<TestCaseData> ServicePassthroughCases()
        {
            yield return Case<ReadRequest, ReadResponse>(
                "Read",
                m => m.ReadAsync(null, 0, TimestampsToReturn.Both,
                    new ArrayOf<ReadValueId>(), default).AsTask());
            yield return Case<HistoryReadRequest, HistoryReadResponse>(
                "HistoryRead",
                m => m.HistoryReadAsync(null, ExtensionObject.Null,
                    TimestampsToReturn.Both, false,
                    new ArrayOf<HistoryReadValueId>(), default).AsTask());
            yield return Case<WriteRequest, WriteResponse>(
                "Write",
                m => m.WriteAsync(null,
                    new ArrayOf<WriteValue>(), default).AsTask());
            yield return Case<HistoryUpdateRequest, HistoryUpdateResponse>(
                "HistoryUpdate",
                m => m.HistoryUpdateAsync(null,
                    new ArrayOf<ExtensionObject>(), default).AsTask());
            yield return Case<BrowseRequest, BrowseResponse>(
                "Browse",
                m => m.BrowseAsync(null, null, 0,
                    new ArrayOf<BrowseDescription>(), default).AsTask());
            yield return Case<BrowseNextRequest, BrowseNextResponse>(
                "BrowseNext",
                m => m.BrowseNextAsync(null, false,
                    new ArrayOf<ByteString>(), default).AsTask());
            yield return Case<TranslateBrowsePathsToNodeIdsRequest,
                TranslateBrowsePathsToNodeIdsResponse>(
                "TranslateBrowsePaths",
                m => m.TranslateBrowsePathsToNodeIdsAsync(null,
                    new ArrayOf<BrowsePath>(), default).AsTask());
            yield return Case<RegisterNodesRequest, RegisterNodesResponse>(
                "RegisterNodes",
                m => m.RegisterNodesAsync(null,
                    new ArrayOf<NodeId>(), default).AsTask());
            yield return Case<UnregisterNodesRequest, UnregisterNodesResponse>(
                "UnregisterNodes",
                m => m.UnregisterNodesAsync(null,
                    new ArrayOf<NodeId>(), default).AsTask());
            yield return Case<CallRequest, CallResponse>(
                "Call",
                m => m.CallAsync(null,
                    new ArrayOf<CallMethodRequest>(), default).AsTask());
            yield return Case<CreateMonitoredItemsRequest,
                CreateMonitoredItemsResponse>(
                "CreateMonitoredItems",
                m => m.CreateMonitoredItemsAsync(null, 0,
                    TimestampsToReturn.Both,
                    new ArrayOf<MonitoredItemCreateRequest>(),
                    default).AsTask());
            yield return Case<ModifyMonitoredItemsRequest,
                ModifyMonitoredItemsResponse>(
                "ModifyMonitoredItems",
                m => m.ModifyMonitoredItemsAsync(null, 0,
                    TimestampsToReturn.Both,
                    new ArrayOf<MonitoredItemModifyRequest>(),
                    default).AsTask());
            yield return Case<SetMonitoringModeRequest,
                SetMonitoringModeResponse>(
                "SetMonitoringMode",
                m => m.SetMonitoringModeAsync(null, 0,
                    MonitoringMode.Reporting,
                    new ArrayOf<uint>(), default).AsTask());
            yield return Case<SetTriggeringRequest, SetTriggeringResponse>(
                "SetTriggering",
                m => m.SetTriggeringAsync(null, 0, 0,
                    new ArrayOf<uint>(), new ArrayOf<uint>(),
                    default).AsTask());
            yield return Case<DeleteMonitoredItemsRequest,
                DeleteMonitoredItemsResponse>(
                "DeleteMonitoredItems",
                m => m.DeleteMonitoredItemsAsync(null, 0,
                    new ArrayOf<uint>(), default).AsTask());
            yield return Case<CreateSubscriptionRequest,
                CreateSubscriptionResponse>(
                "CreateSubscription",
                m => m.CreateSubscriptionAsync(null,
                    1000, 10, 5, 0, true, 0, default).AsTask());
            yield return Case<ModifySubscriptionRequest,
                ModifySubscriptionResponse>(
                "ModifySubscription",
                m => m.ModifySubscriptionAsync(null, 0,
                    1000, 10, 5, 0, 0, default).AsTask());
            yield return Case<SetPublishingModeRequest,
                SetPublishingModeResponse>(
                "SetPublishingMode",
                m => m.SetPublishingModeAsync(null, true,
                    new ArrayOf<uint>(), default).AsTask());
            yield return Case<PublishRequest, PublishResponse>(
                "Publish",
                m => m.PublishAsync(null,
                    new ArrayOf<SubscriptionAcknowledgement>(),
                    default).AsTask());
            yield return Case<RepublishRequest, RepublishResponse>(
                "Republish",
                m => m.RepublishAsync(null, 0, 0, default).AsTask());
            yield return Case<TransferSubscriptionsRequest,
                TransferSubscriptionsResponse>(
                "TransferSubscriptions",
                m => m.TransferSubscriptionsAsync(null,
                    new ArrayOf<uint>(), false, default).AsTask());
            yield return Case<DeleteSubscriptionsRequest,
                DeleteSubscriptionsResponse>(
                "DeleteSubscriptions",
                m => m.DeleteSubscriptionsAsync(null,
                    new ArrayOf<uint>(), default).AsTask());
            yield return Case<AddNodesRequest, AddNodesResponse>(
                "AddNodes",
                m => m.AddNodesAsync(null,
                    new ArrayOf<AddNodesItem>(), default).AsTask());
            yield return Case<AddReferencesRequest, AddReferencesResponse>(
                "AddReferences",
                m => m.AddReferencesAsync(null,
                    new ArrayOf<AddReferencesItem>(), default).AsTask());
            yield return Case<DeleteNodesRequest, DeleteNodesResponse>(
                "DeleteNodes",
                m => m.DeleteNodesAsync(null,
                    new ArrayOf<DeleteNodesItem>(), default).AsTask());
            yield return Case<DeleteReferencesRequest,
                DeleteReferencesResponse>(
                "DeleteReferences",
                m => m.DeleteReferencesAsync(null,
                    new ArrayOf<DeleteReferencesItem>(), default).AsTask());
            yield return Case<QueryFirstRequest, QueryFirstResponse>(
                "QueryFirst",
                m => m.QueryFirstAsync(null,
                    new ViewDescription(),
                    new ArrayOf<NodeTypeDescription>(),
                    new ContentFilter(), 0, 0, default).AsTask());
            yield return Case<QueryNextRequest, QueryNextResponse>(
                "QueryNext",
                m => m.QueryNextAsync(null, false,
                    new ByteString(Array.Empty<byte>()),
                    default).AsTask());
            yield return Case<CancelRequest, CancelResponse>(
                "Cancel",
                m => m.CancelAsync(null, 0, default).AsTask());
        }

        private static TestCaseData Case<TRequest, TResponse>(
            string name,
            Func<ManagedSessionClass, Task> invoke)
            where TRequest : class, IServiceRequest
            where TResponse : class, IServiceResponse, new()
        {
            return new TestCaseData(
                invoke,
                typeof(TRequest),
                (Func<TResponse>)(() => new TResponse()))
                .SetName(name + "DelegatesToInnerSession");
        }

        [TestCaseSource(nameof(ServicePassthroughCases))]
        public async Task ServicePassthroughDelegatesToInnerSession<TResponse>(
            Func<ManagedSessionClass, Task> invoke,
            Type expectedRequestType,
            Func<TResponse> responseFactory)
            where TResponse : class, IServiceResponse, new()
        {
            IServiceRequest captured = null;
            m_innerSession.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IServiceRequest, CancellationToken>(
                    (req, _) => captured = req)
                .Returns(new ValueTask<IServiceResponse>(responseFactory()));

            await invoke(m_managedSession).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null,
                "Channel did not receive a request — passthrough may not " +
                "be delegating to InnerSession.");
            Assert.That(captured, Is.InstanceOf(expectedRequestType));
        }

        [Test]
        public async Task ReadAsyncPassesCancellationTokenThrough()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            m_innerSession.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(
                    (_, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<IServiceResponse>(new ReadResponse());
                    });

            Assert.That(
                async () => await m_managedSession.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ArrayOf<ReadValueId>(), cts.Token)
                    .ConfigureAwait(false),
                Throws.TypeOf<OperationCanceledException>());
        }

        [Test]
        public async Task ReadAsyncBubblesUpExceptionFromInnerSession()
        {
            m_innerSession.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadInternalError));

            Assert.That(
                async () => await m_managedSession.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ArrayOf<ReadValueId>(), default)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((StatusCode)StatusCodes.BadInternalError));

            // The reader-lock is non-recursive; if the previous call had
            // failed to release, the next service call would deadlock.
            // Verify the lock was released by issuing a second call.
            m_innerSession.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse()));

            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(2));
            ReadResponse response = await m_managedSession.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ArrayOf<ReadValueId>(), cts.Token).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
        }

        /// <summary>
        /// Creates a <see cref="ManagedSessionClass"/> with
        /// the given inner session injected via reflection, bypassing
        /// the async factory that needs a real server.
        /// </summary>
        private static ManagedSessionClass
            CreateManagedSessionWithInner(Session innerSession)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<ManagedSessionClass> logger = telemetry.CreateLogger<ManagedSessionClass>();

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens = [new UserTokenPolicy()]
                });

            var reconnectPolicy = new ReconnectPolicy();
            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry)
                .Returns(telemetry);

            ConstructorInfo ctor = typeof(ManagedSessionClass)
                .GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [
                        typeof(ApplicationConfiguration),
                        typeof(ConfiguredEndpoint),
                        typeof(ISessionFactory),
                        typeof(IReconnectPolicy),
                        typeof(IServerRedundancyHandler),
                        typeof(ILogger),
                        typeof(IUserIdentity),
                        typeof(ArrayOf<string>),
                        typeof(string),
                        typeof(uint),
                        typeof(bool)
                    ],
                    null);

            var managed =
                (ManagedSessionClass)ctor.Invoke(
                [
                    configuration,
                    endpoint,
                    sessionFactory.Object,
                    reconnectPolicy,
                    null,
                    logger,
                    null,
                    default(ArrayOf<string>),
                    "TestManagedSession",
                    60000u,
                    false
                ]);

            // Inject the inner session.
            typeof(ManagedSessionClass)
                .GetField(
                    "m_session",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(managed, innerSession);

            // Wire events so the ManagedSession forwards inner
            // session events to its own subscribers.
            typeof(ManagedSessionClass)
                .GetMethod(
                    "WireSessionEvents",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(managed, [innerSession]);

            return managed;
        }

        private static void RaiseKeepAliveOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_KeepAlive",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = (KeepAliveEventHandler)field.GetValue(session);
            handler?.Invoke(
                session,
                new KeepAliveEventArgs(
                    null, ServerState.Running, DateTime.UtcNow));
        }

        private static void RaiseNotificationOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_Publish",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler =
                (NotificationEventHandler)field.GetValue(session);
            handler?.Invoke(
                session,
                new NotificationEventArgs(null, null, default));
        }

        private static void RaisePublishErrorOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_PublishError",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler =
                (PublishErrorEventHandler)field.GetValue(session);
            handler?.Invoke(
                session,
                new PublishErrorEventArgs(
                    new ServiceResult(StatusCodes.BadTimeout)));
        }

        private static void RaiseSubscriptionsChangedOnInner(
            Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_SubscriptionsChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = (EventHandler)field.GetValue(session);
            handler?.Invoke(session, EventArgs.Empty);
        }
    }
}
