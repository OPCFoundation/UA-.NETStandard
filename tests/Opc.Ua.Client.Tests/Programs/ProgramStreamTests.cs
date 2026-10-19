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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Programs;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;
using ProgramState = Opc.Ua.Programs.ProgramState;
using ProgramStateMachineModel = Opc.Ua.Programs.ProgramStateMachineModel;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Tests.Programs
{
    /// <summary>
    /// Captured Program event payloads, filtering, readiness and explicit loss/failure behavior.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Parallelizable(ParallelScope.Fixtures)]
    public sealed class ProgramStreamTests
    {
        /// <summary>
        /// Creates a strict session and remapped SDK event identifiers.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:unrelated");
            context.NamespaceUris.GetIndexOrAppend(Ua.Programs.Namespaces.OpcUaPrograms);
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(context);
            m_proxy = new ProgramStateMachineTypeClient(m_session.Object, s_invocation, telemetry);
            m_transitionType = ExpandedNodeId.ToNodeId(
                Ua.Programs.ObjectTypeIds.ManagedProgramTransitionEventType, context.NamespaceUris);
            m_auditType = ExpandedNodeId.ToNodeId(
                Ua.Programs.ObjectTypeIds.ManagedProgramAuditEventType, context.NamespaceUris);
        }

        /// <summary>
        /// Captured progress events remain distinct even when their target state is unchanged.
        /// </summary>
        [Test]
        public async Task TransitionStreamRetainsSameStateProgressAndFiltersSourceAsync()
        {
            var stream = new ScriptedStreaming(filter =>
            [
                Fields(filter, m_transitionType, new NodeId("different-program", 2), 99),
                Fields(filter, m_transitionType, s_invocation, 1),
                Fields(filter, m_transitionType, s_invocation, 2)
            ]);
            var received = new List<ProgramTransitionEventTypeRecord>();
            await foreach (ProgramTransitionEventTypeRecord record in m_proxy.ObserveProgramTransitionsAsync(
                stream, ObjectIds.Server).ConfigureAwait(false))
            {
                received.Add(record);
            }
            Assert.Multiple(() =>
            {
                Assert.That(received, Has.Count.EqualTo(2));
                Assert.That(received[0], Is.InstanceOf<Ua.Programs.ManagedProgramTransitionEventTypeRecord>());
                Assert.That(received[0].IntermediateResult, Is.EqualTo(Variant.From(1)));
                Assert.That(received[1].IntermediateResult, Is.EqualTo(Variant.From(2)));
                Assert.That(received[1].ToStateId,
                    Is.EqualTo(Variant.From(ProgramStateMachineModel.GetStateId(ProgramState.Running))));
                Assert.That(stream.LastFilter!.WhereClause.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Or));
                Assert.That(stream.LastFilter.WhereClause.Elements.ToArray()!
                    .Any(e => e.FilterOperator == FilterOperator.Equals),
                    Is.True);
                Assert.That(stream.EnumeratorDisposed, Is.True);
                Assert.That(stream.DisposeCalls, Is.Zero);
            });
            m_session.Verify(s => s.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// BaseDataType state identifiers from vendor event models remain intact in typed records.
        /// </summary>
        [Test]
        public async Task VendorStateIdentifiersArePreservedWithoutGuessingNodeIdsAsync()
        {
            var stream = new ScriptedStreaming(filter =>
            {
                Variant[] fields = Fields(filter, m_transitionType, s_invocation, 1).ToArray()!;
                for (int index = 0; index < filter.SelectClauses.Count; index++)
                {
                    if (Path(filter.SelectClauses[index]) == "ToState/Id")
                    {
                        fields[index] = Variant.From(42u);
                    }
                }
                return [ArrayOf.Wrapped(fields)];
            });
            int received = 0;
            await foreach (ProgramTransitionEventTypeRecord record in m_proxy.ObserveProgramTransitionsAsync(
                stream, ObjectIds.Server).ConfigureAwait(false))
            {
                Assert.That(record.ToStateId, Is.EqualTo(Variant.From(42u)));
                received++;
            }
            Assert.That(received, Is.EqualTo(1));
        }

        /// <summary>
        /// Concrete audit events decode through their generated model registration.
        /// </summary>
        [Test]
        public async Task AuditStreamUsesGeneratedRecordAndCapturedTransitionNumberAsync()
        {
            var stream = new ScriptedStreaming(filter => [Fields(filter, m_auditType, s_invocation, 0)]);
            var received = new List<AuditProgramTransitionEventTypeRecord>();
            await foreach (AuditProgramTransitionEventTypeRecord record in m_proxy.ObserveProgramAuditsAsync(
                stream, ObjectIds.Server).ConfigureAwait(false))
            {
                received.Add(record);
            }
            Assert.Multiple(() =>
            {
                Assert.That(received.Single(), Is.InstanceOf<Ua.Programs.ManagedProgramAuditEventTypeRecord>());
                Assert.That(received[0].TransitionNumber, Is.EqualTo(2u));
                Assert.That(received[0].Status, Is.True);
            });
        }

        /// <summary>
        /// A control cannot run before its event monitored item exists on the server.
        /// </summary>
        [Test]
        public async Task WaitRunsControlOnlyAfterServerMonitoredItemIsCreatedAsync()
        {
            var stream = new ScriptedStreaming(filter => [Fields(filter, m_transitionType, s_invocation, 7)]);
            bool invoked = false;
            m_session.Setup(s => s.CallAsync(It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r => r[0].MethodId == MethodIds.ProgramStateMachineType_Start),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(stream.ReadinessInvoked, Is.True);
                    Assert.That(stream.Item.Created, Is.True);
                    invoked = true;
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult()]
                    });
                });
            ProgramTransitionEventTypeRecord result = await m_proxy.WaitForProgramTransitionAsync(
                stream, ObjectIds.Server, ProgramState.Running, token => m_proxy.StartAsync(token))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(invoked, Is.True);
                Assert.That(result.IntermediateResult, Is.EqualTo(Variant.From(7)));
                Assert.That(stream.EnumeratorDisposed, Is.True);
                Assert.That(stream.DisposeCalls, Is.Zero);
            });
        }

        /// <summary>
        /// A custom streaming implementation must explicitly support readiness for control-and-wait.
        /// </summary>
        [Test]
        public async Task ReadinessUnsupportedFailsWithoutIssuingControl()
        {
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            bool called = false;
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await m_proxy.WaitForProgramTransitionAsync(stream.Object, ObjectIds.Server, ProgramState.Running,
                    _ =>
                    {
                        called = true;
                        return default;
                    }).ConfigureAwait(false));
            Assert.That(called, Is.False);
        }

        /// <summary>
        /// A failed server monitored-item creation aborts the wait before control dispatch.
        /// </summary>
        [Test]
        public async Task MonitoredItemCreationFailureDoesNotIssueControl()
        {
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(i => i.Created).Returns(false);
            item.SetupGet(i => i.Error).Returns(new ServiceResult(StatusCodes.BadUserAccessDenied));
            item.As<IMonitoredItemApplyState>().SetupGet(i => i.HasPendingChanges).Returns(false);
            var stream = new ScriptedStreaming(_ => [], item.Object);
            bool called = false;
            await AssertStatus(async () =>
                await m_proxy.WaitForProgramTransitionAsync(stream, ObjectIds.Server, ProgramState.Running,
                    _ =>
                    {
                        called = true;
                        return default;
                    }).ConfigureAwait(false), StatusCodes.BadUserAccessDenied);
            Assert.That(called, Is.False);
        }

        /// <summary>
        /// Server overflow events bypass invocation filtering and report loss explicitly.
        /// </summary>
        [Test]
        public async Task ServerOverflowIsNeverSilentlyDiscardedBySourceFiltering()
        {
            var stream = new ScriptedStreaming(filter =>
                [Fields(filter, ObjectTypeIds.EventQueueOverflowEventType, NodeId.Null, 0)]);
            await AssertStatus(async () =>
            {
                await foreach (ProgramTransitionEventTypeRecord _ in m_proxy.ObserveProgramTransitionsAsync(
                    stream, ObjectIds.Server).ConfigureAwait(false))
                {
                    Assert.Fail("An overflow notification cannot be returned as a Program transition.");
                }
            }, StatusCodes.BadDataLost);
        }

        /// <summary>
        /// The concrete streaming implementation's overflow counter invalidates a pending wait.
        /// </summary>
        [Test]
        public async Task LocalOverflowFailsWaitInsteadOfClaimingCompleteObservationAsync()
        {
            var monitoredItem = new Mock<IMonitoredItem>();
            monitoredItem.SetupGet(i => i.ClientHandle).Returns(17);
            monitoredItem.SetupGet(i => i.Created).Returns(true);
            IMonitoredItem? item = monitoredItem.Object;
            var items = new Mock<IMonitoredItemCollection>();
            items.Setup(i => i.TryAdd(It.IsAny<string>(), It.IsAny<IOptionsMonitor<MonitoringOptions>>(), out item))
                .Returns(true);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(s => s.MonitoredItems).Returns(items.Object);
            var manager = new Mock<ISubscriptionManager>();
            manager.Setup(m => m.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<Subscriptions.SubscriptionOptions>>()))
                .Returns(subscription.Object);
            await using var streaming = new StreamingSubscription(manager.Object);
            await AssertStatus(async () =>
                await m_proxy.WaitForProgramTransitionAsync(
                    streaming, ObjectIds.Server, ProgramState.Running,
                    _ =>
                    {
                        var notification = new EventNotification(item, []);
                        streaming.DispatchEvent(notification);
                        streaming.DispatchEvent(notification);
                        return default;
                    },
                    new MonitoringOptions { QueueSize = 1, DiscardOldest = true }).ConfigureAwait(false),
                StatusCodes.BadDataLost);
            Assert.That(streaming.DroppedNotificationCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Required event fields are validated before the permissive generated decoder runs.
        /// </summary>
        [TestCase("field-count")]
        [TestCase("to-state")]
        [TestCase("event-type")]
        [TestCase("source")]
        public async Task MalformedEventsFailInsteadOfBecomingDefaultRecords(string invalid)
        {
            var stream = new ScriptedStreaming(filter =>
            {
                if (invalid == "field-count")
                {
                    return [ArrayOf<Variant>.Empty];
                }
                Variant[] fields = Fields(filter, m_transitionType, s_invocation, 1).ToArray()!;
                string target = invalid switch
                {
                    "to-state" => "ToState/Id",
                    "event-type" => "EventType",
                    _ => "SourceNode"
                };
                int index = filter.SelectClauses.ToArray()!.Select((c, i) => (Path: Path(c), Index: i))
                    .Single(p => p.Path == target).Index;
                fields[index] = invalid == "to-state" ? Variant.Null : Variant.From(123);
                return [ArrayOf.Wrapped(fields)];
            });
            await AssertStatus(async () =>
            {
                await foreach (ProgramTransitionEventTypeRecord _ in m_proxy.ObserveProgramTransitionsAsync(
                    stream, ObjectIds.Server).ConfigureAwait(false))
                {
                    Assert.Fail("Malformed event was accepted.");
                }
            }, StatusCodes.BadDecodingError);
        }

        /// <summary>
        /// A custom decoder failure remains observable and is never converted to an empty stream.
        /// </summary>
        [Test]
        public async Task DecoderExceptionsArePropagated()
        {
            var registry = new EventRecordDecoderRegistry();
            registry.Register(m_transitionType, ProgramTransitionEventTypeRecord.Decoder.StandardFields,
                _ => throw new InvalidOperationException("decoder-failure"));
            var stream = new ScriptedStreaming(filter => [Fields(filter, m_transitionType, s_invocation, 1)]);
            InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (ProgramTransitionEventTypeRecord _ in m_proxy.ObserveProgramTransitionsAsync(
                    stream, ObjectIds.Server, registry).ConfigureAwait(false))
                {
                    Assert.Fail("Decoder error was swallowed.");
                }
            }))!;
            Assert.That(error.Message, Is.EqualTo("decoder-failure"));
        }

        /// <summary>
        /// An explicitly registered decoder returning no record does not trigger a success-shaped fallback.
        /// </summary>
        [Test]
        public async Task NullCustomDecoderResultIsAnExplicitDecodingFailure()
        {
            var registry = new EventRecordDecoderRegistry();
            registry.Register(m_transitionType, ProgramTransitionEventTypeRecord.Decoder.StandardFields, _ => null);
            var stream = new ScriptedStreaming(filter => [Fields(filter, m_transitionType, s_invocation, 1)]);
            await AssertStatus(async () =>
            {
                await foreach (ProgramTransitionEventTypeRecord _ in m_proxy.ObserveProgramTransitionsAsync(
                    stream, ObjectIds.Server, registry).ConfigureAwait(false))
                {
                    Assert.Fail("A null decoder result was silently replaced.");
                }
            }, StatusCodes.BadDecodingError);
        }

        /// <summary>
        /// Cancellation stops readiness and does not perform a state read or control.
        /// </summary>
        [Test]
        public async Task CancelledWaitDoesNotReadStateOrRunControl()
        {
            var stream = new ScriptedStreaming(_ => []);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool called = false;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_proxy.WaitForProgramTransitionAsync(stream, ObjectIds.Server, ProgramState.Running,
                    _ =>
                    {
                        called = true;
                        return default;
                    }, ct: cancellation.Token).ConfigureAwait(false));
            Assert.That(called, Is.False);
        }

        /// <summary>
        /// Unexpected stream completion is not confused with reaching the target state.
        /// </summary>
        [Test]
        public async Task EarlyStreamEndIsReportedAsNoData()
        {
            var stream = new ScriptedStreaming(_ => []);
            await AssertStatus(async () =>
                await m_proxy.WaitForProgramTransitionAsync(stream, ObjectIds.Server, ProgramState.Halted)
                    .ConfigureAwait(false), StatusCodes.BadNoData);
        }

        private static ArrayOf<Variant> Fields(EventFilter filter, NodeId type, NodeId source, int progress)
        {
            return ArrayOf.Wrapped(filter.SelectClauses.ToArray()!.Select(clause => Path(clause) switch
            {
                "EventId" => Variant.From(ByteString.From([1, 2, 3])),
                "EventType" => Variant.From(type),
                "SourceNode" => Variant.From(source),
                "SourceName" => Variant.From("Program"),
                "Time" or "ReceiveTime" or "ActionTimeStamp" => Variant.From(DateTimeUtc.Now),
                "Message" => Variant.From(new LocalizedText("progress")),
                "Severity" => Variant.From((ushort)1),
                "FromState" or "ToState" => Variant.From(new LocalizedText("Running")),
                "Transition" => Variant.From(new LocalizedText("RunningToRunning")),
                "FromState/Id" or "ToState/Id" or "OldStateId" or "NewStateId" =>
                    Variant.From(ProgramStateMachineModel.GetStateId(ProgramState.Running)),
                "Transition/Id" => Variant.From(new NodeId("vendor-progress", 2)),
                "IntermediateResult" => Variant.From(progress),
                "TransitionNumber" => Variant.From(2u),
                "Status" => Variant.From(true),
                _ => Variant.Null
            }).ToArray());
        }

        private static string Path(SimpleAttributeOperand clause)
        {
            return string.Join("/", clause.BrowsePath.ToArray()!.Select(p => p.Name));
        }

        private static async Task AssertStatus(Func<Task> action, StatusCode expected)
        {
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(action))!;
            Assert.That(exception.StatusCode, Is.EqualTo(expected));
        }

        /// <summary>
        /// Scripted caller-owned streaming subscription with the real readiness contract.
        /// </summary>
        private sealed class ScriptedStreaming : IStreamingSubscription, IStreamingSubscriptionReadiness
        {
            /// <summary>
            /// Creates a scripted stream with a caller-provided monitored-item result.
            /// </summary>
            public ScriptedStreaming(
                Func<EventFilter, ArrayOf<ArrayOf<Variant>>> script,
                IMonitoredItem? item = null)
            {
                m_script = script;
                if (item is null)
                {
                    var mock = new Mock<IMonitoredItem>();
                    mock.SetupGet(i => i.Created).Returns(true);
                    item = mock.Object;
                }
                Item = item;
            }

            /// <summary>
            /// The item made available to the readiness callback.
            /// </summary>
            public IMonitoredItem Item { get; }

            /// <summary>
            /// The most recently requested server event filter.
            /// </summary>
            public EventFilter? LastFilter { get; private set; }

            /// <summary>
            /// Whether the consumer disposed its event enumerator.
            /// </summary>
            public bool EnumeratorDisposed { get; private set; }

            /// <summary>
            /// Whether readiness was invoked before yielding notifications.
            /// </summary>
            public bool ReadinessInvoked { get; private set; }

            /// <summary>
            /// The number of attempts to dispose the caller-owned subscription.
            /// </summary>
            public int DisposeCalls { get; private set; }

            /// <inheritdoc/>
            public IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId, EventFilter filter,
                MonitoringOptions? options = null, CancellationToken ct = default)
            {
                LastFilter = filter;
                return EnumerateAsync(filter, null, ct);
            }

            /// <inheritdoc/>
            public IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId, EventFilter filter, MonitoringOptions? options,
                Func<IMonitoredItem, CancellationToken, ValueTask> onMonitoredItemReady,
                CancellationToken ct = default)
            {
                LastFilter = filter;
                return EnumerateAsync(filter, onMonitoredItemReady, ct);
            }

            /// <inheritdoc/>
            public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId, MonitoringOptions? options = null, CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds, MonitoringOptions? options = null, CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                DisposeCalls++;
                return default;
            }

            private async IAsyncEnumerable<EventNotification> EnumerateAsync(
                EventFilter filter,
                Func<IMonitoredItem, CancellationToken, ValueTask>? ready,
                [EnumeratorCancellation] CancellationToken ct)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (ready is not null)
                    {
                        ReadinessInvoked = true;
                        await ready(Item, ct).ConfigureAwait(false);
                    }
                    ArrayOf<ArrayOf<Variant>> events = m_script(filter);
                    for (int index = 0; index < events.Count; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new EventNotification(Item, events[index]);
                    }
                }
                finally
                {
                    EnumeratorDisposed = true;
                }
            }

            private readonly Func<EventFilter, ArrayOf<ArrayOf<Variant>>> m_script;
        }

        private static readonly NodeId s_invocation = new("program", 2);
        private Mock<ISessionClient> m_session = null!;
        private ProgramStateMachineTypeClient m_proxy = null!;
        private NodeId m_transitionType;
        private NodeId m_auditType;
    }
}
