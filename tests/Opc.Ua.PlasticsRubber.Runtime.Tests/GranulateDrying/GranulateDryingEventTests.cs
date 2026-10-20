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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.PlasticsRubber;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moq;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.GranulateDrying.Tests
{
    public sealed partial class GranulateDryingClientServerTests
    {
        /// <summary>
        /// Real event subscriptions receive bin-sourced common alarms while the mandatory error summary agrees.
        /// </summary>
        [Test]
        public async Task ErrorEventsMatchTheAuthoritativeErrorArrayAndResetArgumentsAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            await using ManagedSession observer = await m_host.OpenSessionAsync(false).ConfigureAwait(false);
            var registry = new EventRecordDecoderRegistry().RegisterPlasticsRubberDecoders(observer.NamespaceUris);
            EventFilter filter = HelpOffNormalAlarmTypeRecord.EventFilters.Build(observer.NamespaceUris, registry);
            await using EventSubscription subscription = await EventSubscription.CreateAsync(
                observer, device.NodeId, filter).ConfigureAwait(false);
            GranulateDryingCommit fault = authority.Next() with
            {
                Timestamp = DryingAuthority.Time(21),
                Errors =
                [
                    new GranulateDryingError("airflow", 710, 2, new LocalizedText("Airflow unavailable"),
                        [GranulateDryingTarget.Bin(1)]) { HelpText = new LocalizedText("Inspect the air supply") },
                    new GranulateDryingError("material", 410, 1, new LocalizedText("Material unavailable"),
                        [GranulateDryingTarget.Bin(1)])
                ]
            };
            await device.PublishAsync(fault).ConfigureAwait(false);
            ArrayOf<Variant> fields = await subscription.NextAsync().ConfigureAwait(false);
            NodeId type = ExpandedNodeId.ToNodeId(
                PlasticsRubber.ObjectTypeIds.HelpOffNormalAlarmType, observer.NamespaceUris);
            var alarm = registry.DecodeAs(type, fields.ToArray()!) as HelpOffNormalAlarmTypeRecord;
            Assert.That(alarm, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(alarm!.SourceNode, Is.EqualTo(device.Bin(1).NodeId));
                Assert.That(alarm.Time, Is.EqualTo(DryingAuthority.Time(21)));
                Assert.That(alarm.EventId.IsNull, Is.False);
            });
            GranulateDryingOperationClient operation = await m_host.Client.Device(device.NodeId).OperationAsync()
                .ConfigureAwait(false);
            GranulateDryingReading<ArrayOf<ClassifiedActiveErrorDataType>> active =
                await operation.ActiveErrors.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(active.Value.Count, Is.EqualTo(2));
                Assert.That(active.Value[0].Id, Is.EqualTo("airflow"));
                Assert.That(active.Value[0].Classification, Is.EqualTo(2));
                Assert.That(active.Value[0].SourceNodes, Is.EqualTo((ArrayOf<NodeId>)[device.Bin(1).NodeId]));
            });
            Assert.That((await operation.HighestActiveAlarmSeverity.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo((ushort)710));
            await AssertUaFailure(async () => await operation.Proxy.ResetErrorByIdAsync("unknown").ConfigureAwait(false),
                StatusCodes.BadNotFound);
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next() with { Errors = [fault.Errors[1]] }));
            await operation.Proxy.ResetErrorByIdAsync("airflow").ConfigureAwait(false);
            Assert.That(authority.LastCommand!.ErrorId, Is.EqualTo("airflow"));
            Assert.That((await operation.HighestActiveAlarmSeverity.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo((ushort)410));
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next() with { Errors = [] }));
            await operation.Proxy.ResetAllErrorsAsync().ConfigureAwait(false);
            Assert.That((await operation.ActiveErrors.ReadAsync().ConfigureAwait(false)).Value.Count, Is.Zero);
            Assert.That((await operation.HighestActiveAlarmSeverity.ReadAsync().ConfigureAwait(false)).Value, Is.Zero);
        }

        /// <summary>
        /// The abstract GDD alarm is realized as an application-owned concrete subtype
        /// and decoded with its generated layout.
        /// </summary>
        [Test]
        public async Task MaintenanceAlarmUsesConcreteSubtypeAndExactTypedFieldsAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            await using ManagedSession observer = await m_host.OpenSessionAsync(false).ConfigureAwait(false);
            var registry = new EventRecordDecoderRegistry().RegisterGranulateDryingDecoders(observer.NamespaceUris);
            EventFilter filter = MaintenanceAlarmTypeRecord.EventFilters.Build(observer.NamespaceUris, registry);
            await using EventSubscription subscription = await EventSubscription.CreateAsync(
                observer, device.NodeId, filter).ConfigureAwait(false);
            await device.PublishAsync(authority.Next(
                DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "Status",
                    Variant.From(MaintenanceStatusEnumeration.DUE), 33),
                DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "RemainingInterval", Variant.From(0.0), 33))
                with { Timestamp = DryingAuthority.Time(33) }).ConfigureAwait(false);
            ArrayOf<Variant> fields = await subscription.NextAsync().ConfigureAwait(false);
            NodeId abstractType = ExpandedNodeId.ToNodeId(ObjectTypeIds.MaintenanceAlarmType, observer.NamespaceUris);
            var alarm = registry.DecodeAs(abstractType, fields.ToArray()!) as MaintenanceAlarmTypeRecord;
            Assert.That(alarm, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(alarm!.Id, Is.EqualTo("inspection"));
                Assert.That(alarm.Status, Is.EqualTo(MaintenanceStatusEnumeration.DUE));
                Assert.That(alarm.SourceNode, Is.EqualTo(device.Maintenance(1).NodeId));
                Assert.That(alarm.Time, Is.EqualTo(DryingAuthority.Time(33)));
                Assert.That(alarm.EventType, Is.Not.EqualTo(abstractType));
                Assert.That(alarm.EventType.NamespaceIndex, Is.EqualTo(m_host.Runtime.InstanceNamespaceIndex));
            });
            Assert.That(await observer.NodeCache.IsTypeOfAsync(alarm!.EventType,
                abstractType).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Provides a deterministic subscription-ready seam using ordinary UA subscription services.
        /// Generated filters and decoders, not hand-written event layouts, define the payload contract.
        /// </summary>
        private sealed class EventSubscription : IAsyncDisposable
        {
            /// <summary>
            /// Creates an already-established test subscription.
            /// </summary>
            private EventSubscription(ManagedSession session, uint subscriptionId)
            {
                m_session = session;
                m_subscriptionId = subscriptionId;
            }

            /// <summary>
            /// Awaits server-side monitored item creation before any event is produced.
            /// </summary>
            internal static async ValueTask<EventSubscription> CreateAsync(
                ManagedSession session, NodeId source, EventFilter filter)
            {
                CreateSubscriptionResponse response = await session.CreateSubscriptionAsync(
                    null, 50, 300, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
                var subscription = new EventSubscription(session, response.SubscriptionId);
                try
                {
                    CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(
                        null, response.SubscriptionId, TimestampsToReturn.Both,
                        [
                            new MonitoredItemCreateRequest
                            {
                                ItemToMonitor = new ReadValueId
                                {
                                    NodeId = source,
                                    AttributeId = Attributes.EventNotifier
                                },
                                MonitoringMode = MonitoringMode.Reporting,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 0,
                                    QueueSize = 64,
                                    DiscardOldest = true,
                                    Filter = new ExtensionObject(filter)
                                }
                            }
                        ], CancellationToken.None).ConfigureAwait(false);
                    Assert.That(monitored.Results.Count, Is.EqualTo(1));
                    Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    return subscription;
                }
                catch
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Reads a queued filtered event with a bounded request timeout.
            /// </summary>
            internal async ValueTask<ArrayOf<Variant>> NextAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                for (int ii = 0; ii < 20; ii++)
                {
                    PublishResponse response = await m_session.PublishAsync(
                        new RequestHeader { TimeoutHint = 15000 }, [], timeout.Token).ConfigureAwait(false);
                    foreach (ExtensionObject body in response.NotificationMessage.NotificationData)
                    {
                        if (body.TryGetValue(out EventNotificationList? events) && events != null &&
                            events.Events.Count != 0)
                        {
                            return events.Events[0].EventFields;
                        }
                    }
                }
                throw new AssertionException("The established event subscription did not receive the committed event.");
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await m_session.DeleteSubscriptionsAsync(null, [m_subscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// The fixture-owned observer session.
            /// </summary>
            private readonly ManagedSession m_session;

            /// <summary>
            /// The one server-side subscription created by this helper.
            /// </summary>
            private readonly uint m_subscriptionId;
        }
    }

    /// <summary>
    /// Exercises both public generated event APIs with finite, caller-owned subscription streams.
    /// </summary>
    [TestFixture]
    public sealed class GranulateDryingPublicEventTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task GddPublicStreamsDecodeGeneratedFieldsAndVendorSubtypesAsync(bool alarm, bool subtype)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId declared = DeclaredType(peer, alarm);
            NodeId actual = subtype ? new NodeId("VendorEvent", 2) : declared;
            if (subtype)
            {
                peer.ExpectType(actual, declared, true);
            }
            int disposed = 0;
            int produced = 0;
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeEventsAsync(
                GranulateDryingClientPeer.DeviceId, It.IsAny<EventFilter>(), null, cancellation.Token))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions _, CancellationToken token) =>
                    Notifications(EventFields(peer, filter, Variant.From(actual), alarm),
                        1, () => produced++, () => disposed++, null, token));
            await using (IAsyncEnumerator<BaseEventTypeRecord> iterator =
                Events(peer.Client, stream.Object, alarm, cancellation.Token).GetAsyncEnumerator(cancellation.Token))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                AssertRecord(iterator.Current, actual, alarm);
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
            Assert.That(produced, Is.EqualTo(1));
            Assert.That(disposed, Is.EqualTo(1));
            if (!subtype)
            {
                Assert.That(peer.Cache.Invocations, Is.Empty, "Registered records decode without a subtype service.");
            }
            VerifyStream(peer, stream);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GddPublicStreamsRejectInvalidArguments(bool alarm)
        {
            var peer = new GranulateDryingClientPeer();
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            ArgumentNullException? absent = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = alarm
                    ? (IAsyncEnumerable<BaseEventTypeRecord>)peer.Client.ObserveAlarmsAsync(null!,
                        GranulateDryingClientPeer.DeviceId)
                    : peer.Client.ObserveMaintenanceAsync(null!, GranulateDryingClientPeer.DeviceId);
            });
            Assert.That(absent!.ParamName, Is.EqualTo("streaming"));
            ArgumentException? notifier = Assert.Throws<ArgumentException>(() =>
            {
                _ = alarm
                    ? (IAsyncEnumerable<BaseEventTypeRecord>)peer.Client.ObserveAlarmsAsync(stream.Object, NodeId.Null)
                    : peer.Client.ObserveMaintenanceAsync(stream.Object, NodeId.Null);
            });
            Assert.That(notifier!.ParamName, Is.EqualTo("notifier"));
            Assert.That(stream.Invocations, Is.Empty);
            peer.VerifyNoServices();
        }

        [TestCase(false, "Missing")]
        [TestCase(true, "Missing")]
        [TestCase(false, "WrongPayload")]
        [TestCase(true, "WrongPayload")]
        [TestCase(false, "Unrelated")]
        [TestCase(true, "Unrelated")]
        [TestCase(false, "RegisteredUnrelated")]
        [TestCase(true, "RegisteredUnrelated")]
        public async Task GddPublicStreamsRejectInvalidArgumentsAndEventTypesAsync(bool alarm, string invalid)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId actual = invalid == "RegisteredUnrelated"
                ? peer.Type(alarm ? PlasticsRubber.ObjectTypeIds.LogbookEventType :
                    PlasticsRubber.ObjectTypeIds.HelpOffNormalAlarmType)
                : new NodeId("unrelated", 2);
            Variant typeField = invalid switch
            {
                "Missing" => Variant.Null,
                "WrongPayload" => Variant.From("not a NodeId"),
                _ => Variant.From(actual)
            };
            if (invalid is "Unrelated" or "RegisteredUnrelated")
            {
                peer.ExpectType(actual, DeclaredType(peer, alarm), false);
            }
            int disposed = 0;
            int produced = 0;
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeEventsAsync(
                GranulateDryingClientPeer.DeviceId, It.IsAny<EventFilter>(), null, cancellation.Token))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions _, CancellationToken token) =>
                    Notifications(EventFields(peer, filter, typeField, alarm),
                        1, () => produced++, () => disposed++, null, token));
            await using IAsyncEnumerator<BaseEventTypeRecord> iterator =
                Events(peer.Client, stream.Object, alarm, cancellation.Token).GetAsyncEnumerator(cancellation.Token);
            await GranulateDryingClientPeer.AssertFailure(async () =>
                await iterator.MoveNextAsync().ConfigureAwait(false), StatusCodes.BadDecodingError);
            Assert.That(produced, Is.EqualTo(1));
            Assert.That(disposed, Is.EqualTo(1), "A rejected record must dispose the upstream iterator.");
            if (invalid is "Missing" or "WrongPayload")
            {
                Assert.That(peer.Cache.Invocations, Is.Empty);
            }
            VerifyStream(peer, stream);
        }

        [TestCase(false, "Complete")]
        [TestCase(true, "Complete")]
        [TestCase(false, "Empty")]
        [TestCase(true, "Empty")]
        [TestCase(false, "Break")]
        [TestCase(true, "Break")]
        [TestCase(false, "Failure")]
        [TestCase(true, "Failure")]
        [TestCase(false, "Cancel")]
        [TestCase(true, "Cancel")]
        [TestCase(false, "PreCancelled")]
        [TestCase(true, "PreCancelled")]
        public async Task GddPublicStreamsPreserveFailureCancellationAndOwnershipAsync(bool alarm, string ending)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId declared = DeclaredType(peer, alarm);
            int disposed = 0;
            int produced = 0;
            var failure = new InvalidOperationException("The peer ended the event stream.");
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeEventsAsync(
                GranulateDryingClientPeer.DeviceId, It.IsAny<EventFilter>(), null, cancellation.Token))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions _, CancellationToken token) =>
                    Notifications(EventFields(peer, filter, Variant.From(declared), alarm),
                        ending == "Empty" ? 0 : 2, () => produced++, () => disposed++,
                        ending == "Failure" ? failure : null, token));
            if (ending == "PreCancelled")
            {
                cancellation.Cancel();
            }
            await using (IAsyncEnumerator<BaseEventTypeRecord> iterator =
                Events(peer.Client, stream.Object, alarm, cancellation.Token).GetAsyncEnumerator(cancellation.Token))
            {
                if (ending == "PreCancelled")
                {
                    OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await iterator.MoveNextAsync().ConfigureAwait(false));
                    Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
                }
                else if (ending == "Empty")
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                }
                else
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                    AssertRecord(iterator.Current, declared, alarm);
                    switch (ending)
                    {
                        case "Complete":
                            Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                            AssertRecord(iterator.Current, declared, alarm);
                            Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                            break;
                        case "Failure":
                            Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                                await iterator.MoveNextAsync().ConfigureAwait(false)), Is.SameAs(failure));
                            break;
                        case "Cancel":
                            cancellation.Cancel();
                            OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async
                                () =>
                                await iterator.MoveNextAsync().ConfigureAwait(false));
                            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
                            break;
                        case "Break":
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(ending));
                    }
                }
            }
            Assert.That(produced, Is.EqualTo(ending == "Complete" ? 2 : ending is "Empty" or "PreCancelled" ? 0 : 1));
            Assert.That(disposed, Is.EqualTo(1));
            Assert.That(peer.Cache.Invocations, Is.Empty);
            VerifyStream(peer, stream);
        }

        private static IAsyncEnumerable<BaseEventTypeRecord> Events(
            GranulateDryingClient client, IStreamingSubscription stream, bool alarm, CancellationToken token) =>
            alarm
                ? client.ObserveAlarmsAsync(stream, GranulateDryingClientPeer.DeviceId, token)
                : client.ObserveMaintenanceAsync(stream, GranulateDryingClientPeer.DeviceId, token);

        private static NodeId DeclaredType(GranulateDryingClientPeer peer, bool alarm) =>
            peer.Type(alarm ? PlasticsRubber.ObjectTypeIds.HelpOffNormalAlarmType : ObjectTypeIds.MaintenanceAlarmType);

        private static Variant[] EventFields(
            GranulateDryingClientPeer peer, EventFilter filter, Variant eventType, bool alarm)
        {
            SimpleAttributeOperand[] clauses = filter.SelectClauses.ToArray()!;
            Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 &&
                clause.BrowsePath[0] == new QualifiedName(Opc.Ua.BrowseNames.EventType)), Is.True);
            Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 &&
                clause.BrowsePath[0] == peer.Common("HelpText")), Is.True);
            if (!alarm)
            {
                Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 &&
                    clause.BrowsePath[0] == peer.Gdd("Id")), Is.True);
                Assert.That(clauses.Any(clause => clause.BrowsePath.Count == 1 &&
                    clause.BrowsePath[0] == peer.Gdd("Status")), Is.True);
            }
            return clauses.Select(clause =>
            {
                string? name = clause.BrowsePath.Count == 1 ? clause.BrowsePath[0].Name : null;
                return name switch
                {
                    Opc.Ua.BrowseNames.EventType => eventType,
                    Opc.Ua.BrowseNames.EventId => Variant.From(ByteString.From(new byte[] { 7, 29, 0xa1, 0xfe })),
                    Opc.Ua.BrowseNames.SourceNode => Variant.From(new NodeId("inspection/source-27", 1)),
                    Opc.Ua.BrowseNames.SourceName => Variant.From("Dryer North / filter"),
                    Opc.Ua.BrowseNames.Time => Variant.From(GranulateDryingClientPeer.SourceTime),
                    Opc.Ua.BrowseNames.ReceiveTime => Variant.From(GranulateDryingClientPeer.ServerTime),
                    Opc.Ua.BrowseNames.Message => Variant.From(new LocalizedText("fr", "Inspection requise")),
                    Opc.Ua.BrowseNames.Severity => Variant.From((ushort)725),
                    "HelpText" => Variant.From(new LocalizedText("en", "Inspect filter 27")),
                    "Id" => Variant.From("inspection-27"),
                    "Status" => Variant.From(MaintenanceStatusEnumeration.WARNING),
                    "RemainingInterval" => Variant.From(-17.5),
                    _ => Variant.Null
                };
            }).ToArray();
        }

        private static void AssertRecord(BaseEventTypeRecord record, NodeId expectedType, bool alarm)
        {
            Assert.Multiple(() =>
            {
                Assert.That(record.GetType(), Is.EqualTo(alarm
                    ? typeof(HelpOffNormalAlarmTypeRecord) : typeof(MaintenanceAlarmTypeRecord)));
                Assert.That(record.EventType, Is.EqualTo(expectedType));
                Assert.That(record.EventId.Span.ToArray(), Is.EqualTo(new byte[] { 7, 29, 0xa1, 0xfe }));
                Assert.That(record.SourceNode, Is.EqualTo(new NodeId("inspection/source-27", 1)));
                Assert.That(record.SourceName, Is.EqualTo("Dryer North / filter"));
                Assert.That(record.Time, Is.EqualTo(GranulateDryingClientPeer.SourceTime));
                Assert.That(record.ReceiveTime, Is.EqualTo(GranulateDryingClientPeer.ServerTime));
                Assert.That(record.Message, Is.EqualTo(new LocalizedText("fr", "Inspection requise")));
                Assert.That(record.Severity, Is.EqualTo(725));
                Assert.That(((HelpOffNormalAlarmTypeRecord)record).HelpText, Is.EqualTo(new LocalizedText("en",
                    "Inspect filter 27")));
                if (!alarm)
                {
                    var maintenance = (MaintenanceAlarmTypeRecord)record;
                    Assert.That(maintenance.Id, Is.EqualTo("inspection-27"));
                    Assert.That(maintenance.Status, Is.EqualTo(MaintenanceStatusEnumeration.WARNING));
                    Assert.That(maintenance.RemainingInterval, Is.EqualTo(-17.5));
                }
            });
        }

        private static async IAsyncEnumerable<EventNotification> Notifications(
            Variant[] fields, int count, Action produced, Action disposed, Exception? failure,
            [EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(false);
                for (int index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    produced();
                    yield return new EventNotification(null, fields.ToArrayOf());
                    if (failure != null)
                    {
                        throw failure;
                    }
                }
            }
            finally
            {
                disposed();
            }
        }

        private static void VerifyStream(GranulateDryingClientPeer peer, Mock<IStreamingSubscription> stream)
        {
            stream.Verify(value => value.SubscribeEventsAsync(GranulateDryingClientPeer.DeviceId,
                It.IsAny<EventFilter>(), null, peer.Token), Times.Once);
            stream.Verify(value => value.DisposeAsync(), Times.Never);
            peer.Verify();
        }
    }
}
