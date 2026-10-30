// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Exercises the protected FLC direct-binding handshake over the shared runtime.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowDirectBindingRuntimeTests
    {
        /// <summary>
        /// Requires both authenticated role proofs before direct binding becomes ready.
        /// </summary>
        [Test]
        public async Task DirectBindingRequiresBothRoleProofs()
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true);
            await pair.StartAsync().ConfigureAwait(false);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            DataChannelResult[] results = await Task.WhenAll(
                pair.Initiator.WaitForDirectBindingAsync(deadline.Token).AsTask(),
                pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).AsTask()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.All.EqualTo(DataChannelResult.Good));
                Assert.That(pair.Initiator.IsDirectBindingReady, Is.True);
                Assert.That(pair.Acceptor.IsDirectBindingReady, Is.True);
                Assert.That(pair.InitiatorCarrier.LastExporterMaterial, Is.All.Zero);
                Assert.That(pair.AcceptorCarrier.LastExporterMaterial, Is.All.Zero);
            });
        }

        /// <summary>
        /// An acceptor opening before the initiator proof waits without selecting a baseline-only flow.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public async Task AcceptorOpenWaitsForDelayedInitiatorProof(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(
                kind, enableDirectBinding: true, enableDirectStreams: true);
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, kind) == FlowOperation.Bind;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(deadline.Token).ConfigureAwait(false);

            Task<DataChannel> opening = pair.Acceptor.OpenAsync(
                FlowLoopback.Parameters(), cancellationToken: deadline.Token).AsTask();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(opening.IsCompleted, Is.False);
                    Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero,
                        "No flow may snapshot baseline delivery while direct selection is still pending.");
                    Assert.That(pair.AcceptorPolicy.Requests, Is.Empty);
                });
            }
            finally
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            }

            await using DataChannel source = await opening.ConfigureAwait(false);
            await using DataChannel? sink = await pair.Initiator.AcceptAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(sink, Is.Not.Null);
            Assert.That(await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 0, 255 }));
            Assert.That(await source.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await sink!.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
            Assert.That(pair.AcceptorCarrier.Writes.Any(write => write.IsData), Is.False,
                "The bound direct flow must not send DATA through baseline FLO.");
        }

        /// <summary>
        /// An initiator without exporter material explicitly declines direct delivery so the acceptor can use FLO.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, false)]
        [TestCase(DataChannelCarrierKind.QuicUasc, true)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, false)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, true)]
        public async Task UnsupportedInitiatorExporterAllowsAcceptorOpenedBaselineFlow(
            DataChannelCarrierKind kind,
            bool exporterFailure)
        {
            await using var pair = new FlowLoopback(
                kind, enableDirectBinding: true, enableDirectStreams: true);
            if (exporterFailure)
            {
                pair.InitiatorCarrier.ExporterUnavailable = true;
            }
            else
            {
                pair.InitiatorCarrier.IsAvailable = false;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);

            DataChannelResult[] bindingResults = await Task.WhenAll(
                pair.Initiator.WaitForDirectBindingAsync(deadline.Token).AsTask(),
                pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).AsTask()).ConfigureAwait(false);
            FlowRecord refusal = pair.InitiatorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, kind))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowOutcome
                {
                    Operation: FlowOperation.Status
                });

            Assert.Multiple(() =>
            {
                Assert.That(bindingResults, Is.All.EqualTo(DataChannelResult.Unsupported));
                Assert.That(((FlowOutcome)FlowControlCodec.Decode(in refusal, new())).Status,
                    Is.EqualTo(FlowWireStatus.Unsupported));
            });

            Task<DataChannel?> accepting = pair.Initiator.AcceptAsync(deadline.Token).AsTask();
            await using DataChannel source = await pair.Acceptor.OpenAsync(
                FlowLoopback.Parameters(), cancellationToken: deadline.Token).ConfigureAwait(false);
            await using DataChannel? sink = await accepting.ConfigureAwait(false);
            Assert.That(sink, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(source.StateData.DirectLane, Is.Null);
                Assert.That(sink!.StateData.DirectLane, Is.Null);
            });
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 7, 1, 9 }));
            Assert.That(await source.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await sink!.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// An acceptor accepts an exact unsupported declaration once and rejects a changed replay.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedInitiatorStatusReplayIsBoundToOriginalOutcome(bool conflicting)
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc, enableDirectBinding: true);
            pair.InitiatorCarrier.IsAvailable = false;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            FlowRecord refusal = pair.InitiatorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowOutcome
                {
                    Operation: FlowOperation.Status
                });
            FlowControl status = conflicting
                ? new FlowOutcome(FlowOperation.Status, 0, FlowWireStatus.Timeout)
                : FlowControlCodec.Decode(in refusal, new());

            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                status, refusal.Revision, refusal.MessageId).ConfigureAwait(false),
                Is.EqualTo(conflicting ? DataChannelResult.SecurityFailure : DataChannelResult.Good));
            Assert.That(pair.Acceptor.IsReady, Is.EqualTo(!conflicting));
        }

        /// <summary>
        /// Reports the binding wait deadline as a data-channel timeout without creating flow state.
        /// </summary>
        [Test]
        public async Task PendingDirectBindingOpenTimeoutIsReportedAsTimeout()
        {
            var options = new DataChannelOptions
            {
                OpenTimeout = TimeSpan.FromMilliseconds(200)
            };
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc,
                options,
                enableDirectBinding: true,
                enableDirectStreams: true);
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Bind;
            await pair.StartAsync().ConfigureAwait(false);
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            DataChannelException? exception;
            try
            {
                exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await pair.Acceptor.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            finally
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            }

            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(pair.Acceptor.IsReady, Is.True);
            Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
            Assert.That(pair.AcceptorBudget.Usage.Operations, Is.Zero);
        }

        /// <summary>
        /// Preserves caller cancellation when it occurs before the direct-binding deadline.
        /// </summary>
        [Test]
        public async Task CallerCancellationWhileWaitingForDirectBindingRemainsCancellation()
        {
            var options = new DataChannelOptions
            {
                OpenTimeout = TimeSpan.FromSeconds(5)
            };
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc,
                options,
                enableDirectBinding: true,
                enableDirectStreams: true);
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Bind;
            await pair.StartAsync().ConfigureAwait(false);
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try
            {
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pair.Acceptor.OpenAsync(
                        FlowLoopback.Parameters(), cancellationToken: cancellation.Token).ConfigureAwait(false));
            }
            finally
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            }

            Assert.That(pair.Acceptor.IsReady, Is.True);
            Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
            Assert.That(pair.AcceptorBudget.Usage.Operations, Is.Zero);
        }

        /// <summary>
        /// Replays the authenticated recorded response after exporter destruction without reopening the handshake.
        /// </summary>
        [Test]
        public async Task ExactBindingResponseReplayPreservesReadyContextAfterExporterErasure()
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc, enableDirectBinding: true);
            await pair.StartAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            FlowRecord response = pair.AcceptorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
            FlowControl binding = FlowControlCodec.Decode(in response, new());
            Assert.That(pair.InitiatorCarrier.LastExporterMaterial, Is.All.Zero);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                binding, response.Revision, response.MessageId).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(pair.Initiator.IsDirectBindingReady, Is.True);
            FlowPair flow = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// A repeated initiator transaction reuses the recorded acceptor proof, not the erased exporter key.
        /// </summary>
        [Test]
        public async Task ExactBindingRequestReplayResendsRecordedProofWithoutExporter()
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc, enableDirectBinding: true);
            await pair.StartAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            FlowRecord request = pair.InitiatorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
            FlowControl binding = FlowControlCodec.Decode(in request, new());
            pair.AcceptorCarrier.GateAfterDelivery = write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Bind;
            try
            {
                Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                    binding, request.Revision, request.MessageId).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                await pair.AcceptorCarrier.Delivered.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(pair.Initiator.IsDirectBindingReady, Is.True);
                    Assert.That(pair.Acceptor.IsDirectBindingReady, Is.True);
                    Assert.That(pair.InitiatorCarrier.LastExporterMaterial, Is.All.Zero);
                    Assert.That(pair.AcceptorCarrier.LastExporterMaterial, Is.All.Zero);
                    Assert.That(pair.AcceptorCarrier.Writes.Count(write =>
                        TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Bind),
                        Is.EqualTo(2));
                });
            }
            finally
            {
                pair.AcceptorCarrier.DeliveryRelease.TrySetResult(true);
            }
        }

        /// <summary>
        /// An exact retry retains an unsupported outcome even if the exporter later becomes available.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, false)]
        [TestCase(DataChannelCarrierKind.QuicUasc, true)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, false)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, true)]
        public async Task DeclinedBindingRequestReplayRetainsStatus(
            DataChannelCarrierKind kind, bool providerUnavailable)
        {
            await using var pair = new FlowLoopback(kind, enableDirectBinding: true);
            pair.AcceptorCarrier.IsAvailable = !providerUnavailable;
            pair.AcceptorCarrier.ExporterUnavailable = !providerUnavailable;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            FlowRecord request = pair.InitiatorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, kind))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
            FlowControl binding = FlowControlCodec.Decode(in request, new());
            CapturedFlowWrite original = pair.AcceptorCarrier.Writes.Single(write =>
                TestFlowCarrier.Operation(write, kind) == FlowOperation.Status);

            pair.AcceptorCarrier.IsAvailable = true;
            pair.AcceptorCarrier.ExporterUnavailable = false;
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                binding, request.Revision, request.MessageId).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Count(write =>
                TestFlowCarrier.Operation(write, kind) == FlowOperation.Status) == 2).ConfigureAwait(false);
            CapturedFlowWrite replay = pair.AcceptorCarrier.Writes.Last(write =>
                TestFlowCarrier.Operation(write, kind) == FlowOperation.Status);
            Assert.Multiple(() =>
            {
                Assert.That(replay.Payload, Is.EqualTo(original.Payload));
                Assert.That(replay.Path, Is.EqualTo(DataChannelPath.Management));
                Assert.That(replay.ChannelId, Is.Zero);
                Assert.That(replay.StreamId, Is.Zero);
                Assert.That(replay.RequestId, Is.Zero);
                Assert.That(pair.AcceptorCarrier.LastExporterMaterial, Is.Null,
                    "A recorded refusal must not invoke the newly available exporter.");
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
                Assert.That(pair.Initiator.IsDirectBindingReady, Is.False);
                Assert.That(pair.Acceptor.IsDirectBindingReady, Is.False);
            });

            FlowPair flow = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// A refused request still owns its transaction and rejects altered or replacement proofs.
        /// </summary>
        [TestCase(false, "proof")]
        [TestCase(false, "digest")]
        [TestCase(false, "role")]
        [TestCase(false, "operation")]
        [TestCase(true, "proof")]
        [TestCase(true, "digest")]
        [TestCase(true, "role")]
        [TestCase(true, "operation")]
        public async Task ConflictingDeclinedBindingRequestClosesContext(bool providerUnavailable, string changed)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc, enableDirectBinding: true);
            pair.AcceptorCarrier.IsAvailable = !providerUnavailable;
            pair.AcceptorCarrier.ExporterUnavailable = !providerUnavailable;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            FlowRecord request = pair.InitiatorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
            var binding = (FlowBinding)FlowControlCodec.Decode(in request, new());
            var conflicting = new FlowBinding(
                changed == "role" ? (byte)1 : binding.Role,
                changed == "digest" ? new ByteString(new byte[32]) : binding.TranscriptDigest,
                changed == "proof" ? new ByteString(new byte[32]) : binding.Proof);

            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                conflicting, request.Revision, changed == "operation" ? request.MessageId + 1 : request.MessageId)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.SecurityFailure));
            Assert.That(pair.Acceptor.IsReady, Is.False);
            Assert.That(pair.Acceptor.IsDirectBindingReady, Is.False);
            Assert.That(pair.AcceptorCarrier.LastExporterMaterial, Is.Null);
        }

        /// <summary>
        /// The initiator rejects a changed outcome for an already declined binding transaction.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public async Task ConflictingDeclinedBindingStatusClosesContext(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind, enableDirectBinding: true);
            pair.AcceptorCarrier.ExporterUnavailable = true;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            FlowRecord status = pair.AcceptorCarrier.Writes
                .Where(write => TestFlowCarrier.Operation(write, kind) == FlowOperation.Status)
                .Select(write => TestFlowCarrier.Record(write, kind))
                .Single();

            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowOutcome(FlowOperation.Status, 0, FlowWireStatus.Timeout), status.Revision, status.MessageId)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.SecurityFailure));
            Assert.That(pair.Initiator.IsReady, Is.False);
            Assert.That(pair.Initiator.IsDirectBindingReady, Is.False);
            Assert.That(pair.InitiatorCarrier.LastExporterMaterial, Is.All.Zero);
        }

        /// <summary>
        /// A completed binding accepts only its exact recorded response, never altered proofs or outcomes.
        /// </summary>
        [TestCase("proof")]
        [TestCase("digest")]
        [TestCase("role")]
        [TestCase("operation")]
        [TestCase("status")]
        public async Task ConflictingBindingResponseReplayClosesContext(string changed)
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc, enableDirectBinding: true);
            await pair.StartAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            FlowRecord response = pair.AcceptorCarrier.Writes
                .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
            var binding = (FlowBinding)FlowControlCodec.Decode(in response, new());
            FlowControl conflicting = changed == "status"
                ? new FlowOutcome(FlowOperation.Status, 0, FlowWireStatus.Unsupported)
                : new FlowBinding(
                    changed == "role" ? (byte)0 : binding.Role,
                    changed == "digest" ? new ByteString(new byte[32]) : binding.TranscriptDigest,
                    changed == "proof" ? new ByteString(new byte[32]) : binding.Proof);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                conflicting, response.Revision, changed == "operation" ? response.MessageId + 1 : response.MessageId)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.SecurityFailure));
            Assert.That(pair.Initiator.IsReady, Is.False);
            Assert.That(pair.Initiator.IsDirectBindingReady, Is.False);
        }

        /// <summary>
        /// A differing endpoint path changes the locally computed transcript and closes the context.
        /// </summary>
        [Test]
        public async Task DirectBindingRejectsTranscriptMismatch()
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true);
            pair.AcceptorCarrier.Path = "/ua/quic?different=1";
            await pair.StartAsync().ConfigureAwait(false);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            DataChannelResult[] results = await Task.WhenAll(
                pair.Initiator.WaitForDirectBindingAsync(deadline.Token).AsTask(),
                pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).AsTask()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.All.EqualTo(DataChannelResult.SecurityFailure));
                Assert.That(pair.Initiator.IsDirectBindingReady, Is.False);
                Assert.That(pair.Acceptor.IsDirectBindingReady, Is.False);
            });
        }

        /// <summary>
        /// An unavailable exporter explicitly refuses only direct binding and preserves configured FLO.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task MissingExporterLeavesBaselineFlowUsable(bool initiatorExporterUnavailable)
        {
            await using var pair = new FlowLoopback(
                DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true);
            if (initiatorExporterUnavailable)
            {
                pair.InitiatorCarrier.ExporterUnavailable = true;
            }
            else
            {
                pair.AcceptorCarrier.ExporterUnavailable = true;
            }
            await pair.StartAsync().ConfigureAwait(false);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(
                await pair.Initiator.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Unsupported));
            if (!initiatorExporterUnavailable)
            {
                Assert.That(
                    await pair.Acceptor.WaitForDirectBindingAsync(deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Unsupported));
                await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Status))
                    .ConfigureAwait(false);
                CapturedFlowWrite statusWrite = pair.AcceptorCarrier.Writes.Single(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Status);
                FlowRecord statusRecord = TestFlowCarrier.Record(
                    statusWrite, DataChannelCarrierKind.QuicUasc);
                FlowControl statusControl = FlowControlCodec.Decode(in statusRecord, new());
                FlowRecord bindingRequest = pair.InitiatorCarrier.Writes
                    .Select(write => TestFlowCarrier.Record(write, DataChannelCarrierKind.QuicUasc))
                    .Single(record => FlowControlCodec.Decode(in record, new()) is FlowBinding);
                Assert.Multiple(() =>
                {
                    Assert.That(statusWrite.Path, Is.EqualTo(DataChannelPath.Management));
                    Assert.That(statusWrite.ChannelId, Is.Zero);
                    Assert.That(statusWrite.StreamId, Is.Zero);
                    Assert.That(statusWrite.RequestId, Is.Zero);
                    Assert.That(statusWrite.IsData, Is.False);
                    Assert.That(statusRecord.Revision, Is.Zero);
                    Assert.That(statusRecord.MessageId, Is.EqualTo(bindingRequest.MessageId));
                    Assert.That(statusControl, Is.TypeOf<FlowOutcome>());
                    Assert.That(statusControl.Operation, Is.EqualTo(FlowOperation.Status));
                    Assert.That(statusControl.Flow, Is.Zero);
                    Assert.That(((FlowOutcome)statusControl).Status, Is.EqualTo(FlowWireStatus.Unsupported));
                });
            }
            FlowPair flow = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x01, 0x02, 0x03 }));

            Assert.That(
                await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// Refuses association controls when the context has not selected direct binding and preserves baseline FLO.
        /// </summary>
        [Test]
        public async Task UnselectedDirectAssociationCannotChangeFlowReadiness()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            var token = new Guid("11112233-4455-6677-8899-aabbccddeeff");

            DataChannelResult associate = await pair.AcceptorCarrier.InjectControlAsync(
                new FlowAssociation(FlowOperation.Associate, flow.Remote.ChannelId, 0, token),
                flow.Remote.Revision,
                100).ConfigureAwait(false);
            DataChannelResult acknowledgement = await pair.InitiatorCarrier.InjectControlAsync(
                new FlowAssociation(FlowOperation.AssociateAck, flow.Local.ChannelId, 0, token),
                flow.Local.Revision,
                100).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(associate, Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(acknowledgement, Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });

            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x01, 0x02, 0x03 }));
            Assert.That(
                await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }
    }
}
