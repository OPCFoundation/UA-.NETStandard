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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Delegation and lifecycle coverage for the transparent <see cref="RedundantClientSession"/> facade. Each test
    /// injects a <see cref="Mock{ISession}"/> as the current leader session and asserts the facade forwards every
    /// <see cref="ISession"/> member to it, in addition to exercising the no-session, role-change and dispose paths.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class RedundantClientSessionFacadeTests
    {
        private ITelemetryContext m_telemetry = null!;
        private readonly List<RedundantClientSession> m_facades = [];
        private readonly List<IDisposable> m_disposables = [];

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [TearDown]
        public async Task TearDown()
        {
            foreach (RedundantClientSession facade in m_facades)
            {
                await facade.DisposeAsync().ConfigureAwait(false);
            }
            m_facades.Clear();

            foreach (IDisposable disposable in m_disposables)
            {
                disposable.Dispose();
            }
            m_disposables.Clear();
        }

        [Test]
        public void LeaderPropertyGettersDelegateToCurrentSession()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            ISessionFactory sessionFactory = Mock.Of<ISessionFactory>();
            IUserIdentity identity = Mock.Of<IUserIdentity>();
            IUserIdentity[] identityHistory = Array.Empty<IUserIdentity>();
            ISystemContext systemContext = Mock.Of<ISystemContext>();
            IEncodeableFactory factory = Mock.Of<IEncodeableFactory>();
            ITypeTable typeTree = Mock.Of<ITypeTable>();
            INodeCache nodeCache = Mock.Of<INodeCache>();
            IFilterContext filterContext = Mock.Of<IFilterContext>();
            IServiceMessageContext messageContext = Mock.Of<IServiceMessageContext>();
            ITransportChannel nullableChannel = Mock.Of<ITransportChannel>();
            ITransportChannel transportChannel = Mock.Of<ITransportChannel>();
            Subscription[] subscriptions = Array.Empty<Subscription>();
            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var sessionId = new NodeId(42u);
            object handle = new();
            var lastKeepAlive = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            session.SetupGet(s => s.SessionFactory).Returns(sessionFactory);
            session.SetupGet(s => s.SessionName).Returns("session-name");
            session.SetupGet(s => s.SessionTimeout).Returns(1234.5);
            session.SetupGet(s => s.Handle).Returns(handle);
            session.SetupGet(s => s.Identity).Returns(identity);
            session.SetupGet(s => s.IdentityHistory).Returns(identityHistory);
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(s => s.ServerUris).Returns(serverUris);
            session.SetupGet(s => s.SystemContext).Returns(systemContext);
            session.SetupGet(s => s.Factory).Returns(factory);
            session.SetupGet(s => s.TypeTree).Returns(typeTree);
            session.SetupGet(s => s.NodeCache).Returns(nodeCache);
            session.SetupGet(s => s.FilterContext).Returns(filterContext);
            session.SetupGet(s => s.Subscriptions).Returns(subscriptions);
            session.SetupGet(s => s.SubscriptionCount).Returns(7);
            session.SetupGet(s => s.DeleteSubscriptionsOnClose).Returns(true);
            session.SetupGet(s => s.PublishRequestCancelDelayOnCloseSession).Returns(11);
            session.SetupGet(s => s.KeepAliveInterval).Returns(2222);
            session.SetupGet(s => s.KeepAliveStopped).Returns(true);
            session.SetupGet(s => s.LastKeepAliveTime).Returns(lastKeepAlive);
            session.SetupGet(s => s.LastKeepAliveTimestamp).Returns(999L);
            session.SetupGet(s => s.OutstandingRequestCount).Returns(3);
            session.SetupGet(s => s.DefunctRequestCount).Returns(4);
            session.SetupGet(s => s.GoodPublishRequestCount).Returns(5);
            session.SetupGet(s => s.MinPublishRequestCount).Returns(6);
            session.SetupGet(s => s.MaxPublishRequestCount).Returns(8);
            session.SetupGet(s => s.Reconnecting).Returns(true);
            session.SetupGet(s => s.TransferSubscriptionsOnReconnect).Returns(true);
            session.SetupGet(s => s.CheckDomain).Returns(true);
            session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Balanced);
            session.SetupGet(s => s.SessionId).Returns(sessionId);
            session.SetupGet(s => s.Connected).Returns(true);
            session.SetupGet(s => s.ActivityTraceFlags).Returns(ClientTraceFlags.Metrics);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NullableTransportChannel).Returns(nullableChannel);
            session.SetupGet(s => s.TransportChannel).Returns(transportChannel);
            session.SetupGet(s => s.ReturnDiagnostics).Returns(DiagnosticsMasks.ServiceSymbolicId);
            session.SetupGet(s => s.OperationTimeout).Returns(30000);
            session.SetupGet(s => s.DefaultTimeoutHint).Returns(12345);

            Assert.That(facade.SessionFactory, Is.SameAs(sessionFactory));
            Assert.That(facade.SessionName, Is.EqualTo("session-name"));
            Assert.That(facade.SessionTimeout, Is.EqualTo(1234.5));
            Assert.That(facade.Handle, Is.SameAs(handle));
            Assert.That(facade.Identity, Is.SameAs(identity));
            Assert.That(facade.IdentityHistory, Is.SameAs(identityHistory));
            Assert.That(facade.NamespaceUris, Is.SameAs(namespaceUris));
            Assert.That(facade.ServerUris, Is.SameAs(serverUris));
            Assert.That(facade.SystemContext, Is.SameAs(systemContext));
            Assert.That(facade.Factory, Is.SameAs(factory));
            Assert.That(facade.TypeTree, Is.SameAs(typeTree));
            Assert.That(facade.NodeCache, Is.SameAs(nodeCache));
            Assert.That(facade.FilterContext, Is.SameAs(filterContext));
            Assert.That(facade.Subscriptions, Is.SameAs(subscriptions));
            Assert.That(facade.SubscriptionCount, Is.EqualTo(7));
            Assert.That(facade.DeleteSubscriptionsOnClose, Is.True);
            Assert.That(facade.PublishRequestCancelDelayOnCloseSession, Is.EqualTo(11));
            Assert.That(facade.KeepAliveInterval, Is.EqualTo(2222));
            Assert.That(facade.KeepAliveStopped, Is.True);
            Assert.That(facade.LastKeepAliveTime, Is.EqualTo(lastKeepAlive));
            Assert.That(facade.LastKeepAliveTimestamp, Is.EqualTo(999L));
            Assert.That(facade.OutstandingRequestCount, Is.EqualTo(3));
            Assert.That(facade.DefunctRequestCount, Is.EqualTo(4));
            Assert.That(facade.GoodPublishRequestCount, Is.EqualTo(5));
            Assert.That(facade.MinPublishRequestCount, Is.EqualTo(6));
            Assert.That(facade.MaxPublishRequestCount, Is.EqualTo(8));
            Assert.That(facade.Reconnecting, Is.True);
            Assert.That(facade.TransferSubscriptionsOnReconnect, Is.True);
            Assert.That(facade.CheckDomain, Is.True);
            Assert.That(facade.ContinuationPointPolicy, Is.EqualTo(ContinuationPointPolicy.Balanced));
            Assert.That(facade.SessionId, Is.EqualTo(sessionId));
            Assert.That(facade.Connected, Is.True);
            Assert.That(facade.ActivityTraceFlags, Is.EqualTo(ClientTraceFlags.Metrics));
            Assert.That(facade.MessageContext, Is.SameAs(messageContext));
            Assert.That(facade.NullableTransportChannel, Is.SameAs(nullableChannel));
            Assert.That(facade.TransportChannel, Is.SameAs(transportChannel));
            Assert.That(facade.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));
            Assert.That(facade.OperationTimeout, Is.EqualTo(30000));
            Assert.That(facade.DefaultTimeoutHint, Is.EqualTo(12345));

            // Reference-typed members whose concrete instances are awkward to fabricate are asserted by delegation.
            _ = facade.ConfiguredEndpoint;
            _ = facade.PreferredLocales;
            _ = facade.DefaultSubscription;
            _ = facade.OperationLimits;
            _ = facade.ServerCapabilities;
            _ = facade.Endpoint;
            _ = facade.EndpointConfiguration;
            session.VerifyGet(s => s.ConfiguredEndpoint, Times.Once());
            session.VerifyGet(s => s.PreferredLocales, Times.Once());
            session.VerifyGet(s => s.DefaultSubscription, Times.Once());
            session.VerifyGet(s => s.OperationLimits, Times.Once());
            session.VerifyGet(s => s.ServerCapabilities, Times.Once());
            session.VerifyGet(s => s.Endpoint, Times.Once());
            session.VerifyGet(s => s.EndpointConfiguration, Times.Once());
        }

        [Test]
        public void LeaderPropertySettersDelegateToCurrentSession()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            facade.DeleteSubscriptionsOnClose = true;
            facade.PublishRequestCancelDelayOnCloseSession = 11;
            facade.KeepAliveInterval = 2222;
            facade.MinPublishRequestCount = 6;
            facade.MaxPublishRequestCount = 8;
            facade.TransferSubscriptionsOnReconnect = true;
            facade.ContinuationPointPolicy = ContinuationPointPolicy.Balanced;
            facade.ActivityTraceFlags = ClientTraceFlags.Metrics;
            facade.ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId;
            facade.OperationTimeout = 30000;
            facade.DefaultTimeoutHint = 12345;
            facade.DefaultSubscription = null!;

            session.VerifySet(s => s.DeleteSubscriptionsOnClose = true, Times.Once());
            session.VerifySet(s => s.PublishRequestCancelDelayOnCloseSession = 11, Times.Once());
            session.VerifySet(s => s.KeepAliveInterval = 2222, Times.Once());
            session.VerifySet(s => s.MinPublishRequestCount = 6, Times.Once());
            session.VerifySet(s => s.MaxPublishRequestCount = 8, Times.Once());
            session.VerifySet(s => s.TransferSubscriptionsOnReconnect = true, Times.Once());
            session.VerifySet(s => s.ContinuationPointPolicy = ContinuationPointPolicy.Balanced, Times.Once());
            session.VerifySet(s => s.ActivityTraceFlags = ClientTraceFlags.Metrics, Times.Once());
            session.VerifySet(s => s.ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId, Times.Once());
            session.VerifySet(s => s.OperationTimeout = 30000, Times.Once());
            session.VerifySet(s => s.DefaultTimeoutHint = 12345, Times.Once());
            session.VerifySet(s => s.DefaultSubscription = It.IsAny<Subscription>(), Times.Once());

            // The Handle setter also remembers the value and is a no-op for a non-managed session.
            Assert.That(() => facade.Handle = new object(), Throws.Nothing);
        }

        [Test]
        public void LeaderSyncMethodsDelegateToCurrentSession()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            Subscription[] loaded = Array.Empty<Subscription>();
            var savedConfiguration = new SessionConfiguration();
            session.Setup(s => s.Load(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<IEnumerable<Type>?>()))
                .Returns(loaded);
            session.Setup(s => s.SaveSessionConfiguration(It.IsAny<Stream?>())).Returns(savedConfiguration);
            session.Setup(s => s.ApplySessionConfiguration(It.IsAny<SessionConfiguration>())).Returns(true);
            session.Setup(s => s.AddSubscription(It.IsAny<Subscription>())).Returns(true);
            session.Setup(s => s.RemoveTransferredSubscription(It.IsAny<Subscription>())).Returns(true);
            session.Setup(s => s.BeginPublish(It.IsAny<int>())).Returns(true);
            session.Setup(s => s.NewRequestHandle()).Returns(4242u);

            facade.Save(Stream.Null, [], null);
            Assert.That(facade.Load(Stream.Null, false, null), Is.SameAs(loaded));
            Assert.That(facade.SaveSessionConfiguration(), Is.SameAs(savedConfiguration));
            Assert.That(facade.ApplySessionConfiguration(new SessionConfiguration()), Is.True);
            Assert.That(facade.AddSubscription(null!), Is.True);
            Assert.That(facade.RemoveTransferredSubscription(null!), Is.True);
            Assert.That(facade.BeginPublish(1000), Is.True);
            Assert.That(facade.NewRequestHandle(), Is.EqualTo(4242u));
            facade.StartPublishing(1000, true);

            session.Verify(
                s => s.Save(Stream.Null, It.IsAny<IEnumerable<Subscription>>(), null),
                Times.Once());
            session.Verify(s => s.StartPublishing(1000, true), Times.Once());

#pragma warning disable CS0618 // AttachChannel/DetachChannel are obsolete but still delegated.
            ITransportChannel channel = Mock.Of<ITransportChannel>();
            facade.AttachChannel(channel);
            facade.DetachChannel();
            session.Verify(s => s.AttachChannel(channel), Times.Once());
            session.Verify(s => s.DetachChannel(), Times.Once());
#pragma warning restore CS0618
        }

        [Test]
        public void TryGetSubscriptionManagerReturnsManagerWhenLeader()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            ISubscriptionManager? expected = Mock.Of<ISubscriptionManager>();
            session.Setup(s => s.TryGetSubscriptionManager(out expected)).Returns(true);

            bool result = facade.TryGetSubscriptionManager(out ISubscriptionManager? manager);

            Assert.That(result, Is.True);
            Assert.That(manager, Is.SameAs(expected));
        }

        [Test]
        public void TryGetSubscriptionManagerReturnsFalseWithoutSession()
        {
            RedundantClientSession facade = CreateStandbyFacade();

            bool result = facade.TryGetSubscriptionManager(out ISubscriptionManager? manager);

            Assert.That(result, Is.False);
            Assert.That(manager, Is.Null);
        }

        [Test]
        public async Task LeaderAttributeServiceCallsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            var read = new ReadResponse();
            var write = new WriteResponse();
            var historyRead = new HistoryReadResponse();
            var historyUpdate = new HistoryUpdateResponse();
            var browse = new BrowseResponse();
            var browseNext = new BrowseNextResponse();
            var translate = new TranslateBrowsePathsToNodeIdsResponse();
            var register = new RegisterNodesResponse();
            var unregister = new UnregisterNodesResponse();
            var call = new CallResponse();

            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(read));
            session.Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(write));
            session.Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryReadResponse>(historyRead));
            session.Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryUpdateResponse>(historyUpdate));
            session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(browse));
            session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(browseNext));
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(translate));
            session.Setup(s => s.RegisterNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RegisterNodesResponse>(register));
            session.Setup(s => s.UnregisterNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<UnregisterNodesResponse>(unregister));
            session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(call));

            Assert.That(await facade.ReadAsync(null, 0, TimestampsToReturn.Neither, [], ct).ConfigureAwait(false), Is.SameAs(read));
            Assert.That(await facade.WriteAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(write));
            Assert.That(
                await facade.HistoryReadAsync(null, default, TimestampsToReturn.Both, false, [], ct).ConfigureAwait(false),
                Is.SameAs(historyRead));
            Assert.That(await facade.HistoryUpdateAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(historyUpdate));
            Assert.That(await facade.BrowseAsync(null, null, 0, [], ct).ConfigureAwait(false), Is.SameAs(browse));
            Assert.That(await facade.BrowseNextAsync(null, false, [], ct).ConfigureAwait(false), Is.SameAs(browseNext));
            Assert.That(await facade.TranslateBrowsePathsToNodeIdsAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(translate));
            Assert.That(await facade.RegisterNodesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(register));
            Assert.That(await facade.UnregisterNodesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(unregister));
            Assert.That(await facade.CallAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(call));
        }

        [Test]
        public async Task LeaderMonitoredItemServiceCallsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            var create = new CreateMonitoredItemsResponse();
            var modify = new ModifyMonitoredItemsResponse();
            var setMode = new SetMonitoringModeResponse();
            var setTriggering = new SetTriggeringResponse();
            var delete = new DeleteMonitoredItemsResponse();

            session.Setup(s => s.CreateMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CreateMonitoredItemsResponse>(create));
            session.Setup(s => s.ModifyMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ModifyMonitoredItemsResponse>(modify));
            session.Setup(s => s.SetMonitoringModeAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<MonitoringMode>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SetMonitoringModeResponse>(setMode));
            session.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SetTriggeringResponse>(setTriggering));
            session.Setup(s => s.DeleteMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeleteMonitoredItemsResponse>(delete));

            Assert.That(
                await facade.CreateMonitoredItemsAsync(null, 1, TimestampsToReturn.Both, [], ct).ConfigureAwait(false),
                Is.SameAs(create));
            Assert.That(
                await facade.ModifyMonitoredItemsAsync(null, 1, TimestampsToReturn.Both, [], ct).ConfigureAwait(false),
                Is.SameAs(modify));
            Assert.That(
                await facade.SetMonitoringModeAsync(null, 1, MonitoringMode.Reporting, [], ct).ConfigureAwait(false),
                Is.SameAs(setMode));
            Assert.That(
                await facade.SetTriggeringAsync(null, 1, 2, [], [], ct).ConfigureAwait(false),
                Is.SameAs(setTriggering));
            Assert.That(
                await facade.DeleteMonitoredItemsAsync(null, 1, [], ct).ConfigureAwait(false),
                Is.SameAs(delete));
        }

        [Test]
        public async Task LeaderSubscriptionServiceCallsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            var create = new CreateSubscriptionResponse();
            var modify = new ModifySubscriptionResponse();
            var setMode = new SetPublishingModeResponse();
            var publish = new PublishResponse();
            var republish = new RepublishResponse();
            var transfer = new TransferSubscriptionsResponse();
            var delete = new DeleteSubscriptionsResponse();

            session.Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CreateSubscriptionResponse>(create));
            session.Setup(s => s.ModifySubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ModifySubscriptionResponse>(modify));
            session.Setup(s => s.SetPublishingModeAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SetPublishingModeResponse>(setMode));
            session.Setup(s => s.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PublishResponse>(publish));
            session.Setup(s => s.RepublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RepublishResponse>(republish));
            session.Setup(s => s.TransferSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TransferSubscriptionsResponse>(transfer));
            session.Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeleteSubscriptionsResponse>(delete));

            Assert.That(
                await facade.CreateSubscriptionAsync(null, 1000, 10, 3, 1000, true, 0, ct).ConfigureAwait(false),
                Is.SameAs(create));
            Assert.That(
                await facade.ModifySubscriptionAsync(null, 1, 1000, 10, 3, 1000, 0, ct).ConfigureAwait(false),
                Is.SameAs(modify));
            Assert.That(await facade.SetPublishingModeAsync(null, true, [], ct).ConfigureAwait(false), Is.SameAs(setMode));
            Assert.That(await facade.PublishAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(publish));
            Assert.That(await facade.RepublishAsync(null, 1, 2, ct).ConfigureAwait(false), Is.SameAs(republish));
            Assert.That(await facade.TransferSubscriptionsAsync(null, [], true, ct).ConfigureAwait(false), Is.SameAs(transfer));
            Assert.That(await facade.DeleteSubscriptionsAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(delete));
        }

        [Test]
        public async Task LeaderNodeManagementServiceCallsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            var addNodes = new AddNodesResponse();
            var addReferences = new AddReferencesResponse();
            var deleteNodes = new DeleteNodesResponse();
            var deleteReferences = new DeleteReferencesResponse();
            var queryFirst = new QueryFirstResponse();
            var queryNext = new QueryNextResponse();

            session.Setup(s => s.AddNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<AddNodesItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AddNodesResponse>(addNodes));
            session.Setup(s => s.AddReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<AddReferencesItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AddReferencesResponse>(addReferences));
            session.Setup(s => s.DeleteNodesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<DeleteNodesItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeleteNodesResponse>(deleteNodes));
            session.Setup(s => s.DeleteReferencesAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<DeleteReferencesItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeleteReferencesResponse>(deleteReferences));
            session.Setup(s => s.QueryFirstAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<ArrayOf<NodeTypeDescription>>(),
                    It.IsAny<ContentFilter>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<QueryFirstResponse>(queryFirst));
            session.Setup(s => s.QueryNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<QueryNextResponse>(queryNext));

            Assert.That(await facade.AddNodesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(addNodes));
            Assert.That(await facade.AddReferencesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(addReferences));
            Assert.That(await facade.DeleteNodesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(deleteNodes));
            Assert.That(await facade.DeleteReferencesAsync(null, [], ct).ConfigureAwait(false), Is.SameAs(deleteReferences));
            Assert.That(await facade.QueryFirstAsync(null, null, [], null, 0, 0, ct).ConfigureAwait(false), Is.SameAs(queryFirst));
            Assert.That(await facade.QueryNextAsync(null, false, default, ct).ConfigureAwait(false), Is.SameAs(queryNext));
        }

        [Test]
        public async Task LeaderSessionServiceCallsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            var createSession = new CreateSessionResponse();
            var activateSession = new ActivateSessionResponse();
            var closeSession = new CloseSessionResponse();
            var cancel = new CancelResponse();

            session.Setup(s => s.CreateSessionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ApplicationDescription>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CreateSessionResponse>(createSession));
            session.Setup(s => s.ActivateSessionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<SignatureData>(),
                    It.IsAny<ArrayOf<SignedSoftwareCertificate>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<SignatureData>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ActivateSessionResponse>(activateSession));
            session.Setup(s => s.CloseSessionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CloseSessionResponse>(closeSession));
            session.Setup(s => s.CancelAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CancelResponse>(cancel));

            Assert.That(
                await facade.CreateSessionAsync(null, null, null, null, null, default, default, 0, 0, ct).ConfigureAwait(false),
                Is.SameAs(createSession));
            Assert.That(
                await facade.ActivateSessionAsync(null, null, [], [], default, null, ct).ConfigureAwait(false),
                Is.SameAs(activateSession));
            Assert.That(await facade.CloseSessionAsync(null, true, ct).ConfigureAwait(false), Is.SameAs(closeSession));
            Assert.That(await facade.CancelAsync(null, 1, ct).ConfigureAwait(false), Is.SameAs(cancel));
        }

        [Test]
        public async Task LeaderConvenienceMethodsDelegateToCurrentSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);
            CancellationToken ct = CancellationToken.None;

            session.Setup(s => s.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ITransportChannel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.ReloadInstanceCertificateAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.FetchNamespaceTablesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.FetchTypeTreeAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.FetchTypeTreeAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.OpenAsync(
                    It.IsAny<string>(),
                    It.IsAny<uint>(),
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.UpdateSessionAsync(
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.ChangePreferredLocalesAsync(
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(s => s.CloseAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(StatusCodes.Good));
            session.Setup(s => s.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(StatusCodes.Good));
            session.Setup(s => s.RemoveSubscriptionAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));
            session.Setup(s => s.RemoveSubscriptionsAsync(
                    It.IsAny<IEnumerable<Subscription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));
            session.Setup(s => s.ReactivateSubscriptionsAsync(
                    It.IsAny<SubscriptionCollection>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));
            session.Setup(s => s.TransferSubscriptionsAsync(
                    It.IsAny<SubscriptionCollection>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));
            session.Setup(s => s.RepublishAsync(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((true, ServiceResult.Good)));

            await facade.ReconnectAsync(null, null, ct).ConfigureAwait(false);
            await facade.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
            await facade.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
            await facade.FetchTypeTreeAsync((ExpandedNodeId)null!, ct).ConfigureAwait(false);
            await facade.FetchTypeTreeAsync([], ct).ConfigureAwait(false);
            await facade.OpenAsync("session", 1000, Mock.Of<IUserIdentity>(), [], true, true, ct).ConfigureAwait(false);
            await facade.UpdateSessionAsync(Mock.Of<IUserIdentity>(), [], ct).ConfigureAwait(false);
            await facade.ChangePreferredLocalesAsync([], ct).ConfigureAwait(false);

            Assert.That(await facade.CloseAsync(1000, true, ct).ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
            Assert.That(await facade.CloseAsync(ct).ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
            Assert.That(await facade.RemoveSubscriptionAsync(null!, ct).ConfigureAwait(false), Is.True);
            Assert.That(await facade.RemoveSubscriptionsAsync([], ct).ConfigureAwait(false), Is.True);
            Assert.That(await facade.ReactivateSubscriptionsAsync([], true, ct).ConfigureAwait(false), Is.True);
            Assert.That(await facade.TransferSubscriptionsAsync([], false, ct).ConfigureAwait(false), Is.True);

            (bool republished, ServiceResult republishResult) = await facade.RepublishAsync(1, 2, ct).ConfigureAwait(false);
            Assert.That(republished, Is.True);
            Assert.That(republishResult, Is.SameAs(ServiceResult.Good));

            session.Verify(
                s => s.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ITransportChannel>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
            session.Verify(s => s.ReloadInstanceCertificateAsync(It.IsAny<CancellationToken>()), Times.Once());
            session.Verify(s => s.FetchNamespaceTablesAsync(It.IsAny<CancellationToken>()), Times.Once());
            session.Verify(
                s => s.OpenAsync(
                    It.IsAny<string>(),
                    It.IsAny<uint>(),
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Test]
        public void FacadeStateReflectsLeadershipAndCurrentSession()
        {
            var session = new Mock<ISession>();
            RedundantClientSession leader = CreateLeaderFacade(session);
            RedundantClientSession standby = CreateStandbyFacade();

            Assert.That(leader.IsLeader, Is.True);
            Assert.That(leader.Current, Is.SameAs(session.Object));
            Assert.That(leader.Disposed, Is.False);
            Assert.That(standby.IsLeader, Is.False);
            Assert.That(standby.Current, Is.Null);
        }

        [Test]
        public async Task WaitForLeadershipCompletesWhenLeaderHasSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            await facade.WaitForLeadershipAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(facade.Current, Is.SameAs(session.Object));
        }

        [Test]
        public async Task StartAsyncStartsCoordinatorAndWiresSessionAsync()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            await facade.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(facade.Current, Is.SameAs(session.Object));
        }

        [Test]
        public void RememberedValuesReappliedOnSessionSwap()
        {
            ISession? current = null;
            var first = new Mock<ISession>();
            var second = new Mock<ISession>();
            current = first.Object;
            RedundantClientSession facade = CreateLeaderFacade(() => current);

            object handle = new();
            facade.DeleteSubscriptionsOnClose = true;
            facade.KeepAliveInterval = 5;
            facade.MinPublishRequestCount = 6;
            facade.MaxPublishRequestCount = 7;
            facade.TransferSubscriptionsOnReconnect = true;
            facade.ContinuationPointPolicy = ContinuationPointPolicy.Balanced;
            facade.PublishRequestCancelDelayOnCloseSession = 8;
            facade.Handle = handle;

            current = second.Object;
            facade.RefreshActiveSessionForTesting();

            second.VerifySet(s => s.DeleteSubscriptionsOnClose = true, Times.Once());
            second.VerifySet(s => s.KeepAliveInterval = 5, Times.Once());
            second.VerifySet(s => s.MinPublishRequestCount = 6, Times.Once());
            second.VerifySet(s => s.MaxPublishRequestCount = 7, Times.Once());
            second.VerifySet(s => s.TransferSubscriptionsOnReconnect = true, Times.Once());
            second.VerifySet(s => s.ContinuationPointPolicy = ContinuationPointPolicy.Balanced, Times.Once());
            second.VerifySet(s => s.PublishRequestCancelDelayOnCloseSession = 8, Times.Once());
            Assert.That(facade.Current, Is.SameAs(second.Object));
        }

        [Test]
        public void EventsForwardFromCurrentSessionToFacadeSubscribers()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            ISession? keepAliveSender = null;
            ISession? notificationSender = null;
            ISession? publishErrorSender = null;
            ISession? acknowledgeSender = null;
            object? subscriptionsChangedSender = null;
            object? sessionClosingSender = null;
            object? sessionConfigurationChangedSender = null;
            ISession? renewSender = null;

            void KeepAliveHandler(ISession s, KeepAliveEventArgs e) => keepAliveSender = s;
            void NotificationHandler(ISession s, NotificationEventArgs e) => notificationSender = s;
            void PublishErrorHandler(ISession s, PublishErrorEventArgs e) => publishErrorSender = s;
            void AcknowledgeHandler(ISession s, PublishSequenceNumbersToAcknowledgeEventArgs e) =>
                acknowledgeSender = s;
            void SubscriptionsChangedHandler(object? sender, EventArgs e) => subscriptionsChangedSender = sender;
            void SessionClosingHandler(object? sender, EventArgs e) => sessionClosingSender = sender;
            void SessionConfigurationChangedHandler(object? sender, EventArgs e) =>
                sessionConfigurationChangedSender = sender;
            IUserIdentity RenewHandler(ISession s, IUserIdentity identity)
            {
                renewSender = s;
                return identity;
            }

            facade.KeepAlive += KeepAliveHandler;
            facade.Notification += NotificationHandler;
            facade.PublishError += PublishErrorHandler;
            facade.PublishSequenceNumbersToAcknowledge += AcknowledgeHandler;
            facade.SubscriptionsChanged += SubscriptionsChangedHandler;
            facade.SessionClosing += SessionClosingHandler;
            facade.SessionConfigurationChanged += SessionConfigurationChangedHandler;
            facade.RenewUserIdentity += RenewHandler;

            session.Raise(
                s => s.KeepAlive += null,
                session.Object,
                new KeepAliveEventArgs(ServiceResult.Good, ServerState.Running, DateTime.UtcNow));
            session.Raise(
                s => s.Notification += null,
                session.Object,
                new NotificationEventArgs(null!, null!, default));
            session.Raise(
                s => s.PublishError += null,
                session.Object,
                new PublishErrorEventArgs(ServiceResult.Good));
            session.Raise(
                s => s.PublishSequenceNumbersToAcknowledge += null,
                session.Object,
                new PublishSequenceNumbersToAcknowledgeEventArgs(
                    [],
                    []));
            session.Raise(s => s.SubscriptionsChanged += null, session.Object, EventArgs.Empty);
            session.Raise(s => s.SessionClosing += null, session.Object, EventArgs.Empty);
            session.Raise(s => s.SessionConfigurationChanged += null, session.Object, EventArgs.Empty);
            session.Raise(s => s.RenewUserIdentity += null, session.Object, Mock.Of<IUserIdentity>());

            Assert.That(keepAliveSender, Is.SameAs(facade));
            Assert.That(notificationSender, Is.SameAs(facade));
            Assert.That(publishErrorSender, Is.SameAs(facade));
            Assert.That(acknowledgeSender, Is.SameAs(facade));
            Assert.That(subscriptionsChangedSender, Is.SameAs(facade));
            Assert.That(sessionClosingSender, Is.SameAs(facade));
            Assert.That(sessionConfigurationChangedSender, Is.SameAs(facade));
            Assert.That(renewSender, Is.SameAs(facade));

            // Unsubscribing stops forwarding, exercising the event remove accessors.
            facade.KeepAlive -= KeepAliveHandler;
            facade.Notification -= NotificationHandler;
            facade.PublishError -= PublishErrorHandler;
            facade.PublishSequenceNumbersToAcknowledge -= AcknowledgeHandler;
            facade.SubscriptionsChanged -= SubscriptionsChangedHandler;
            facade.SessionClosing -= SessionClosingHandler;
            facade.SessionConfigurationChanged -= SessionConfigurationChangedHandler;
            facade.RenewUserIdentity -= RenewHandler;

            keepAliveSender = null;
            session.Raise(
                s => s.KeepAlive += null,
                session.Object,
                new KeepAliveEventArgs(ServiceResult.Good, ServerState.Running, DateTime.UtcNow));
            Assert.That(keepAliveSender, Is.Null);
        }

        [Test]
        public async Task RoleChangesPromoteThenDemoteRewireSessionAsync()
        {
            ISession? current = null;
            var session = new Mock<ISession>();
            var election = new ManualLeaderElection();
            var store = new InMemorySharedKeyValueStore();
            m_disposables.Add(store);
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default, EnableTokenReuse = false },
                election,
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var facade = new RedundantClientSession(coordinator, () => current);
            m_facades.Add(facade);

            var promoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var demoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            facade.RoleChanged += isLeader =>
            {
                if (isLeader)
                {
                    promoted.TrySetResult(true);
                }
                else
                {
                    demoted.TrySetResult(true);
                }
            };

            Assert.That(facade.Current, Is.Null);

            current = session.Object;
            election.Promote();
            await promoted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(facade.IsLeader, Is.True);
            Assert.That(facade.Current, Is.SameAs(session.Object));

            election.Demote();
            await demoted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(facade.IsLeader, Is.False);
            Assert.That(facade.Current, Is.Null);
        }

        [Test]
        public void NoSessionSyncMembersThrowBadInvalidState()
        {
            RedundantClientSession facade = CreateStandbyFacade();

            ServiceResultException getterException =
                Assert.Throws<ServiceResultException>(() => _ = facade.Connected)!;
            Assert.That(getterException.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            ServiceResultException setterException =
                Assert.Throws<ServiceResultException>(() => facade.KeepAliveInterval = 1)!;
            Assert.That(setterException.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            ServiceResultException methodException =
                Assert.Throws<ServiceResultException>(() => facade.BeginPublish(1000))!;
            Assert.That(methodException.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void AsyncMemberThrowsWhenCancelled()
        {
            RedundantClientSession facade = CreateStandbyFacade();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await facade.ReadAsync(null, 0, TimestampsToReturn.Neither, [], cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void DisposeIsIdempotentAndBlocksFurtherUse()
        {
            var session = new Mock<ISession>();
            RedundantClientSession facade = CreateLeaderFacade(session);

            facade.Dispose();
            Assert.That(facade.Disposed, Is.True);
            Assert.That(facade.Dispose, Throws.Nothing);

            Assert.That(() => _ = facade.SessionName, Throws.InstanceOf<ObjectDisposedException>());
            Assert.That(
                async () => await facade.ReadAsync(null, 0, TimestampsToReturn.Neither, [], CancellationToken.None).ConfigureAwait(false),
                Throws.InstanceOf<ObjectDisposedException>());
            Assert.That(
                async () => await facade.StartAsync(CancellationToken.None).ConfigureAwait(false),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        private RedundantClientSession CreateLeaderFacade(Mock<ISession> session)
        {
            return CreateLeaderFacade(() => session.Object);
        }

        private RedundantClientSession CreateLeaderFacade(Func<ISession?> accessor)
        {
            var store = new InMemorySharedKeyValueStore();
            m_disposables.Add(store);
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default, EnableTokenReuse = false },
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var facade = new RedundantClientSession(coordinator, accessor);
            m_facades.Add(facade);
            return facade;
        }

        private RedundantClientSession CreateStandbyFacade()
        {
            var store = new InMemorySharedKeyValueStore();
            m_disposables.Add(store);
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default, EnableTokenReuse = false },
                new StaticLeaderElection(false),
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var facade = new RedundantClientSession(coordinator, () => null);
            m_facades.Add(facade);
            return facade;
        }

        private sealed class ManualLeaderElection : ILeaderElection
        {
            public bool IsLeader { get; private set; }

            public event Action<bool>? LeadershipChanged;

            public void Start()
            {
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public void Promote()
            {
                IsLeader = true;
                LeadershipChanged?.Invoke(true);
            }

            public void Demote()
            {
                IsLeader = false;
                LeadershipChanged?.Invoke(false);
            }
        }
    }
}
