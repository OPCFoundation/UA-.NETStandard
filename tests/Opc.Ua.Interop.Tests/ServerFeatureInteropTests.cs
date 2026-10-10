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
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// This stack's client against the peer's interop server: events, the
    /// subscription features (deadband, queue overflow, triggering,
    /// republish, transfer), X509 user tokens and the RegisterNodes,
    /// IndexRange, NodeManagement, FindServers and session reconnect
    /// services. The peer server exposes the Interop folder of
    /// <see cref="LegacyServerInteropTests"/> plus a RaiseEvent() method that
    /// reports one BaseEventType event (message "interop event", severity
    /// 500) through the Server object.
    /// </summary>
    [TestFixture]
    [PeerDifferences]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ServerFeatureInteropTests
    {
        private const string kInteropNamespace = "urn:opcfoundation.org:interop:legacy";
        private static readonly TimeSpan s_startTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);
        private static readonly int[] s_partiallyWrittenInt32Array = [1, 20, 30, 4];

        private ITelemetryContext m_telemetry;
        private LegacyPeerProcess m_server;
        private ClientFixture m_clientFixture;
        private string m_pkiRoot;
        private Uri m_serverUrl;
        private ArrayOf<EndpointDescription> m_endpoints;
        private ISession m_session;
        private ushort m_ns;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = InteropPki.CreateRoot();
            (m_server, string url) = await LegacyPeerProcess
                .StartServerAsync(InteropPki.ServerPki(m_pkiRoot), s_startTimeout)
                .ConfigureAwait(false);
            m_serverUrl = new Uri(url);
            m_clientFixture = new ClientFixture(telemetry: m_telemetry) { SessionTimeout = 120_000 };
            await m_clientFixture.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot)).ConfigureAwait(false);
            m_endpoints = await m_clientFixture.GetEndpointsAsync(m_serverUrl).ConfigureAwait(false);
            m_session = await ConnectAsync(null!).ConfigureAwait(false);
            m_ns = m_session.NamespaceUris.GetIndexOrAppend(kInteropNamespace);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                    m_session.Dispose();
                }
                if (m_clientFixture != null)
                {
                    await m_clientFixture.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await InteropPki.StopAndDeleteAsync(m_server, m_pkiRoot).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An event subscription on the Server object receives the event of
        /// RaiseEvent() with its EventId, Message and Severity.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task RaiseEventDeliversEventAsync(CancellationToken ct)
        {
            var events = new List<EventFieldList>();
            Subscription subscription = await CreateEventSubscriptionAsync(events, ct).ConfigureAwait(false);
            try
            {
                CallMethodResult result = await CallAsync(new NodeId("Interop", m_ns), new NodeId("RaiseEvent", m_ns), ct)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, "RaiseEvent returned " + result.StatusCode);
                EventFieldList received = await WaitForAsync(events,
                    e => e.EventFields[2].TryGetValue(out LocalizedText message) && message.Text == "interop event", ct)
                    .ConfigureAwait(false);
                Assert.That(received, Is.Not.Null, $"no event in {s_wait.TotalSeconds} s; received {events.Count}");
                Assert.That(received.EventFields[0].TryGetValue(out ByteString id) && !id.IsEmpty, Is.True, "no EventId");
                Assert.That(received.EventFields[3].TryGetValue(out ushort severity), Is.True, "no Severity");
                Assert.That(severity, Is.EqualTo(500));
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// ConditionRefresh on an event subscription delivers the
        /// RefreshStartEvent and RefreshEndEvent pair.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ConditionRefreshDeliversRefreshEventsAsync(CancellationToken ct)
        {
            var events = new List<EventFieldList>();
            Subscription subscription = await CreateEventSubscriptionAsync(events, ct).ConfigureAwait(false);
            try
            {
                CallMethodResult result = await CallAsync(ObjectTypeIds.ConditionType,
                    MethodIds.ConditionType_ConditionRefresh, ct, new Variant(subscription.Id)).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, "ConditionRefresh returned " + result.StatusCode);
                EventFieldList end = await WaitForAsync(events,
                    e => e.EventFields[1].TryGetValue(out NodeId type) && type == ObjectTypeIds.RefreshEndEventType, ct)
                    .ConfigureAwait(false);
                Assert.That(end, Is.Not.Null, "no RefreshEndEvent");
                lock (events)
                {
                    Assert.That(events.Any(e => e.EventFields[1].TryGetValue(out NodeId type) &&
                        type == ObjectTypeIds.RefreshStartEventType), Is.True, "no RefreshStartEvent");
                }
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An absolute deadband of 10 on Interop/Double suppresses changes
        /// smaller than 10 and reports larger ones.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task AbsoluteDeadbandSuppressesSmallChangesAsync(CancellationToken ct)
        {
            await WriteAsync("Double", new Variant(0.0), ct).ConfigureAwait(false);
            var values = new List<DataValue>();
            Subscription subscription = await CreateDataSubscriptionAsync("Double", values, item =>
                item.Filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.Absolute,
                    DeadbandValue = 10
                }, 100, ct).ConfigureAwait(false);
            try
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                foreach (double v in new[] { 5.0, 20.0, 25.0, 40.0 })
                {
                    await WriteAsync("Double", new Variant(v), ct).ConfigureAwait(false);
                    await Task.Delay(400, ct).ConfigureAwait(false);
                }
                await Task.Delay(800, ct).ConfigureAwait(false);
                double[] seen = Doubles(values);
                Assert.That(seen, Does.Contain(20.0).And.Contain(40.0), string.Join(", ", seen));
                Assert.That(seen, Does.Not.Contain(5.0).And.Not.Contain(25.0), string.Join(", ", seen));
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A queue of 2 with DiscardOldest under a slow publishing interval
        /// delivers the last two of five quick writes, with the Overflow bit.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task QueueOverflowSetsOverflowBitAsync(CancellationToken ct)
        {
            await WriteAsync("Int32", new Variant(0), ct).ConfigureAwait(false);
            var values = new List<DataValue>();
            Subscription subscription = await CreateDataSubscriptionAsync("Int32", values, item =>
            {
                item.SamplingInterval = 0;
                item.QueueSize = 2;
                item.DiscardOldest = true;
            }, 2000, ct).ConfigureAwait(false);
            try
            {
                await Task.Delay(2500, ct).ConfigureAwait(false);
                lock (values)
                {
                    values.Clear();
                }
                for (int ii = 1; ii <= 5; ii++)
                {
                    await WriteAsync("Int32", new Variant(ii), ct).ConfigureAwait(false);
                }
                await Task.Delay(3000, ct).ConfigureAwait(false);
                DataValue[] seen;
                lock (values)
                {
                    seen = [.. values];
                }
                string list = string.Join(", ", seen.Select(v => $"{v.WrappedValue}:{v.StatusCode}"));
                Assert.That(seen, Has.Length.EqualTo(2), list);
                Assert.That(seen[0].WrappedValue.TryGetValue(out int first) && first == 4, Is.True, list);
                Assert.That(seen[1].WrappedValue.TryGetValue(out int second) && second == 5, Is.True, list);
                Assert.That(seen.Any(v => v.StatusCode.Overflow), Is.True, "no Overflow bit: " + list);
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A sampling item linked by SetTriggering reports only when its
        /// triggering item reports.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task TriggeringReportsLinkedItemAsync(CancellationToken ct)
        {
            var subscription = new Subscription(m_session.DefaultSubscription) { PublishingInterval = 100 };
            m_session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            try
            {
                int linkedReports = 0;
                var trigger = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = new NodeId("Int32", m_ns),
                    SamplingInterval = 50,
                    MonitoringMode = MonitoringMode.Reporting
                };
                var linked = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = new NodeId("String", m_ns),
                    SamplingInterval = 50,
                    MonitoringMode = MonitoringMode.Sampling
                };
                linked.Notification += (_, _) => Interlocked.Increment(ref linkedReports);
                subscription.AddItem(trigger);
                subscription.AddItem(linked);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                SetTriggeringResponse set = await m_session.SetTriggeringAsync(null, subscription.Id,
                    trigger.Status.Id, [linked.Status.Id], [], ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(set.AddResults[0]), Is.True, "SetTriggering returned " + set.AddResults[0]);

                await Task.Delay(500, ct).ConfigureAwait(false);
                int before = Volatile.Read(ref linkedReports);
                await WriteAsync("String", new Variant("linked " + Guid.NewGuid()), ct).ConfigureAwait(false);
                await Task.Delay(500, ct).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref linkedReports), Is.EqualTo(before), "the sampling item reported without its trigger");
                await WriteAsync("Int32", new Variant(Environment.TickCount), ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref linkedReports), Is.GreaterThan(before), "the triggered item did not report");
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Republish of a sequence number the subscription never sent returns
        /// BadMessageNotAvailable, and of an unknown subscription
        /// BadSubscriptionIdInvalid. (Republishing a sent message is racy: the
        /// client acknowledges it with its next Publish and the server may
        /// drop acknowledged messages.)
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task RepublishReportsUnavailableMessagesAsync(CancellationToken ct)
        {
            var values = new List<DataValue>();
            Subscription subscription = await CreateDataSubscriptionAsync("Int32", values, _ => { }, 100, ct)
                .ConfigureAwait(false);
            try
            {
                StatusCode notSent = await RepublishStatusAsync(subscription.Id, subscription.SequenceNumber + 1000, ct)
                    .ConfigureAwait(false);
                Assert.That(notSent.Code, Is.EqualTo(StatusCodes.BadMessageNotAvailable.Code), notSent.ToString());
                StatusCode unknown = await RepublishStatusAsync(subscription.Id + 100_000, 1, ct).ConfigureAwait(false);
                Assert.That(unknown.Code, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid.Code), unknown.ToString());
            }
            finally
            {
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        private async Task<StatusCode> RepublishStatusAsync(uint subscriptionId, uint sequence, CancellationToken ct)
        {
            try
            {
                RepublishResponse response = await m_session.RepublishAsync(null, subscriptionId, sequence, ct)
                    .ConfigureAwait(false);
                return response.ResponseHeader.ServiceResult;
            }
            catch (ServiceResultException sre)
            {
                return sre.StatusCode;
            }
        }

        /// <summary>
        /// A subscription transferred to a second session of the same user
        /// keeps reporting there.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task TransferSubscriptionToNewSessionAsync(CancellationToken ct)
        {
            var values = new List<DataValue>();
            Subscription subscription = await CreateDataSubscriptionAsync("Int32", values, _ => { }, 100, ct)
                .ConfigureAwait(false);
            ISession target = await ConnectAsync(null!).ConfigureAwait(false);
            try
            {
                SubscriptionCollection transferred = new SubscriptionCollection([subscription]).CloneSubscriptions(false);
                foreach (Subscription s in transferred)
                {
                    target.AddSubscription(s);
                }
                int notifications = 0;
                foreach (MonitoredItem item in transferred[0].MonitoredItems)
                {
                    item.Notification += (_, _) => Interlocked.Increment(ref notifications);
                }
                bool ok = await target.TransferSubscriptionsAsync(transferred, true, ct).ConfigureAwait(false);
                Assert.That(ok, Is.True, "TransferSubscriptions failed");
                await Task.Delay(300, ct).ConfigureAwait(false);
                int before = Volatile.Read(ref notifications);
                await WriteAsync("Int32", new Variant(Environment.TickCount), ct).ConfigureAwait(false);
                DateTime deadline = DateTime.UtcNow + s_wait;
                while (Volatile.Read(ref notifications) == before && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                Assert.That(Volatile.Read(ref notifications), Is.GreaterThan(before), "the transferred subscription reported nothing");
            }
            finally
            {
                target.DeleteSubscriptionsOnClose = true;
                await target.CloseAsync(ct).ConfigureAwait(false);
                target.Dispose();
                await DeleteAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A session with an X509 user identity token (a self-signed user
        /// certificate the peer auto-accepts) reads the server state.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task X509UserTokenIsAcceptedAsync(CancellationToken ct)
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=InteropUser, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var provider = new InProcessCertificateProvider(cert);
            var identity = new UserIdentity(new X509IdentityTokenHandler(
                new CertificateIdentifier { Thumbprint = cert.Thumbprint, SubjectName = cert.Subject },
                new CertificatePasswordProvider(),
                provider));
            ISession session = await ConnectAsync(identity).ConfigureAwait(false);
            try
            {
                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True, state.StatusCode.ToString());
            }
            finally
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// RegisterNodes returns ids that read like the originals.
        /// </summary>
        [Test]
        public async Task RegisterNodesAndReadThroughIdsAsync()
        {
            RegisterNodesResponse registered = await m_session.RegisterNodesAsync(null,
                [new NodeId("Int32", m_ns), new NodeId("String", m_ns)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(registered.RegisteredNodeIds.Count, Is.EqualTo(2));
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [.. registered.RegisteredNodeIds.ToArray()!.Select(id => new ReadValueId { NodeId = id, AttributeId = Attributes.Value })],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results.ToArray()!.All(r => StatusCode.IsGood(r.StatusCode)), Is.True);
            await m_session.UnregisterNodesAsync(null, registered.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes and reads parts of Interop/Int32Array with an IndexRange.
        /// </summary>
        [Test]
        public async Task IndexRangeWriteAndReadAsync()
        {
            await WriteAsync("Int32Array", new Variant(Enumerable.Range(0, 10).ToArray()), CancellationToken.None)
                .ConfigureAwait(false);
            WriteResponse write = await m_session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = new NodeId("Int32Array", m_ns),
                    AttributeId = Attributes.Value,
                    IndexRange = "2:3",
                    Value = new DataValue(new Variant((ArrayOf<int>)[20, 30]))
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(write.Results[0]), Is.True, "write 2:3 returned " + write.Results[0]);
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
            [
                new ReadValueId { NodeId = new NodeId("Int32Array", m_ns), AttributeId = Attributes.Value, IndexRange = "1:4" }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(read.Results[0].StatusCode), Is.True, "read 1:4 returned " + read.Results[0].StatusCode);
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out ArrayOf<int> part), Is.True, read.Results[0].WrappedValue.ToString());
            Assert.That(part.ToArray(), Is.EqualTo(s_partiallyWrittenInt32Array));
        }

        /// <summary>
        /// AddNodes adds an object below the Interop folder that browsing
        /// finds, and DeleteNodes removes it. A server without node
        /// management answers with a status code (inconclusive).
        /// </summary>
        [Test]
        public async Task AddAndDeleteNodeAsync()
        {
            string name = "Added_" + Guid.NewGuid().ToString("N")[..8];
            AddNodesResponse added;
            try
            {
                added = await AddNodeAsync(name).ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Inconclusive("The server does not support NodeManagement: " + sre.StatusCode);
                return;
            }
            StatusCode status = added.Results[0].StatusCode;
            if (status == StatusCodes.BadServiceUnsupported || status == StatusCodes.BadNotSupported ||
                status == StatusCodes.BadNotImplemented)
            {
                Assert.Inconclusive("The server does not support NodeManagement: " + status);
            }
            Assert.That(StatusCode.IsGood(status), Is.True, "AddNodes returned " + status);
            DeleteNodesResponse deleted = await m_session.DeleteNodesAsync(null,
                [new DeleteNodesItem { NodeId = added.Results[0].AddedNodeId, DeleteTargetReferences = true }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(deleted.Results[0]), Is.True, "DeleteNodes returned " + deleted.Results[0]);
        }

        private async Task<AddNodesResponse> AddNodeAsync(string name)
        {
            return await m_session.AddNodesAsync(null,
            [
                new AddNodesItem
                {
                    ParentNodeId = new NodeId("Interop", m_ns),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    RequestedNewNodeId = new NodeId(name, m_ns),
                    BrowseName = new QualifiedName(name, m_ns),
                    NodeClass = NodeClass.Object,
                    NodeAttributes = new ExtensionObject(new ObjectAttributes
                    {
                        DisplayName = new LocalizedText(name),
                        SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName
                    }),
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                }
            ], CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// FindServers lists the peer server.
        /// </summary>
        [Test]
        public async Task FindServersListsServerAsync()
        {
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                m_clientFixture.Config, m_serverUrl, DiagnosticsMasks.None, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(default, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(servers.ToArray()!.Select(s => s.ApplicationUri),
                Does.Contain(m_session.Endpoint.Server.ApplicationUri));
        }

        /// <summary>
        /// Reconnects the session on a new secure channel; the session id is
        /// kept and reads go on.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task SessionReconnectKeepsSessionAsync(CancellationToken ct)
        {
            ISession session = await ConnectAsync(null!).ConfigureAwait(false);
            try
            {
                NodeId before = session.SessionId;
                await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
                Assert.That(session.SessionId, Is.EqualTo(before));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True, state.StatusCode.ToString());
            }
            finally
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
                session.Dispose();
            }
        }

        private async Task<ISession> ConnectAsync(IUserIdentity identity)
        {
            EndpointDescription description = m_endpoints.ToArray()!.FirstOrDefault(e =>
                e.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
                e.SecurityMode == MessageSecurityMode.SignAndEncrypt)!;
            Assert.That(description, Is.Not.Null, "The peer offers no Basic256Sha256/SignAndEncrypt endpoint.");
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(m_clientFixture.Config));
            return await m_clientFixture.ConnectAsync(endpoint, identity).ConfigureAwait(false);
        }

        private async Task<Subscription> CreateEventSubscriptionAsync(List<EventFieldList> events, CancellationToken ct)
        {
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100
            };
            m_session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From("Severity"));
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = 0,
                QueueSize = 100,
                Filter = filter
            };
            item.Notification += (_, e) =>
            {
                if (e.NotificationValue is EventFieldList fields)
                {
                    lock (events)
                    {
                        events.Add(fields);
                    }
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(item.Status.Error), Is.True, "event item " + item.Status.Error);
            return subscription;
        }

        private async Task<Subscription> CreateDataSubscriptionAsync(
            string name,
            List<DataValue> values,
            Action<MonitoredItem> configure,
            int publishingInterval,
            CancellationToken ct)
        {
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100
            };
            m_session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = new NodeId(name, m_ns),
                SamplingInterval = 50,
                QueueSize = 10
            };
            configure(item);
            item.Notification += (_, e) =>
            {
                if (e.NotificationValue is MonitoredItemNotification n)
                {
                    lock (values)
                    {
                        values.Add(n.Value);
                    }
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(item.Status.Error), Is.True, "monitored item " + item.Status.Error);
            return subscription;
        }

        private static double[] Doubles(List<DataValue> values)
        {
            lock (values)
            {
                return [.. values.Select(v => v.WrappedValue.TryGetValue(out double d) ? d : double.NaN)];
            }
        }

        private static async Task<EventFieldList> WaitForAsync(
            List<EventFieldList> events,
            Func<EventFieldList, bool> match,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + s_wait;
            while (DateTime.UtcNow < deadline)
            {
                lock (events)
                {
                    EventFieldList found = events.FirstOrDefault(match)!;
                    if (found != null)
                    {
                        return found;
                    }
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return null!;
        }

        private async Task DeleteAsync(Subscription subscription, CancellationToken ct)
        {
            try
            {
                await m_session.RemoveSubscriptionAsync(subscription, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The test already reported its outcome.
            }
        }

        private async Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, CancellationToken ct,
            params Variant[] arguments)
        {
            CallResponse response = await m_session.CallAsync(null,
            [
                new CallMethodRequest { ObjectId = objectId, MethodId = methodId, InputArguments = [.. arguments] }
            ], ct).ConfigureAwait(false);
            return response.Results[0];
        }

        private async Task WriteAsync(string name, Variant value, CancellationToken ct)
        {
            WriteResponse response = await m_session.WriteAsync(null,
            [
                new WriteValue { NodeId = new NodeId(name, m_ns), AttributeId = Attributes.Value, Value = new DataValue(value) }
            ], ct).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True, $"write of {name} returned {response.Results[0]}");
        }
    }
}
