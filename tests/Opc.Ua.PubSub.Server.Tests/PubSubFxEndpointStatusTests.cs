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
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Data;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using Harness = Opc.Ua.PubSub.Server.Tests.PubSubCommunicationIdMethodTests.Harness;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Exercises the Part 81 endpoint health matrices through generated endpoint Value reads and live runtime states.
    /// </summary>
    [TestFixture]
    public sealed class PubSubFxEndpointStatusTests
    {
        [TestCaseSource(nameof(StatusMatrix))]
        public async Task FxEndpointStatusMatchesPart81Matrix(
            PubSubConnectionEndpointModeEnum mode,
            PubSubState? readerState,
            PubSubState? writerState,
            ConnectionEndpointStatusEnum expected)
        {
            await using var harness = new Harness();
            var endpoint = CreateEndpoint(mode);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(
                harness.Context, endpoint, Reader(readerState), Writer(writerState));

            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(expected));
            Assert.That(endpoint.Status!.Value, Is.EqualTo(expected));
        }

        [Test]
        public async Task FxStatusEnableMethodAwaitsTransportAndPropagatesCancellation()
        {
            var open = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new Mock<IPubSubTransport>();
            transport.Setup(t => t.OpenAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken ct) =>
                {
                    using CancellationTokenRegistration registration = ct.Register(() => open.TrySetCanceled());
                    entered.SetResult(true);
                    await open.Task.ConfigureAwait(false);
                });
            await using var harness = new Harness(transportFactory: () => transport.Object);
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "delayed",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            }).ConfigureAwait(false);
            Assert.That(harness.Application.State.TryEnable(), Is.True);
            MethodState enable = harness.DynamicMethod("pubsub:connection:delayed:Status", "Enable");
            Assert.That(enable.OnCallMethod, Is.Null, "An async enable must not block a service-dispatch thread.");
            Assert.That(enable.OnCallMethod2Async, Is.Not.Null);
            using var cancellation = new CancellationTokenSource();
            Task<(ServiceResult Result, List<Variant> Outputs)> pending =
                harness.CallAsync(enable, harness.Context, [], cancellation.Token);
            try
            {
                Assert.That(entered.Task.IsCompleted, Is.True);
                Assert.That(pending.IsCompleted, Is.False);
            }
            finally
            {
                cancellation.Cancel();
            }
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(outputs, Is.Empty);
            transport.Verify(t => t.OpenAsync(cancellation.Token), Times.Once);
            transport.Verify(t => t.DisposeAsync(), Times.Once);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task FxStatusCancelledMethodDoesNotMutateRuntime(bool enable)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "cancelled",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            }).ConfigureAwait(false);
            PubSubState initial = PubSubState.Disabled;
            if (!enable)
            {
                Assert.That(harness.Application.State.TryEnable(), Is.True);
                Assert.That(harness.Application.Connections[0].State.TryEnable(), Is.True);
                initial = PubSubState.PreOperational;
            }
            MethodState method = harness.DynamicMethod(
                "pubsub:connection:cancelled:Status", enable ? "Enable" : "Disable");
            Assert.That(method.OnCallMethod2Async, Is.Not.Null);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            (ServiceResult result, List<Variant> outputs) =
                await harness.CallAsync(method, harness.Context, [], cancellation.Token).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(outputs, Is.Empty);
            Assert.That(harness.Application.Connections[0].State.State, Is.EqualTo(initial));
        }

        [Test]
        public async Task FxEndpointEnableMethodDoesNotClaimReceptionOrClearAReaderFault()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "subscriber",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                ReaderGroups =
                [
                    new ReaderGroupDataType
                    {
                        Name = "group",
                        SecurityMode = MessageSecurityMode.None,
                        DataSetReaders =
                        [
                            new DataSetReaderDataType
                            {
                                Name = "reader",
                                DataSetWriterId = 1,
                                MessageReceiveTimeout = 500,
                                SecurityMode = MessageSecurityMode.None,
                                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                            }
                        ]
                    }
                ]
            }).ConfigureAwait(false);
            var connection = harness.Application.Connections[0];
            var group = connection.ReaderGroups[0];
            var reader = (DataSetReader)group.DataSetReaders[0];
            Assert.That(harness.Application.State.TryEnable(), Is.True);
            Assert.That(connection.State.TryEnable(), Is.True);
            Assert.That(group.State.TryEnable(), Is.True);
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader);
            MethodState enable = harness.DynamicMethod("pubsub:reader:subscriber:group:reader:Status", "Enable");
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Ready));

            (ServiceResult enabled, _) = await harness.CallAsync(enable, harness.Context, []).ConfigureAwait(false);
            Assert.That(enabled.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reader.State.State, Is.EqualTo(PubSubState.PreOperational),
                "A successful enable Call is not evidence of valid DataSet reception.");
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));

            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            Assert.That(reader.State.TryFault(StatusCodes.BadTimeout), Is.True);
            (ServiceResult repeated, _) = await harness.CallAsync(enable, harness.Context, []).ConfigureAwait(false);
            Assert.That(repeated.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reader.State.State, Is.EqualTo(PubSubState.Error));
            Assert.That(reader.State.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Error));
            var status = harness.Manager.FindPredefinedNode<BaseDataVariableState>(
                new NodeId("pubsub:reader:subscriber:group:reader:Status:State",
                    harness.Manager.AddressSpaceNamespaceIndex));
            Assert.That(status.Value, Is.EqualTo(Variant.From((int)PubSubState.Error)),
                "The ordinary PubSub status must report the resulting runtime state, not the requested target.");

            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            MethodState disable = harness.DynamicMethod("pubsub:reader:subscriber:group:reader:Status", "Disable");
            (ServiceResult disabled, _) = await harness.CallAsync(disable, harness.Context, []).ConfigureAwait(false);
            Assert.That(disabled.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reader.State.State, Is.EqualTo(PubSubState.Disabled));
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Ready));
            Assert.That(status.Value, Is.EqualTo(Variant.From((int)PubSubState.Disabled)));
            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Ready));
        }

        [Test]
        public async Task FxEndpointStatusFollowsActualTimeoutWatcherAndFreshHeartbeatRecovery()
        {
            await using var harness = new Harness();
            var clock = new FakeTimeProvider();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var sink = new Mock<ISubscribedDataSetSink>();
            var reader = new DataSetReader(
                new DataSetReaderDataType { Name = "reader", DataSetWriterId = 1, MessageReceiveTimeout = 500 },
                sink.Object, telemetry, clock);
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);
            var registration = new Mock<IAsyncDisposable>();
            var scheduler = new Mock<IPubSubScheduler>();
            Func<CancellationToken, ValueTask>? poll = null;
            scheduler.Setup(s => s.ScheduleAsync(It.IsAny<PubSubSchedule>(),
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Callback<PubSubSchedule, Func<CancellationToken, ValueTask>, CancellationToken>(
                    (_, action, _) => poll = action)
                .Returns(new ValueTask<IAsyncDisposable>(registration.Object));
            await using var group = new ReaderGroup(new ReaderGroupDataType { Name = "group" },
                [reader], telemetry, scheduler.Object, diagnostics);
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader);
            await group.EnableAsync().ConfigureAwait(false);
            Func<CancellationToken, ValueTask> tick = poll
                ?? throw new InvalidOperationException("The real ReaderGroup did not register its timeout watcher.");
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));
            var message = new UadpDataSetMessage
            {
                DataSetWriterId = 1,
                ContentMask = UadpDataSetMessageContentMask.SequenceNumber,
                SequenceNumber = 1
            };
            var networkMessage = new UadpNetworkMessage { DataSetMessages = [message] };
            await group.DispatchAsync(networkMessage).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));

            clock.Advance(TimeSpan.FromMilliseconds(499));
            await tick(CancellationToken.None).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts), Is.Zero);
            await group.DispatchAsync(networkMessage).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await tick(CancellationToken.None).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Error),
                "Neither a Status read nor a repeated sequence may extend the receive deadline.");
            Assert.That(reader.State.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts), Is.EqualTo(1));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await tick(CancellationToken.None).ConfigureAwait(false);
            await group.DispatchAsync(networkMessage).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Error));
            Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts), Is.EqualTo(1));
            await group.DispatchAsync(new UadpNetworkMessage
            {
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        ContentMask = UadpDataSetMessageContentMask.SequenceNumber,
                        SequenceNumber = 1,
                        MessageType = PubSubDataSetMessageType.KeepAlive
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            Assert.That(reader.IsReceiveTimedOut(), Is.False);
            sink.Verify(s => s.WriteAsync(It.IsAny<IReadOnlyList<DataSetField>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));

            await group.DisableAsync().ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Ready));
            registration.Verify(r => r.DisposeAsync(), Times.Once);
            await group.DispatchAsync(networkMessage).ConfigureAwait(false);
            sink.Verify(s => s.WriteAsync(It.IsAny<IReadOnlyList<DataSetField>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Test]
        public async Task FxEndpointStatusTracksReceivedDataFaultAndRecoveryWithoutChangingReaderHealth()
        {
            await using var harness = new Harness();
            var clock = new FakeTimeProvider();
            var sink = new Mock<ISubscribedDataSetSink>();
            var reader = new DataSetReader(
                new DataSetReaderDataType { Name = "reader", DataSetWriterId = 1, MessageReceiveTimeout = 500 },
                sink.Object, NUnitTelemetryContext.Create(), clock);
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader);
            var notifications = new List<ConnectionEndpointStatusEnum>();
            endpoint.Status!.ClearChangeMasks(harness.Context, false);
            endpoint.Status.StateChanged += (context, node, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Value) != 0)
                {
                    notifications.Add(endpoint.Status.Value);
                }
            };
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Ready));

            Assert.That(reader.State.TryEnable(), Is.True);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));
            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            Assert.That(endpoint.Status!.Value, Is.EqualTo(ConnectionEndpointStatusEnum.Operational));

            clock.Advance(TimeSpan.FromMilliseconds(501));
            Assert.That(reader.IsReceiveTimedOut(), Is.True);
            Assert.That(reader.State.TryFault(StatusCodes.BadTimeout), Is.True);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Error));
            Assert.That(reader.State.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(reader.IsReceiveTimedOut(), Is.True, "Reading endpoint Status must not refresh the receive clock.");

            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            Assert.That(reader.IsReceiveTimedOut(), Is.False);
            Assert.That(notifications, Is.EqualTo(new[]
            {
                ConnectionEndpointStatusEnum.PreOperational, ConnectionEndpointStatusEnum.Operational,
                ConnectionEndpointStatusEnum.Error, ConnectionEndpointStatusEnum.Operational
            }));
            sink.Verify(s => s.WriteAsync(It.IsAny<IReadOnlyList<Opc.Ua.PubSub.Encoding.DataSetField>>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task FxEndpointStatusAssociationReplacementDoesNotRetainOrFollowOldReader()
        {
            await using var harness = new Harness();
            IDataSetReader oldReader = Reader(PubSubState.Operational)!;
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            IDisposable oldBinding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, oldReader);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            oldBinding.Dispose();
            using (harness.Manager.BindFxEndpointStatus(harness.Context, endpoint))
            {
                Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Initial));
                Assert.That(oldReader.State.TryFault(StatusCodes.BadTimeout), Is.True);
                Assert.That(endpoint.Status!.Value, Is.EqualTo(ConnectionEndpointStatusEnum.Initial));
            }
            IDataSetReader replacement = Reader(PubSubState.PreOperational)!;
            using IDisposable newBinding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, replacement);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));
            Assert.That(replacement.State.TryMarkOperational(), Is.True);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            Assert.That(oldReader.State.State, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public async Task FxEndpointStatusDisposalWaitsForActiveNotification()
        {
            await using var harness = new Harness();
            IDataSetReader reader = Reader(PubSubState.Disabled)!;
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader);
            using var notificationEntered = new ManualResetEventSlim();
            using var releaseNotification = new ManualResetEventSlim();
            endpoint.Status!.StateChanged += (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Value) != 0)
                {
                    notificationEntered.Set();
                    releaseNotification.Wait(TimeSpan.FromSeconds(5));
                }
            };

            Task<bool> transition = Task.Run(() => reader.State.TryEnable());
            Assert.That(notificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task disposal = Task.Run(() =>
            {
                disposeStarted.TrySetResult(true);
                binding.Dispose();
            });
            try
            {
                await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(disposal.Wait(TimeSpan.FromMilliseconds(100)), Is.False);
            }
            finally
            {
                releaseNotification.Set();
            }

            Assert.That(await transition.ConfigureAwait(false), Is.True);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task FxEndpointStatusRefusesOtherReadHandlerForms(int kind)
        {
            await using var harness = new Harness();
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            switch (kind)
            {
                case 0:
                    endpoint.Status!.OnReadValue = (
                        ISystemContext context, NodeState node, NumericRange range, QualifiedName encoding,
                        ref Variant value, ref StatusCode status, ref DateTimeUtc timestamp) => ServiceResult.Good;
                    break;
                case 1:
                    endpoint.Status!.OnReadValueAsync = (_, _, _, _, _) =>
                        throw new InvalidOperationException("The foreign handler must not be invoked.");
                    break;
                default:
                    endpoint.Status!.OnSimpleReadValueAsync = (_, _, _) =>
                        throw new InvalidOperationException("The foreign handler must not be invoked.");
                    break;
            }
            NodeValueEventHandler? sync = endpoint.Status.OnReadValue;
            NodeValueEventHandlerAsync? asyncFull = endpoint.Status.OnReadValueAsync;
            NodeValueSimpleEventHandlerAsync? asyncSimple = endpoint.Status.OnSimpleReadValueAsync;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(endpoint.Status.OnReadValue, Is.SameAs(sync));
            Assert.That(endpoint.Status.OnReadValueAsync, Is.SameAs(asyncFull));
            Assert.That(endpoint.Status.OnSimpleReadValueAsync, Is.SameAs(asyncSimple));
            Assert.That(endpoint.Status.OnSimpleReadValue, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxEndpointStatusDisposalDetachesCallbacksAndNeverOwnsRuntime(bool disposeManager)
        {
            await using var harness = new Harness();
            IDataSetReader reader = Reader(PubSubState.Disabled)!;
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader);
            NodeValueSimpleEventHandler callback = endpoint.Status!.OnSimpleReadValue!;
            Assert.That(reader.State.TryEnable(), Is.True);
            Assert.That(endpoint.Status.Value, Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));

            if (disposeManager)
            {
                harness.Manager.Dispose();
            }
            else
            {
                binding.Dispose();
            }
            binding.Dispose();
            Assert.That(endpoint.Status.OnSimpleReadValue, Is.Null);
            Assert.That(reader.State.TryMarkOperational(), Is.True);
            Assert.That(endpoint.Status.Value, Is.EqualTo(ConnectionEndpointStatusEnum.PreOperational));
            Variant value = new(42);
            Assert.That(callback(harness.Context, endpoint.Status, ref value).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(value, Is.EqualTo(new Variant(42)));
            if (disposeManager)
            {
                Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            else
            {
                using IDisposable replacement = harness.Manager.BindFxEndpointStatus(
                    harness.Context, endpoint, reader);
                Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(ConnectionEndpointStatusEnum.Operational));
            }
        }

        [Test]
        public async Task FxEndpointStatusRefusesForeignHandlersAndDuplicateBindings()
        {
            await using var harness = new Harness();
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            NodeValueSimpleEventHandler handler = (ISystemContext context, NodeState node, ref Variant value) =>
            {
                value = new Variant(99);
                return ServiceResult.Good;
            };
            endpoint.Status!.OnSimpleReadValue = handler;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(endpoint.Status.OnSimpleReadValue, Is.SameAs(handler));
            endpoint.Status.OnSimpleReadValue = null;
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint);
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint),
                Throws.TypeOf<InvalidOperationException>());
            endpoint.Status.OnSimpleReadValue = handler;
            binding.Dispose();
            Assert.That(endpoint.Status.OnSimpleReadValue, Is.SameAs(handler));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxEndpointStatusRejectsInvalidRuntimeStateWithoutLeavingCallbacks(bool duplex)
        {
            await using var harness = new Harness();
            IDataSetReader reader = Reader((PubSubState)99)!;
            var endpoint = CreateEndpoint(duplex
                ? PubSubConnectionEndpointModeEnum.PublisherSubscriber : PubSubConnectionEndpointModeEnum.Subscriber);
            IDataSetWriter? writer = duplex ? Writer(PubSubState.Disabled) : null;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader, writer),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(endpoint.Status!.OnSimpleReadValue, Is.Null);
            reader.State.Restore(PubSubState.PreOperational);
            using IDisposable binding = harness.Manager.BindFxEndpointStatus(harness.Context, endpoint, reader, writer);
            Assert.That(reader.State.TryMarkOperational(), Is.True);
            Assert.That(ReadStatus(harness, endpoint), Is.EqualTo(duplex
                ? ConnectionEndpointStatusEnum.Ready : ConnectionEndpointStatusEnum.Operational));
        }

        [Test]
        public async Task FxEndpointStatusValidatesInitializedHostAndModeBeforeAttaching()
        {
            await using var harness = new Harness();
            var endpoint = CreateEndpoint(PubSubConnectionEndpointModeEnum.Subscriber);
            Assert.That(() => harness.Manager.BindFxEndpointStatus(null!, endpoint),
                Throws.ArgumentNullException);
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, null!),
                Throws.ArgumentNullException);
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, new(null)),
                Throws.ArgumentException);
            endpoint.Mode!.Value = (PubSubConnectionEndpointModeEnum)0;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(harness.Context, endpoint),
                Throws.ArgumentException);
            endpoint.Mode.Value = PubSubConnectionEndpointModeEnum.Subscriber;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(
                harness.Context, endpoint, writer: Writer(PubSubState.Operational)), Throws.ArgumentException);
            endpoint.Mode.Value = PubSubConnectionEndpointModeEnum.Publisher;
            Assert.That(() => harness.Manager.BindFxEndpointStatus(
                harness.Context, endpoint, Reader(PubSubState.Operational)), Throws.ArgumentException);
            Assert.That(endpoint.Status!.OnSimpleReadValue, Is.Null);
        }

        /// <summary>
        /// Enumerates each cell of the published single-mode and duplex state matrices.
        /// </summary>
        private static IEnumerable<TestCaseData> StatusMatrix()
        {
            PubSubState?[] states =
            [
                null, PubSubState.Disabled, PubSubState.Paused,
                PubSubState.PreOperational, PubSubState.Operational, PubSubState.Error
            ];
            ConnectionEndpointStatusEnum[] singles =
            [
                ConnectionEndpointStatusEnum.Initial, ConnectionEndpointStatusEnum.Ready,
                ConnectionEndpointStatusEnum.Ready, ConnectionEndpointStatusEnum.PreOperational,
                ConnectionEndpointStatusEnum.Operational, ConnectionEndpointStatusEnum.Error
            ];
            // Explicit Figure 31 rows: reader state, then writer state. Missing members dominate every state.
            int[,] duplex =
            {
                { 0, 0, 0, 0, 0, 0 },
                { 0, 1, 1, 1, 1, 4 },
                { 0, 1, 1, 1, 1, 4 },
                { 0, 1, 1, 2, 2, 4 },
                { 0, 1, 1, 2, 3, 4 },
                { 0, 4, 4, 4, 4, 4 }
            };
            for (int i = 0; i < states.Length; i++)
            {
                yield return new TestCaseData(
                    PubSubConnectionEndpointModeEnum.Subscriber, states[i], null, singles[i]);
                yield return new TestCaseData(
                    PubSubConnectionEndpointModeEnum.Publisher, null, states[i], singles[i]);
                for (int j = 0; j < states.Length; j++)
                {
                    yield return new TestCaseData(PubSubConnectionEndpointModeEnum.PublisherSubscriber,
                        states[i], states[j], (ConnectionEndpointStatusEnum)duplex[i, j]);
                }
            }
        }

        /// <summary>
        /// Creates a host-owned generated endpoint with its typed mode and readable Status.
        /// </summary>
        private static PubSubConnectionEndpointState CreateEndpoint(PubSubConnectionEndpointModeEnum mode)
        {
            var endpoint = new PubSubConnectionEndpointState(null) { NodeId = new NodeId("host-endpoint", 1) };
            endpoint.Mode = BaseDataVariableState<PubSubConnectionEndpointModeEnum>
                .With<EnumerationBuilder<PubSubConnectionEndpointModeEnum>>(endpoint);
            endpoint.Mode.Value = mode;
            endpoint.Status = BaseDataVariableState<ConnectionEndpointStatusEnum>
                .With<EnumerationBuilder<ConnectionEndpointStatusEnum>>(endpoint);
            endpoint.Status.DataType = DataTypeIds.Int32;
            endpoint.Status.ValueRank = ValueRanks.Scalar;
            endpoint.Status.AccessLevel = AccessLevels.CurrentRead;
            endpoint.Status.UserAccessLevel = AccessLevels.CurrentRead;
            return endpoint;
        }

        /// <summary>
        /// Supplies a reader association backed by a real state machine, or a missing association.
        /// </summary>
        private static IDataSetReader? Reader(PubSubState? state)
        {
            if (state is null)
            {
                return null;
            }
            var reader = new Mock<IDataSetReader>(MockBehavior.Strict);
            reader.SetupGet(r => r.State).Returns(new PubSubStateMachine(
                "reader", PubSubComponentKind.DataSetReader,
                NUnitTelemetryContext.Create().CreateLogger<PubSubStateMachine>(), state.Value));
            return reader.Object;
        }

        /// <summary>
        /// Supplies a writer association backed by a real state machine, or a missing association.
        /// </summary>
        private static IDataSetWriter? Writer(PubSubState? state)
        {
            if (state is null)
            {
                return null;
            }
            var writer = new Mock<IDataSetWriter>(MockBehavior.Strict);
            writer.SetupGet(w => w.State).Returns(new PubSubStateMachine(
                "writer", PubSubComponentKind.DataSetWriter,
                NUnitTelemetryContext.Create().CreateLogger<PubSubStateMachine>(), state.Value));
            return writer.Object;
        }

        /// <summary>
        /// Reads the projected enumeration through the node's ordinary Value attribute hook.
        /// </summary>
        private static ConnectionEndpointStatusEnum ReadStatus(Harness harness, PubSubConnectionEndpointState endpoint)
        {
            var value = new DataValue();
            ServiceResult result = endpoint.Status!.ReadAttribute(
                harness.Context, Attributes.Value, default, default, ref value);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ConnectionEndpointStatusEnum status), Is.True);
            return status;
        }
    }
}
