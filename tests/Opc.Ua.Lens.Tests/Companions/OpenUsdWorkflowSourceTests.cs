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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Tests.Subscriptions;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdWorkflowSourceTests
    {
        [Test]
        public async Task OneOwnedSubscriptionCapturesExactValuesBeforeThePublisherReusesStorage()
        {
            var fixture = new CaptureFixture();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            await using (enumerator.ConfigureAwait(false))
            {
                Task<bool> pending = enumerator.MoveNextAsync().AsTask();
                Assert.That(fixture.Adapter.Adds, Is.EqualTo(1));
                Assert.That(fixture.Adapter.Options!.CurrentValue.PublishingEnabled, Is.True);
                Assert.That(fixture.Adapter.ItemOptions, Has.Count.EqualTo(2));
                Assert.That(fixture.Adapter.ItemOptions[0].CurrentValue.StartNodeId, Is.EqualTo(sNodes[0]));
                Assert.That(fixture.Adapter.ItemOptions[1].CurrentValue.StartNodeId, Is.EqualTo(sNodes[1]));
                Assert.That(fixture.Adapter.ItemOptions.All(options =>
                    options.CurrentValue.AttributeId == Attributes.Value &&
                    options.CurrentValue.MonitoringMode == MonitoringMode.Reporting), Is.True);
                int[] storage = [1, 2, 3];
                await fixture.SendAsync([fixture.Change(Variant.From(storage)), fixture.Change(Variant.From(7d), 1)])
                    .ConfigureAwait(false);
                storage[0] = 999;

                Assert.That(await pending.WaitAsync(sWait).ConfigureAwait(false), Is.True);
                OpenUsdWorkflowChange first = enumerator.Current;
                Assert.That(first.Source, Is.EqualTo(sNodes[0]));
                Assert.That(first.SequenceNumber, Is.EqualTo(71));
                Assert.That(first.PublishTime, Is.EqualTo(sPublishTime));
                Assert.That(first.Value.WrappedValue.TryGetValue(out ArrayOf<int> values), Is.True);
                Assert.That(values.ToArray(), Is.EqualTo(sOriginal));
                Assert.That(first.Value.SourceTimestamp, Is.EqualTo(OpenUsdWorkflowTestContext.Start));
                Assert.That(first.Value.SourcePicoseconds, Is.EqualTo(123));
                Assert.That(await enumerator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(enumerator.Current.Source, Is.EqualTo(sNodes[1]));
                Assert.That(enumerator.Current.Value.WrappedValue.TryGetValue(out double scalar), Is.True);
                Assert.That(scalar, Is.EqualTo(7));
            }
            fixture.VerifyDisposedOnce();
        }

        [Test]
        public async Task TheExactQueueCapacityIsAcceptedAndReusedAfterConsumption()
        {
            var fixture = new CaptureFixture();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            await using (enumerator.ConfigureAwait(false))
            {
                Task<bool> first = enumerator.MoveNextAsync().AsTask();
                await fixture.SendAsync([fixture.Change(Variant.From(0))]).ConfigureAwait(false);
                Assert.That(await first.WaitAsync(sWait).ConfigureAwait(false), Is.True);
                await fixture.SendAsync(Enumerable.Range(1, 128).Select(index =>
                    fixture.Change(Variant.From(index))).ToArray()).ConfigureAwait(false);
                for (int index = 1; index <= 128; index++)
                {
                    Assert.That(await enumerator.MoveNextAsync().ConfigureAwait(false), Is.True);
                    Assert.That(enumerator.Current.Value.WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(index));
                }
                await fixture.SendAsync([fixture.Change(Variant.From(129))]).ConfigureAwait(false);
                Assert.That(await enumerator.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(enumerator.Current.Value.WrappedValue.TryGetValue(out int last), Is.True);
                Assert.That(last, Is.EqualTo(129));
            }
            fixture.VerifyDisposedOnce();
        }

        [TestCase("queue")]
        [TestCase("bytes")]
        [TestCase("value")]
        public async Task CaptureOverflowFailsExplicitlyAndDisposesAllOwnedItems(string limit)
        {
            var fixture = new CaptureFixture();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            Task<bool> first = enumerator.MoveNextAsync().AsTask();
            await fixture.SendAsync([fixture.Change(Variant.From(0))]).ConfigureAwait(false);
            Assert.That(await first.WaitAsync(sWait).ConfigureAwait(false), Is.True);
            int count = limit == "queue" ? 129 : limit == "bytes" ? 66 : 1;
            Variant payload = limit == "queue" ? Variant.From(1) :
                Variant.From(ByteString.From(new byte[limit == "bytes" ? 16000 : 20000]));
            await fixture.SendAsync(Enumerable.Range(0, count).Select(_ =>
                fixture.Change(payload)).ToArray()).ConfigureAwait(false);

            await Assert.ThatAsync(() => enumerator.MoveNextAsync().AsTask().WaitAsync(sWait),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            await enumerator.DisposeAsync().ConfigureAwait(false);

            fixture.VerifyDisposedOnce();
        }

        [TestCase(PublishState.Stopped)]
        [TestCase(PublishState.Timeout)]
        [TestCase(PublishState.Transferred)]
        [TestCase(PublishState.Completed)]
        public async Task SubscriptionFailureReleasesAllItemsThroughOneOwnedSubscription(PublishState state)
        {
            var fixture = new CaptureFixture();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            Task<bool> first = enumerator.MoveNextAsync().AsTask();
            await fixture.Handler.OnSubscriptionStateChangedAsync(
                fixture.Adapter.Subscription.Object, SubscriptionState.Created, state).ConfigureAwait(false);

            await Assert.ThatAsync(() => first.WaitAsync(sWait),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadSubscriptionIdInvalid)).ConfigureAwait(false);
            await enumerator.DisposeAsync().ConfigureAwait(false);

            fixture.VerifyDisposedOnce();
        }

        [Test]
        public async Task ItemRejectionIsNotReportedAsAnEmptySuccessfulCapture()
        {
            var fixture = new CaptureFixture();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            Task<bool> first = enumerator.MoveNextAsync().AsTask();
            fixture.Adapter.Monitored[1].SetupGet(item => item.Error)
                .Returns(new ServiceResult(StatusCodes.BadUserAccessDenied));
            await fixture.Handler.OnKeepAliveNotificationAsync(
                fixture.Adapter.Subscription.Object, 71, sPublishTime, PublishState.KeepAlive).ConfigureAwait(false);

            await Assert.ThatAsync(() => first.WaitAsync(sWait),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
            await enumerator.DisposeAsync().ConfigureAwait(false);

            fixture.VerifyDisposedOnce();
        }

        [Test]
        public async Task CancellationDrainsTheOwnedSubscriptionAndIgnoresLateCallbacks()
        {
            var fixture = new CaptureFixture();
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, cancellation.Token).GetAsyncEnumerator();
            Task<bool> first = enumerator.MoveNextAsync().AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => first.WaitAsync(sWait),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await fixture.SendAsync([fixture.Change(Variant.From(99))]).ConfigureAwait(false);
            await fixture.Handler.OnSubscriptionStateChangedAsync(
                fixture.Adapter.Subscription.Object, SubscriptionState.Deleted, PublishState.Completed)
                .ConfigureAwait(false);

            fixture.VerifyDisposedOnce();
        }

        [Test]
        public async Task CaptureAndCleanupFailuresAreBothPreserved()
        {
            var fixture = new CaptureFixture();
            var cleanup = new InvalidOperationException("owned cleanup");
            fixture.Adapter.Subscription.Setup(subscription => subscription.DisposeAsync())
                .Returns(() => ValueTask.FromException(cleanup));
            IAsyncEnumerator<OpenUsdWorkflowChange> enumerator =
                fixture.Source.ObserveAsync(sNodes, CancellationToken.None).GetAsyncEnumerator();
            Task<bool> first = enumerator.MoveNextAsync().AsTask();
            await fixture.Handler.OnSubscriptionStateChangedAsync(
                fixture.Adapter.Subscription.Object, SubscriptionState.Error, PublishState.None).ConfigureAwait(false);

            await Assert.ThatAsync(() => first.WaitAsync(sWait),
                Throws.TypeOf<AggregateException>().With.Property(nameof(AggregateException.InnerExceptions))
                    .Count.EqualTo(2)).ConfigureAwait(false);
            AggregateException failure = first.Exception?.InnerException as AggregateException ??
                throw new AssertionException("Expected the capture and cleanup failure.");
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanup));
            await enumerator.DisposeAsync().ConfigureAwait(false);

            fixture.VerifyDisposedOnce();
        }

        [TestCase("empty")]
        [TestCase("excess")]
        [TestCase("null")]
        [TestCase("duplicate")]
        public void InvalidObservationNodesNeverCreateASubscription(string fault)
        {
            var fixture = new CaptureFixture();
            ArrayOf<NodeId> nodes = fault switch
            {
                "empty" => [],
                "excess" => Enumerable.Range(0, 129).Select(index => new NodeId((uint)index + 1)).ToArray(),
                "null" => [NodeId.Null],
                "duplicate" => [sNodes[0], sNodes[0]],
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };

            if (fault is "empty" or "excess")
            {
                Assert.That(() => fixture.Source.ObserveAsync(nodes, CancellationToken.None),
                    Throws.InvalidOperationException);
            }
            else
            {
                Assert.That(() => fixture.Source.ObserveAsync(nodes, CancellationToken.None), Throws.ArgumentException);
            }

            Assert.That(fixture.Adapter.Adds, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HistoryLimitAndEarlyStopReleaseTheExactContinuation(bool earlyStop)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            var source = new OpenUsdWorkflowSource(fixture.Context);
            var continuation = ByteString.From([1, 3, 5]);
            int reads = 0;
            int releases = 0;
            fixture.Server.Session.Setup(session => session.HistoryReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ExtensionObject details, TimestampsToReturn timestamps, bool release,
                    ArrayOf<HistoryReadValueId> nodes, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(nodes, Has.Count.EqualTo(1));
                    Assert.That(nodes[0].NodeId, Is.EqualTo(sNodes[0]));
                    Assert.That(timestamps, Is.EqualTo(TimestampsToReturn.Both));
                    if (release)
                    {
                        releases++;
                        Assert.That(nodes[0].ContinuationPoint, Is.EqualTo(continuation));
                        return ValueTask.FromResult(new HistoryReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }]
                        });
                    }
                    reads++;
                    Assert.That(details.TryGetValue(
                        out ReadRawModifiedDetails? request), Is.True);
                    Assert.That(request!.NumValuesPerNode, Is.EqualTo(2));
                    Assert.That(request.StartTime, Is.EqualTo(OpenUsdWorkflowTestContext.Start));
                    Assert.That(request.EndTime,
                        Is.EqualTo(OpenUsdWorkflowTestContext.Start.Add(TimeSpan.FromHours(1))));
                    Assert.That(request.IsReadModified, Is.False);
                    return ValueTask.FromResult(new HistoryReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new HistoryReadResult
                            {
                                StatusCode = StatusCodes.Good,
                                ContinuationPoint = continuation,
                                HistoryData = new ExtensionObject(new HistoryData
                                {
                                    DataValues =
                                    [
                                        OpenUsdWorkflowTestContext.Value(Variant.From(1)),
                                        OpenUsdWorkflowTestContext.Value(Variant.From(2), 1),
                                        OpenUsdWorkflowTestContext.Value(Variant.From(3), 2)
                                    ]
                                })
                            }
                        ]
                    });
                });

            async Task ReadAsync()
            {
                int received = 0;
                await foreach (DataValue value in source.ReadHistoryAsync(
                    sNodes[0], OpenUsdWorkflowTestContext.Start.ToDateTime(),
                    OpenUsdWorkflowTestContext.Start.Add(TimeSpan.FromHours(1)).ToDateTime(), 2, CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    Assert.That(value.WrappedValue.TryGetValue(out int actual), Is.True);
                    Assert.That(actual, Is.EqualTo(++received));
                    if (earlyStop)
                    {
                        break;
                    }
                }
            }

            if (earlyStop)
            {
                await ReadAsync().ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(ReadAsync, Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(releases, Is.EqualTo(1));
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        private static readonly ArrayOf<NodeId> sNodes = [new NodeId("first", 2), new NodeId("second", 2)];
        private static readonly int[] sOriginal = [1, 2, 3];

        private static readonly DateTime sPublishTime = OpenUsdWorkflowTestContext.Start
            .Add(TimeSpan.FromSeconds(2)).ToDateTime();

        private static readonly TimeSpan sWait = TimeSpan.FromSeconds(10);

        private sealed class CaptureFixture
        {
            public CaptureFixture()
            {
                ISubscriptionManager? manager = Adapter.Manager.Object;
                Context.Server.Session.Setup(session => session.TryGetSubscriptionManager(out manager)).Returns(true);
                Adapter.Items.SetupGet(items => items.Items)
                    .Returns(() => Adapter.Monitored.Select(item => item.Object));
                Adapter.ItemAdded = () =>
                {
                    Assert.That(Adapter.Options!.CurrentValue.PublishingEnabled, Is.False);
                    string name = Adapter.ItemNames[^1];
                    Adapter.Monitored[^1].SetupGet(item => item.Name).Returns(name);
                };
                Source = new OpenUsdWorkflowSource(Context.Context);
            }

            public OpenUsdWorkflowTestContext Context { get; } = new();

            public ChannelV2EngineAdapterTests.AdapterContext Adapter { get; } = new();

            public OpenUsdWorkflowSource Source { get; }

            public ISubscriptionNotificationHandler Handler => Adapter.Handler ??
                throw new AssertionException("The owned subscription was not created.");

            public DataValueChange Change(Variant value, int item = 0)
            {
                return new DataValueChange(
                    Adapter.Monitored[item].Object, OpenUsdWorkflowTestContext.Value(value), null);
            }

            public async Task SendAsync(ArrayOf<DataValueChange> changes)
            {
                await Handler.OnDataChangeNotificationAsync(
                    Adapter.Subscription.Object, 71, sPublishTime, changes.Memory, PublishState.None, [])
                    .ConfigureAwait(false);
            }

            public void VerifyDisposedOnce()
            {
                Adapter.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Once);
                Assert.That(Adapter.ItemOptions, Has.Count.EqualTo(2));
                Context.Server.VerifyNoMutationOrSessionOwnership();
            }
        }
    }
}
