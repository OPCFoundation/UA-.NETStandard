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
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowSendAdmissionTests
    {
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        [Repeat(20)]
        public async Task PositiveRecheckWaitsWithoutConsumingAnIdentifierOrChangingPublicRefusal(
            DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            if (kind != DataChannelCarrierKind.DedicatedUasc)
            {
                await pair.OpenAsync().ConfigureAwait(false);
            }
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.InitiatorPolicy.Handler = async (request, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return new DataChannelAuthorization(DataChannelResult.Good, request.Parameters);
            };
            Task recheck = pair.Initiator.RecheckAuthorizationAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task<DataChannelResult> pending = flow.Local.SendWithAdmissionAsync(
                new ByteString(new byte[] { 12, 34 })).AsTask();
            try
            {
                Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.Multiple(() =>
                {
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
                    Assert.That(flow.Local.StateData.SendMessages, Is.Zero);
                    Assert.That(flow.Local.StateData.SendBytes, Is.Zero);
                    Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
                });
            }
            finally
            {
                release.TrySetResult(true);
                await recheck.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(await pending.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(first!.MessageId, Is.EqualTo(1));
            Assert.That(first.Payload.ToArray(), Is.EqualTo(new byte[] { 12, 34 }));
            Assert.That(await flow.Local.SendWithAdmissionAsync(new ByteString("8"u8.ToArray()))
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? second = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(second!.MessageId, Is.EqualTo(2));
            Assert.That(second.Payload.ToArray(), Is.EqualTo("8"u8.ToArray()));
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorBudget.Usage.Messages == 0).ConfigureAwait(false);
            Assert.That(pair.InitiatorBudget.Usage.Messages, Is.Zero);
        }

        [Test]
        [Repeat(20)]
        public async Task QueueAdmissionResumesAfterTheExistingWriterReleasesItsReservation()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1
            };
            await using var pair = new FlowLoopback(options: options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateAfterDelivery = write => write.IsData;
            Task<DataChannelResult> first = flow.Local.SendAsync(new ByteString(new byte[] { 1 })).AsTask();
            await pair.InitiatorCarrier.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task<DataChannelResult> pending = flow.Local.SendWithAdmissionAsync(
                new ByteString(new byte[] { 2 })).AsTask();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(flow.Local.StateData.LastDataId, Is.EqualTo(1));
                    Assert.That(pair.InitiatorBudget.Usage.Messages, Is.EqualTo(1));
                    Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.EqualTo(18));
                });
                // Free the receiver's single slot before testing admission of another sender-owned message.
                using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
                Assert.That(received!.MessageId, Is.EqualTo(1));
                Assert.That(received.Payload.ToArray(), Is.EqualTo(new byte[] { 1 }));
            }
            finally
            {
                pair.InitiatorCarrier.DeliveryRelease.TrySetResult(true);
                await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? last = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(last!.MessageId, Is.EqualTo(2));
            Assert.That(last.Payload.ToArray(), Is.EqualTo(new byte[] { 2 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Repeat(20)]
        public async Task ApplicationAdmissionWakesWhenAnIndependentContextReleasesCapacity(bool byteLimit)
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = byteLimit ? 4096 : 65536,
                MaxMessages = 16
            });
            await using var first = new FlowLoopback(initiatorBudget: budget);
            await using var second = new FlowLoopback(initiatorBudget: budget);
            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            FlowPair occupied = await first.OpenAsync().ConfigureAwait(false);
            FlowPair waiting = await second.OpenAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => budget.Usage.Messages == 0).ConfigureAwait(false);
            first.InitiatorCarrier.GateBefore = write => write.IsData;
            int heldCount = byteLimit ? 1 : 16 - (2 * DataChannelApplicationBudget.ControlHeadroomMessages);
            Task<DataChannelResult>[] held = [.. Enumerable.Range(0, heldCount).Select(_ => occupied.Local.SendAsync(ByteString.Empty).AsTask())];
            await first.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelApplicationBudget.DataChannelCarrierReservation? lease = null;
            if (byteLimit)
            {
                Assert.That(budget.TryReserveCarrier(
                    4096 - 17 - (2 * DataChannelApplicationBudget.ControlHeadroomBytes), 0, 0, out lease), Is.True);
            }
            using (lease)
            {
                var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                Task<DataChannelResult> pending = waiting.Local.SendWithAdmissionAsync(
                    new ByteString("\t"u8.ToArray()), cancellation.Token).AsTask();
                try
                {
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(waiting.Local.StateData.LastDataId, Is.Zero);
                    Assert.That(occupied.Local.StateData.SendMessages, Is.EqualTo(heldCount));
                    Assert.That(waiting.Local.StateData.SendMessages, Is.Zero);
                    first.InitiatorCarrier.BeforeRelease.TrySetResult(true);
                    Assert.That(await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Is.All.EqualTo(DataChannelResult.Good));
                    lease?.Dispose();
                    Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                }
                finally
                {
                    first.InitiatorCarrier.BeforeRelease.TrySetResult(true);
                    lease?.Dispose();
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await pending.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    cancellation.Dispose();
                }
            }
            using DataChannelMessage? delivered = await waiting.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(delivered!.MessageId, Is.EqualTo(1));
            Assert.That(delivered.Payload.ToArray(), Is.EqualTo("\t"u8.ToArray()));
        }

        [TestCase(17, 2)]
        [TestCase(64, 1)]
        public void RefusedMessageReservationDoesNotSignalItsOwnCapacityWaiter(long bytes, int messages)
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = bytes,
                MaxMessages = messages
            });
            Assert.That(budget.TryReserveMessage(17), Is.True);
            Task changed = budget.CapacityChanged;
            Assert.That(budget.TryReserveMessage(1), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(changed.IsCompleted, Is.False);
                Assert.That(budget.Usage.BufferedBytes, Is.EqualTo(17));
                Assert.That(budget.Usage.Messages, Is.EqualTo(1));
            });
            budget.Release(FlowResource.BufferedBytes, 17);
            budget.Release(FlowResource.Message, 1);
            Assert.That(changed.IsCompleted, Is.True);
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        [Test]
        public async Task CancelledAdmissionDoesNotQueueOrConsumeAnIdentifier()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.InitiatorPolicy.Handler = async (request, token) =>
            {
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return new DataChannelAuthorization(DataChannelResult.Good, request.Parameters);
            };
            Task recheck = pair.Initiator.RecheckAuthorizationAsync().AsTask();
            var cancellation = new CancellationTokenSource();
            Task<DataChannelResult> pending = flow.Local.SendWithAdmissionAsync(
                ByteString.Empty, cancellation.Token).AsTask();
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
                Assert.That(flow.Local.StateData.SendMessages, Is.Zero);
                Assert.That(flow.Local.StateData.SendBytes, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                await recheck.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                cancellation.Dispose();
            }
            Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? delivered = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(delivered!.MessageId, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedAuthorizationOrContextClosureReleasesAdmissionWaiters(bool closeContext)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.InitiatorPolicy.Handler = async (_, token) =>
            {
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
            };
            Task recheck = pair.Initiator.RecheckAuthorizationAsync().AsTask();
            Task<DataChannelResult> pending = flow.Local.SendWithAdmissionAsync(ByteString.Empty).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            try
            {
                if (closeContext)
                {
                    await pair.Initiator.InvalidateAsync().ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                }
                Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
                Assert.That(flow.Local.StateData.SendMessages, Is.Zero);
                Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
                await FlowLoopback.EventuallyAsync(() => pair.InitiatorBudget.Usage.Messages == 0)
                    .ConfigureAwait(false);
                Assert.That(pair.InitiatorBudget.Usage.Messages, Is.Zero);
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Faulted));
                if (!closeContext)
                {
                    Assert.That(flow.Local.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                    Assert.That(pair.Initiator.IsReady, Is.True);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await recheck.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PermanentValidationFailureReturnsWithoutWaitingOrConsumingAnIdentifier()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await flow.Local.SendWithAdmissionAsync(new ByteString(new byte[257])).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
            Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? delivered = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(delivered!.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task RevisionBarrierRefusalIsNotRetriedAsAuthorizationOrCapacityPressure()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Commit;
            Task<DataChannelResult> updating = flow.Local.UpdateAsync(proposal).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            try
            {
                Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
            }
            finally
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
                await updating.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(await updating.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? delivered = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(delivered!.MessageId, Is.EqualTo(1));
            Assert.That(delivered.Revision, Is.EqualTo(2));
        }

        [Test]
        public async Task CommittedDatagramRefusalIsNotReadmittedAsAnotherMessage()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 4096)).ConfigureAwait(false);
            pair.InitiatorCarrier.SendOutcome = write => write.IsData
                ? DataChannelResult.WouldBlock : DataChannelResult.Good;
            Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.EqualTo(1));
            Assert.That(flow.Local.StateData.LastDataId, Is.EqualTo(1));
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
            pair.InitiatorCarrier.SendOutcome = null;
            Assert.That(await flow.Local.SendWithAdmissionAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? delivered = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(delivered!.MessageId, Is.EqualTo(2));
        }
    }
}
