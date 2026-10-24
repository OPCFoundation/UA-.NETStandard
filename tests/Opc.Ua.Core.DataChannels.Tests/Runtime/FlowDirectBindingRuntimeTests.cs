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
            pair.InitiatorCarrier.ExporterUnavailable = true;
            await pair.StartAsync().ConfigureAwait(false);

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Acceptor.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false))
                .ConfigureAwait(false);

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
            pair.InitiatorCarrier.ExporterUnavailable = true;
            await pair.StartAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await pair.Acceptor.OpenAsync(
                    FlowLoopback.Parameters(), cancellationToken: cancellation.Token).ConfigureAwait(false));

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
