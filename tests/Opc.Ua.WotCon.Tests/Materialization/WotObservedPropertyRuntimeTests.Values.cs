// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

#pragma warning disable CS0618 // tests exercise the obsolete classic API

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT observed property runtime.
    /// </summary>
    public sealed partial class WotObservedPropertyRuntimeTests
    {
        /// <summary>
        /// Verifies that observation ranges are independent and cache reads keep the whole value.
        /// </summary>
        /// <param name="invalidRange">The invalid-range flag for this test case.</param>
        /// <param name="invalidEncoding">The invalid-encoding flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ObservationRangesAreIndependentAndCacheReadsKeepTheWholeValue(
            bool invalidRange, bool invalidEncoding)
        {
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var started = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceSubscription = new Mock<IWotSubscription>();
            sourceSubscription.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = (notification, _) =>
                {
                    started.TrySetResult(notification);
                    return new ValueTask<IWotSubscription>(sourceSubscription.Object);
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(observe, channel);
            await WithProjectionAsync([observe], channels, async (session, _) =>
            {
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 50
                };
                MonitoredItem whole = CreateItem(session, subscription);
                MonitoredItem slice = CreateItem(session, subscription);
                slice.IndexRange = invalidRange ? "9" : "1";
                if (invalidEncoding)
                {
                    slice.Encoding = new QualifiedName("UnknownEncoding");
                }
                ChannelReader<DataValue> wholeValues = Watch(whole);
                ChannelReader<DataValue> sliceValues = Watch(slice);
                subscription.AddItem(whole);
                subscription.AddItem(slice);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    Action<WotNotification> publish = await started.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    ArrayOf<int> input = [10, 20, 30];
                    var timestamp = new DateTimeUtc(2026, 3, 4, 5, 6, 7);
                    publish(new WotNotification(new DataValue(
                        new Variant(input), StatusCodes.UncertainInitialValue, timestamp)));
                    DataValue complete = await ReceiveAsync(wholeValues).ConfigureAwait(false);
                    DataValue partial = await ReceiveAsync(sliceValues).ConfigureAwait(false);
                    AssertArray(complete, [10, 20, 30], StatusCodes.UncertainInitialValue, timestamp);
                    StatusCode expectedError = invalidRange
                        ? StatusCodes.BadIndexRangeNoData
                        : StatusCodes.BadDataEncodingInvalid;
                    if (invalidRange || invalidEncoding)
                    {
                        Assert.That(partial.StatusCode, Is.EqualTo(expectedError));
                        Assert.That(partial.WrappedValue.IsNull, Is.True);
                        Assert.That(partial.SourceTimestamp, Is.EqualTo(timestamp));
                    }
                    else
                    {
                        AssertArray(partial, [20], StatusCodes.UncertainInitialValue, timestamp);
                    }
                    ReadResponse read = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Both,
                        [
                            new ReadValueId
                            {
                                NodeId = whole.ResolvedNodeId,
                                AttributeId = Attributes.Value,
                                IndexRange = slice.IndexRange,
                                DataEncoding = slice.Encoding
                            },
                            new ReadValueId { NodeId = whole.ResolvedNodeId, AttributeId = Attributes.Value }
                        ],
                        CancellationToken.None).ConfigureAwait(false);
                    if (invalidRange || invalidEncoding)
                    {
                        Assert.That(read.Results[0].StatusCode, Is.EqualTo(expectedError));
                        Assert.That(read.Results[0].WrappedValue.IsNull, Is.True);
                    }
                    else
                    {
                        AssertArray(read.Results[0], [20], StatusCodes.UncertainInitialValue, timestamp);
                    }
                    AssertArray(read.Results[1], [10, 20, 30], StatusCodes.UncertainInitialValue, timestamp);
                    Assert.That(channel.ReadCount, Is.Zero);
                    Assert.That(channel.ObserveCount, Is.EqualTo(1));
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                sourceSubscription.Verify(s => s.DisposeAsync(), Times.Once);
            }, valueRank: ValueRanks.OneDimension).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that observation overflow is visible and stops the source until restarted.
        /// </summary>
        [Test]
        public async Task ObservationOverflowIsVisibleAndStopsTheSourceUntilRestarted()
        {
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var started = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceSubscription = new Mock<IWotSubscription>();
            sourceSubscription.Setup(s => s.DisposeAsync()).Returns(() =>
            {
                stopped.TrySetResult(true);
                return default;
            });
            Action<WotNotification>? latest = null;
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = (notification, _) =>
                {
                    latest = notification;
                    started.TrySetResult(notification);
                    return new ValueTask<IWotSubscription>(sourceSubscription.Object);
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(observe, channel);
            var factory = new RecordingRuntimeFactory(
                channels, options: new WotProjectionBindingRuntimeOptions { MaxQueuedPropertyValues = 1 });
            await WithProjectionAsync([observe], channels, async (session, _) =>
            {
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 50
                };
                MonitoredItem item = CreateItem(session, subscription);
                ChannelReader<DataValue> values = Watch(item);
                subscription.AddItem(item);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    Action<WotNotification> publish = await started.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    int entered = 0;
                    factory.ObservedNode!.Node.OnReadRolePermissions =
                        (context, node, ref permissions) =>
                        {
                            if (Interlocked.Exchange(ref entered, 1) == 0)
                            {
                                publish(new WotNotification(new DataValue(new Variant(43))));
                                publish(new WotNotification(new DataValue(new Variant(44))));
                            }
                            return ServiceResult.Good;
                        };
                    publish(new WotNotification(new DataValue(new Variant(42))));
                    DataValue failure = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                    Assert.That(failure.WrappedValue.IsNull, Is.True);
                    await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    sourceSubscription.Verify(s => s.DisposeAsync(), Times.Once);
                    Assert.That(channel.ReadCount, Is.Zero);

                    await subscription.SetMonitoringModeAsync(MonitoringMode.Disabled, [item]).ConfigureAwait(false);
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, [item]).ConfigureAwait(false);
                    Assert.That(channel.ObserveCount, Is.EqualTo(2));
                    latest!(new WotNotification(new DataValue(new Variant(45))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 45);
                }
                finally
                {
                    factory.ObservedNode!.Node.OnReadRolePermissions = null;
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                sourceSubscription.Verify(s => s.DisposeAsync(), Times.Exactly(2));
            }, runtimeFactory: factory).ConfigureAwait(false);
        }

        private static void AssertArray(
            in DataValue value,
            ArrayOf<int> expected,
            StatusCode status,
            DateTimeUtc timestamp)
        {
            Assert.That(value.StatusCode, Is.EqualTo(status));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<int> array), Is.True);
            Assert.That(array.ToArray(), Is.EqualTo(expected.ToArray()));
        }
    }
}
