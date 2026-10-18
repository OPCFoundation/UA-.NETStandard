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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;
using DataChannelCarrierReservation = Opc.Ua.DataChannels.DataChannelApplicationBudget.DataChannelCarrierReservation;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowResourceTests
    {
        /// <summary>
        /// DATA cannot borrow another context's control guarantee, including while a closed owner drains its controls.
        /// </summary>
        [Test]
        public void ControlHeadroomRemainsOwnedAcrossSaturationAndClosure()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = 2 * DataChannelApplicationBudget.ControlHeadroomBytes,
                MaxMessages = 2 * DataChannelApplicationBudget.ControlHeadroomMessages
            });
            Assert.That(budget.TryReserveControlHeadroom(out DataChannelApplicationBudget.ControlHeadroom? firstLease), Is.True);
            Assert.That(budget.TryReserveControlHeadroom(out DataChannelApplicationBudget.ControlHeadroom? secondLease), Is.True);
            using DataChannelApplicationBudget.ControlHeadroom first = firstLease!;
            using DataChannelApplicationBudget.ControlHeadroom second = secondLease!;
            Assert.That(budget.TryReserveMessage(1), Is.False);
            Assert.That(budget.TryReserveCarrier(1, 0, 0, out _), Is.False);
            Assert.That(first.TryReserve(DataChannelApplicationBudget.ControlHeadroomBytes), Is.True);
            Assert.That(first.TryReserve(1), Is.False, "The other context's headroom is not a shared overflow pool.");
            Assert.That(second.TryReserve(DataChannelApplicationBudget.ControlHeadroomBytes), Is.True);
            Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(2 * DataChannelApplicationBudget.ControlHeadroomBytes));

            first.Dispose();
            Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(2 * DataChannelApplicationBudget.ControlHeadroomBytes),
                "Closure does not reclaim an active control.");
            first.Release(DataChannelApplicationBudget.ControlHeadroomBytes);
            Assert.That(budget.TryReserveMessage(DataChannelApplicationBudget.ControlHeadroomBytes), Is.True);
            Assert.That(budget.TryReserveMessage(1), Is.False);
            budget.Release(FlowResource.BufferedBytes, DataChannelApplicationBudget.ControlHeadroomBytes);
            budget.Release(FlowResource.Message, 1);
            second.Release(DataChannelApplicationBudget.ControlHeadroomBytes);
            second.Dispose();
            Assert.That(budget.TryReserveMessage(2 * DataChannelApplicationBudget.ControlHeadroomBytes), Is.True,
                "Unused headroom returns only after its owner closes.");
            budget.Release(FlowResource.BufferedBytes, 2 * DataChannelApplicationBudget.ControlHeadroomBytes);
            budget.Release(FlowResource.Message, 1);
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        /// <summary>
        /// Neither first directional barrier can retire the opposite direction of the newly admitted revision.
        /// </summary>
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.Quic)]
        public async Task FirstDirectionalBarrierRetainsBothNewRevisionWindows(DataChannelCarrierKind carrier)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, carrier) == FlowOperation.Commit;
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            Task<DataChannelResult> updating = flow.Local.UpdateAsync(proposal, 1).AsTask();
            try
            {
                await pair.AcceptorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                FlowRevision opener = flow.Local.StateData.Revisions[2];
                FlowRevision acceptor = flow.Remote.StateData.Revisions[2];
                Assert.Multiple(() =>
                {
                    Assert.That(opener.SendActive, Is.True);
                    Assert.That(opener.ReceiveActive, Is.False);
                    Assert.That(opener.Pending, Is.True);
                    Assert.That(acceptor.SendActive, Is.False);
                    Assert.That(acceptor.ReceiveActive, Is.True);
                    Assert.That(acceptor.Pending, Is.True);
                    Assert.That(opener.ReservedSend, Is.EqualTo(1024));
                    Assert.That(opener.ReservedReceive, Is.EqualTo(1024));
                    Assert.That(acceptor.ReservedSend, Is.EqualTo(1024));
                    Assert.That(acceptor.ReservedReceive, Is.EqualTo(1024));
                    Assert.That(pair.InitiatorBudget.Usage.ReservedBytes, Is.EqualTo(3072));
                    Assert.That(pair.AcceptorBudget.Usage.ReservedBytes, Is.EqualTo(3072));
                });
            }
            finally
            {
                pair.AcceptorCarrier.BeforeRelease.TrySetResult(true);
            }
            Assert.That(await updating.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// A saturated application DATA budget still admits the controls that revise and release old receive windows.
        /// </summary>
        [TestCase(DataChannelCarrierKind.SharedUasc, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, true)]
        [TestCase(DataChannelCarrierKind.Quic, false)]
        [TestCase(DataChannelCarrierKind.Quic, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, true)]
        public async Task DataSaturationLeavesManagementAndRevisionHeadroom(
            DataChannelCarrierKind carrier, bool byteLimit)
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = 4096,
                MaxMessages = 8
            });
            await using var pair = new FlowLoopback(carrier, initiatorBudget: budget);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(fromInitiator: false).ConfigureAwait(false);
            DataChannel sender = flow.Local;
            DataChannel receiver = flow.Remote;
            int count = byteLimit ? 1 : 8 - DataChannelApplicationBudget.ControlHeadroomMessages;
            for (int index = 0; index < count; index++)
            {
                Assert.That(await sender.SendAsync(ByteString.Empty).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            using DataChannelMessage? retained = await receiver.ReadAsync().ConfigureAwait(false);
            Assert.That(retained!.Revision, Is.EqualTo(1));
            await FlowLoopback.EventuallyAsync(() => budget.Usage.Messages == count).ConfigureAwait(false);

            DataChannelCarrierReservation? carrierLease = null;
            if (byteLimit)
            {
                long available = 4096 - DataChannelApplicationBudget.ControlHeadroomBytes - budget.Usage.BufferedBytes;
                Assert.That(budget.TryReserveCarrier(available, 0, 0, out carrierLease), Is.True);
            }
            using (carrierLease)
            {
                Assert.That(budget.TryReserveMessage(1), Is.False);
                Assert.That(await pair.Acceptor.PingAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                DataChannelParametersDataType update = sender.Parameters;
                update.Priority = 1;
                Assert.That(await sender.UpdateAsync(update, 1).AsTask().WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                await FlowLoopback.EventuallyAsync(() =>
                    receiver.Revision == 2 && receiver.GetOperationStatus(1).PendingRevision == 0)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(budget.Usage.ReservedBytes, Is.EqualTo(3072),
                        "The old application-owned receive revision remains charged alongside both new directions.");
                    Assert.That(budget.Usage.BufferedBytes, Is.LessThanOrEqualTo(4096));
                    Assert.That(budget.Usage.Messages, Is.LessThanOrEqualTo(8));
                    Assert.That(retained.Revision, Is.EqualTo(1));
                    Assert.That(pair.Initiator.IsReady, Is.True);
                });
            }
        }

        /// <summary>
        /// Partial native records in independent contexts share admission before allocating declared bodies.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task PartialDecodersShareApplicationStorageAndReleaseEveryParserOnRejection(
            DataChannelCarrierKind carrier)
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxBufferedBytes = 4096 });
            await using var first = new FlowLoopback(carrier, initiatorBudget: budget);
            await using var second = new FlowLoopback(carrier, initiatorBudget: budget);
            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => budget.Usage.Messages == 0).ConfigureAwait(false);
            var header = ByteString.FromHexString("D1000000010100000001000000");
            Assert.That(await first.Initiator.ReceiveStreamAsync(4, header[..1]).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Initiator.ReceiveStreamAsync(4, header[..1]).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(4 * FlowStreamInput.ParserStorageBytes));
            const int heldBytes = 4096 -
                (4 * FlowStreamInput.ParserStorageBytes) -
                (2 * DataChannelApplicationBudget.ControlHeadroomBytes) -
                128;
            Assert.That(budget.TryReserveCarrier(heldBytes, 0, 0, out DataChannelCarrierReservation? lease), Is.True);
            using (lease)
            {
                Assert.That(await first.Initiator.ReceiveStreamAsync(4, header[1..]).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                const long reserved = heldBytes + (4 * FlowStreamInput.ParserStorageBytes) + (3 * 213);
                Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(reserved));
                Task<DataChannelResult> blocked = second.Initiator.ReceiveStreamAsync(4, header[1..]).AsTask();
                Assert.That(blocked.IsCompleted, Is.False);
                Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(reserved),
                    "A second declared body cannot be retained without application admission.");

                first.Clock.Advance(first.Options.OpenTimeout);
                Assert.That(await blocked.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(budget.Usage.BufferedBytes,
                    Is.EqualTo(heldBytes + (3 * FlowStreamInput.ParserStorageBytes) + (3 * 213)));
                Assert.That(await second.Initiator.ReceiveStreamAsync(4, ByteString.Empty, endOfStream: true)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(heldBytes + (2 * FlowStreamInput.ParserStorageBytes)));
                Assert.That(budget.Usage.Messages, Is.Zero);
            }
            await first.DisposeAsync().ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        [Test]
        public async Task DependencyInjectionRequiresExplicitPolicyAndSharesOneApplicationBudget()
        {
            var missing = new ServiceCollection();
            missing.AddLogging();
            missing.AddOpcUa().AddDataChannels();
            using (ServiceProvider noPolicy = missing.BuildServiceProvider())
            {
                Assert.Throws<InvalidOperationException>(() =>
                    noPolicy.GetRequiredService<IDataChannelContextFactory>());
            }
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddSingleton<IDataChannelAuthorizer>(new TestFlowAuthorizer());
            services.AddOpcUa().AddDataChannels(applicationLimits:
                new DataChannelApplicationLimits { MaxContexts = 1 });
            using ServiceProvider provider = services.BuildServiceProvider();
            IDataChannelContextFactory factory = provider.GetRequiredService<IDataChannelContextFactory>();
            var firstCarrier = new TestFlowCarrier(DataChannelCarrierKind.SharedUasc, true, new TestFlowAssociation());
            var secondCarrier = new TestFlowCarrier(DataChannelCarrierKind.SharedUasc, true, new TestFlowAssociation());
            await using DataChannelContext context = factory.Create(firstCarrier);
            DataChannelException? failure = Assert.Throws<DataChannelException>(() => factory.Create(secondCarrier));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(provider.GetRequiredService<DataChannelApplicationBudget>().Usage.Contexts, Is.EqualTo(1));
            Assert.That(context.IsReady, Is.False);
        }

        [Test]
        public void ProtocolDefaultsAreTheFrozenCompleteMessageDefaults()
        {
            var options = new DataChannelOptions();
            Assert.Multiple(() =>
            {
                Assert.That(options.MaxFlows, Is.EqualTo(128));
                Assert.That(options.MaxMessageSize, Is.EqualTo(16777216));
                Assert.That(options.MaxControlBytes, Is.EqualTo(65536));
                Assert.That(options.MaxReceiveBytes, Is.EqualTo(33554432));
                Assert.That(options.MaxQueueMessages, Is.EqualTo(1024));
                Assert.That(options.OpenTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(options.UpdateTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(options.DrainTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(options.PingTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(options.GrantLifetime, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(options.AuthorizationRecheckInterval, Is.EqualTo(TimeSpan.FromSeconds(60)));
                Assert.That(options.DatagramRevisionGrace, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(options.AllowIntegrity, Is.False);
            });
        }

        [TestCase(100, 1, 1, true)]
        [TestCase(101, 1, 1, false)]
        [TestCase(100, 2, 1, false)]
        [TestCase(100, 1, 2, false)]
        public void CarrierReservationsAreAtomicAcrossEveryDimensionAndDisposeExactlyOnce(
            long bytes, int streams, int operations, bool expected)
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = 100,
                MaxStreams = 1,
                MaxOperations = 1
            });
            bool reserved = budget.TryReserveCarrier(
                bytes, streams, operations, out DataChannelCarrierReservation? lease);
            Assert.That(reserved, Is.EqualTo(expected));
            if (lease != null)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(bytes));
                    Assert.That(budget.Usage.Streams, Is.EqualTo(streams));
                    Assert.That(budget.Usage.Operations, Is.EqualTo(operations));
                });
                lease.Dispose();
                lease.Dispose();
            }
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        [Test]
        public async Task FlowAdmissionIsAggregatedAcrossIndependentContextsOfOneApplication()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxFlows = 1 });
            await using var first = new FlowLoopback(initiatorBudget: budget);
            await using var second = new FlowLoopback(initiatorBudget: budget);
            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            FlowPair active = await first.OpenAsync().ConfigureAwait(false);
            DataChannelException? failure = Assert.ThrowsAsync<DataChannelException>(
                async () => await second.Initiator.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(budget.Usage.Contexts, Is.EqualTo(2));
                Assert.That(budget.Usage.Flows, Is.EqualTo(1));
                Assert.That(second.Initiator.Channels.IsEmpty, Is.True);
            });
            await active.Local.ResetAsync().ConfigureAwait(false);
            FlowPair admitted = await second.OpenAsync().ConfigureAwait(false);
            Assert.That(admitted.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(budget.Usage.Flows, Is.EqualTo(1));
        }

        [Test]
        public async Task RevisionAdmissionReservesOldAndProposedWindowsInsteadOfOvercommitting()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 256,
                MaxReceiveBytes = 1024,
                MaxContextBytes = 1024
            };
            await using var pair = new FlowLoopback(options: options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxReceiveBytes = 512;
            proposal.MaxMessageSize = 128;
            Assert.That(await flow.Local.UpdateAsync(proposal, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.Revision, Is.EqualTo(1));
                Assert.That(flow.Local.GetStatus().PendingRevision, Is.Zero);
                Assert.That(pair.InitiatorBudget.Usage.ReservedBytes, Is.EqualTo(2048));
            });
        }

        [Test]
        public async Task MessageCountLimitIncludesEmptyMessagesAndDoesNotMutateFailedSend()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 2
            };
            await using var pair = new FlowLoopback(options: options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            Task<DataChannelResult> first = flow.Local.SendAsync(ByteString.Empty).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task<DataChannelResult> second = flow.Local.SendAsync(ByteString.Empty).AsTask();
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.EqualTo(34));
            Assert.That(pair.InitiatorBudget.Usage.Messages, Is.EqualTo(2));
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using DataChannelMessage? one = await flow.Remote.ReadAsync().ConfigureAwait(false);
            using DataChannelMessage? two = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(one!.MessageId, Is.EqualTo(1));
            Assert.That(two!.MessageId, Is.EqualTo(2));
        }

        [Test]
        public async Task OneOrderedNativeStreamCanBackpressureWithoutBlockingAnotherFlow()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1
            };
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair blocked = await pair.OpenAsync().ConfigureAwait(false);
            await blocked.Local.SendAsync(ByteString.Empty).ConfigureAwait(false);
            Task<DataChannelResult> pending = blocked.Local.SendAsync(ByteString.Empty).AsTask();
            FlowPair independent = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await independent.Local.SendAsync(new ByteString(new byte[] { 3 })).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? other = await independent.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(other!.Payload[0], Is.EqualTo(3));
            Assert.That(pending.IsCompleted, Is.False);
            using (DataChannelMessage? release = await blocked.Remote.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(release!.MessageId, Is.EqualTo(1));
            }
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? last = await blocked.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(last!.MessageId, Is.EqualTo(2));
        }

        [Test]
        public async Task DataIdentifierExhaustionCannotWrapOrFaultAnUnrelatedFlow()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair exhausted = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync().ConfigureAwait(false);
            // Reach the UInt32 boundary without sending four billion otherwise identical records.
            exhausted.Local.StateData.LastDataId = uint.MaxValue;
            Assert.That(await exhausted.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(exhausted.Local.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(await other.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? unaffected = await other.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(unaffected!.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task MaximumOperationIdentifierCommitsOnceThenRefusesWrap()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            Assert.That(await flow.Local.UpdateAsync(proposal, uint.MaxValue).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            proposal.Priority = 2;
            Assert.That(await flow.Local.UpdateAsync(proposal).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(flow.Local.Revision, Is.EqualTo(2));
            Assert.That(flow.Local.Parameters.Priority, Is.EqualTo(1));
        }
    }
}
