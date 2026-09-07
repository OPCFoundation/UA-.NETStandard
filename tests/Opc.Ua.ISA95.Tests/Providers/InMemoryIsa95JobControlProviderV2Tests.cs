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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public class InMemoryIsa95JobControlProviderV2Tests
    {
        [Test]
        public async Task StoreAndStartAllowsOrderToStart()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobOrderReceiptV2 receipt = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success));
        }

        [Test]
        public async Task PauseResumeAndAbortFollowTheStateMachine()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);

            Isa95JobOrderReceiptV2 pause = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Pause,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            Isa95JobOrderReceiptV2 resume = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Resume,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            Isa95JobOrderReceiptV2 abort = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Abort,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(pause.Result), Is.True);
            Assert.That(ServiceResult.IsGood(resume.Result), Is.True);
            Assert.That(ServiceResult.IsGood(abort.Result), Is.True);
        }

        [Test]
        public async Task PauseOnStoredOrderIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV2 pause = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Pause,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(pause.Result), Is.True);
            Assert.That(
                pause.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidStatus));
        }

        [Test]
        public async Task RevokeStartMovesOrderBackToNotAllowedToStart()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Start,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV2 revoke = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.RevokeStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            Isa95JobOrderReceiptV2 start = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Start,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(revoke.Result), Is.True);
            Assert.That(ServiceResult.IsGood(start.Result), Is.True);
        }

        [Test]
        public async Task AbortOnTerminalOrderIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Abort,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV2 abortAgain = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Abort,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(abortAgain.Result), Is.True);
        }

        [Test]
        public async Task StoreEmitsExactlyOneStatusNotification()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].JobOrderId, Is.EqualTo("job1"));
            Assert.That(notifications[0].JobOrder.JobOrderID, Is.EqualTo("job1"));
            Assert.That(notifications[0].StateNumber, Is.EqualTo(1u));
            Assert.That(notifications[0].StateText.Text, Is.EqualTo("NotAllowedToStart"));
            Assert.That(notifications[0].SequenceNumber, Is.EqualTo(1ul));
        }

        [Test]
        public async Task PauseEmitsCompositeInterruptedState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Pause,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(notifications[0].StateNumber, Is.EqualTo(4u));
            Assert.That(notifications[0].StateText.Text, Is.EqualTo("Interrupted"));
            Assert.That(notifications[0].State.Count, Is.EqualTo(2));
            Assert.That(notifications[0].State[1].StateText.Text, Is.EqualTo("Suspended"));
        }

        [Test]
        public async Task UpdateEmitsStatusNotificationForTheSelfTransition()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                3,
                async () =>
                {
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Store,
                        Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Update,
                        new V2.ISA95JobOrderDataType
                        {
                            JobOrderID = "job1",
                            Priority = 5
                        }).ConfigureAwait(false);
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Start,
                        Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                }).ConfigureAwait(false);

            Assert.That(notifications, Has.Count.EqualTo(3));
            Assert.That(notifications[0].StateNumber, Is.EqualTo(1u));
            Assert.That(notifications[1].StateNumber, Is.EqualTo(1u));
            Assert.That(notifications[1].JobOrder.Priority, Is.EqualTo((short)5));
            Assert.That(notifications[2].StateNumber, Is.EqualTo(2u));
            Assert.That(notifications[0].SequenceNumber, Is.EqualTo(1ul));
            Assert.That(notifications[1].SequenceNumber, Is.EqualTo(2ul));
            Assert.That(notifications[2].SequenceNumber, Is.EqualTo(3ul));
        }

        [Test]
        public async Task StatusNotificationTimestampUsesTimeProvider()
        {
            var time = new FakeTimeProvider();
            var expected = DateTimeUtc.From(time.GetUtcNow());
            using var provider = new InMemoryIsa95JobControlProvider(null, time);

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(notifications[0].Timestamp, Is.EqualTo(expected));
        }

        [Test]
        public async Task MultipleSubscribersEachReceiveEveryChangeExactlyOnce()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            IAsyncEnumerator<Isa95JobStatusNotificationV2> first =
                provider.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            IAsyncEnumerator<Isa95JobStatusNotificationV2> second =
                provider.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);

            try
            {
                ValueTask<bool> firstPending = first.MoveNextAsync();
                ValueTask<bool> secondPending = second.MoveNextAsync();

                await provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).ConfigureAwait(false);

                Assert.That(await firstPending.ConfigureAwait(false), Is.True);
                Assert.That(await secondPending.ConfigureAwait(false), Is.True);
                Assert.That(first.Current.JobOrderId, Is.EqualTo("job1"));
                Assert.That(second.Current.JobOrderId, Is.EqualTo("job1"));
                Assert.That(first.Current.SequenceNumber, Is.EqualTo(second.Current.SequenceNumber));
            }
            finally
            {
                cts.Cancel();
                await first.DisposeAsync().ConfigureAwait(false);
                await second.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancellingSubscriptionTokenEndsEnumeration()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            IAsyncEnumerator<Isa95JobStatusNotificationV2> enumerator =
                provider.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);

            try
            {
                ValueTask<bool> pending = enumerator.MoveNextAsync();
                await provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                Assert.That(await pending.ConfigureAwait(false), Is.True);

                cts.Cancel();
                Assert.That(await enumerator.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task VersionOneAndVersionTwoShareTheSameOrderStore()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV2 startViaV2 = await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Start,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            Isa95JobOrderReceiptV1 startViaV1Again = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(startViaV2.Result), Is.True);
            Assert.That(ServiceResult.IsUncertain(startViaV1Again.Result), Is.True);
        }

        [Test]
        public async Task ResponseReceivedViaVersionTwoIsVisibleViaVersionOne()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(Isa95TestData.V2Response("r1", "job1")).ConfigureAwait(false);

            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                "job1",
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(query.Result), Is.True);
            Assert.That(query.Responses.Count, Is.EqualTo(1));
            Assert.That(query.Responses[0].ID, Is.EqualTo("r1"));
        }

        [Test]
        public async Task ResponseReceivedViaVersionOneIsVisibleViaVersionTwo()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(Isa95TestData.V1Response("r1", "job1")).ConfigureAwait(false);

            Isa95JobResponseByIdResultV2 query =
                await provider.RequestJobResponseByJobOrderIdAsync("job1").ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(query.Result), Is.True);
            Assert.That(query.Response, Is.Not.Null);
            Assert.That(query.Response!.JobResponseID, Is.EqualTo("r1"));
        }

        [Test]
        public async Task ResponsesCanBeRequestedByState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V2Response("done", "job1")).ConfigureAwait(false);
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V2Response(
                    "running",
                    "job2",
                    stateNumber: 3,
                    stateText: "Running")).ConfigureAwait(false);

            Isa95JobResponsesByStateResultV2 result =
                await provider.RequestJobResponsesByStateAsync(
                [
                    new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateNumber = 3,
                        StateText = new LocalizedText("Running")
                    }
                ]).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(result.Responses, Has.Count.EqualTo(1));
            Assert.That(result.Responses[0].JobResponseID, Is.EqualTo("running"));
        }

        [Test]
        public async Task PauseNotificationSubstateTargetsInterruptedSubstates()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Pause,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            V2.ISA95StateDataType top = notifications[0].State[0];
            V2.ISA95StateDataType sub = notifications[0].State[1];
            Assert.That(top.BrowsePath == null || top.BrowsePath.Elements.Count == 0, Is.True);
            Assert.That(sub.StateNumber, Is.EqualTo(2u));
            Assert.That(sub.BrowsePath.Elements[0].TargetName.Name,
                Is.EqualTo(V2.BrowseNames.InterruptedSubstates));
        }

        [Test]
        public async Task NotificationJobResponseStateEqualsNotificationStateNeverStale()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            // Store a response for job1 that is in a terminal state.
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V2Response("r1", "job1")).ConfigureAwait(false);

            // Storing the order emits a NotAllowedToStart notification; the carried
            // job response state must be updated to match, not the stale Completed.
            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Isa95JobStatusNotificationV2 notification = notifications[0];
            Assert.That(notification.StateNumber, Is.EqualTo(1u));
            Assert.That(notification.JobResponse.JobResponseID, Is.EqualTo("r1"));
            Assert.That(notification.JobResponse.JobState[0].StateNumber, Is.EqualTo(1u));
            Assert.That(
                Isa95V2StateMachine.FromStateArray(notification.JobResponse.JobState),
                Is.EqualTo(Isa95V2StateMachine.FromStateArray(notification.State)));
        }

        [Test]
        public async Task ResponsesRequestedByInterruptedTopLevelMatchSuspended()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            ArrayOf<V2.ISA95StateDataType> suspended =
                Isa95V2StateMachine.ToStateArray(Isa95JobCanonicalState.Suspended);
            await provider.ReceiveJobResponseAsync(new V2.ISA95JobResponseDataType
            {
                JobResponseID = "sus",
                JobOrderID = "job1",
                JobState = suspended
            }).ConfigureAwait(false);

            Isa95JobResponsesByStateResultV2 result =
                await provider.RequestJobResponsesByStateAsync(
                [
                    new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateNumber = 4,
                        StateText = new LocalizedText("Interrupted")
                    }
                ]).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(result.Responses, Has.Count.EqualTo(1));
            Assert.That(result.Responses[0].JobResponseID, Is.EqualTo("sus"));
        }

        [Test]
        public async Task StoreRetainsAndEmitsTheAuditComment()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            ArrayOf<LocalizedText> comment = new[]
            {
                new LocalizedText("en", "created"),
                new LocalizedText("de", "erstellt")
            }.ToArrayOf();

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1"),
                    comment).AsTask()).ConfigureAwait(false);

            Assert.That(notifications[0].Comment.Count, Is.EqualTo(2));
            Assert.That(notifications[0].Comment[0].Text, Is.EqualTo("created"));
            Assert.That(notifications[0].Comment[1].Text, Is.EqualTo("erstellt"));
        }

        [Test]
        public async Task LatestAuditCommentIsRetainedAcrossOperationsThatOmitOne()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            ArrayOf<LocalizedText> created = new[] { new LocalizedText("created") }.ToArrayOf();
            ArrayOf<LocalizedText> paused = new[] { new LocalizedText("paused for maintenance") }.ToArrayOf();

            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.StoreAndStart,
                Isa95TestData.V2Order("job1"),
                created).ConfigureAwait(false);

            // The execution transition supplies no comment; the retained comment persists.
            IReadOnlyList<Isa95JobStatusNotificationV2> afterBegin = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.TransitionAsync(
                    "job1",
                    Isa95JobExecutionTransition.BeginExecution).AsTask()).ConfigureAwait(false);
            Assert.That(afterBegin[0].Comment.Count, Is.EqualTo(1));
            Assert.That(afterBegin[0].Comment[0].Text, Is.EqualTo("created"));

            // A later operation with a comment updates the retained audit comment.
            IReadOnlyList<Isa95JobStatusNotificationV2> afterPause = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Pause,
                    Isa95TestData.V2Order("job1"),
                    paused).AsTask()).ConfigureAwait(false);
            Assert.That(afterPause[0].Comment[0].Text, Is.EqualTo("paused for maintenance"));
        }

        [Test]
        public async Task StoreWithoutCommentEmitsAnEmptyComment()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            IReadOnlyList<Isa95JobStatusNotificationV2> notifications = await Isa95TestData.CaptureAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(notifications[0].Comment.Count, Is.Zero);
        }

        [Test]
        public async Task UpdateEmitsExactlyOneCatalogChangeWithTheUpdatedOrder()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            IReadOnlyList<Isa95JobOrderCatalogChange> changes = await Isa95TestData.CaptureCatalogAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Update,
                    new V2.ISA95JobOrderDataType { JobOrderID = "job1", Priority = 5 }).AsTask()).ConfigureAwait(false);

            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Updated));
            Assert.That(changes[0].JobOrderId, Is.EqualTo("job1"));
            Assert.That(changes[0].Order, Is.Not.Null);
            Assert.That(changes[0].Order!.JobOrder.Priority, Is.EqualTo((short)5));
            Assert.That(changes[0].Order!.State[0].StateNumber, Is.EqualTo(1u));
        }

        [Test]
        public async Task CancelEmitsExactlyOneCatalogChangeRemoved()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            IReadOnlyList<Isa95JobOrderCatalogChange> changes = await Isa95TestData.CaptureCatalogAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Cancel,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Removed));
            Assert.That(changes[0].JobOrderId, Is.EqualTo("job1"));
            Assert.That(changes[0].Order, Is.Null);
        }

        [Test]
        public async Task ClearEmitsCatalogChangeRemoved()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);
            await provider.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Abort,
                Isa95TestData.V2Order("job1")).ConfigureAwait(false);

            IReadOnlyList<Isa95JobOrderCatalogChange> changes = await Isa95TestData.CaptureCatalogAsync(
                provider,
                1,
                () => provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Clear,
                    Isa95TestData.V2Order("job1")).AsTask()).ConfigureAwait(false);

            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Removed));
        }

        [Test]
        public async Task StateChangingOperationsDoNotEmitCatalogChanges()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            // Store and Start are life-cycle state changes carried by the status
            // source; only the trailing Cancel is a catalog change, so it must be
            // the single change captured.
            IReadOnlyList<Isa95JobOrderCatalogChange> changes = await Isa95TestData.CaptureCatalogAsync(
                provider,
                1,
                async () =>
                {
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Store,
                        Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Start,
                        Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                    await provider.ReceiveJobOrderAsync(
                        Isa95JobOrderOperationV2.Cancel,
                        Isa95TestData.V2Order("job1")).ConfigureAwait(false);
                }).ConfigureAwait(false);

            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Removed));
        }
    }
}
