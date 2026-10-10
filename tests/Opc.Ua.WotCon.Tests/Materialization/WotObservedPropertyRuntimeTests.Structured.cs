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
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotObservedPropertyRuntimeTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task StructuredObservationsComposeEveryFieldWithoutUsingSeparateReadForms(
            bool separateRead, bool xmlEncoding)
        {
            var lowMapping = new WotTargetMappingDescriptor(
                targetNodeId: "nsu=urn:wot-observe;s=Value",
                targetTypeNodeId: "nsu=urn:wot-observe;i=9102", fieldPath: "Low");
            var highMapping = new WotTargetMappingDescriptor(
                targetNodeId: "nsu=urn:wot-observe;s=Value",
                targetTypeNodeId: "nsu=urn:wot-observe;i=9102", fieldPath: "High");
            WotCompiledForm lowObserve = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty, lowMapping, affordanceName: "low", formIndex: 1);
            WotCompiledForm highObserve = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty, highMapping, affordanceName: "high", formIndex: 1);
            WotCompiledForm lowRead = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, lowMapping, affordanceName: "low");
            WotCompiledForm highRead = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, highMapping, affordanceName: "high");
            var lowStarted = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var highStarted = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lowSubscription = new Mock<IWotSubscription>();
            var highSubscription = new Mock<IWotSubscription>();
            lowSubscription.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            highSubscription.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            var lowObserver = new FakeWotBindingChannel(lowObserve)
            {
                OnObserve = (notification, _) =>
                {
                    lowStarted.TrySetResult(notification);
                    return new ValueTask<IWotSubscription>(lowSubscription.Object);
                }
            };
            var highObserver = new FakeWotBindingChannel(highObserve)
            {
                OnObserve = (notification, _) =>
                {
                    highStarted.TrySetResult(notification);
                    return new ValueTask<IWotSubscription>(highSubscription.Object);
                }
            };
            var lowReader = new FakeWotBindingChannel(lowRead)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(111.0))))
            };
            var highReader = new FakeWotBindingChannel(highRead)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(222.0))))
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(lowObserve, lowObserver);
            channels.SetChannel(highObserve, highObserver);
            channels.SetChannel(lowRead, lowReader);
            channels.SetChannel(highRead, highReader);
            WotCompiledForm[] forms = separateRead
                ? [lowRead, highRead, lowObserve, highObserve]
                : [lowObserve, highObserve];

            await WithProjectionAsync(forms, channels, async (session, _) =>
            {
                RegisterObservedRange(session.Factory);
                var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create(), session.Factory)
                {
                    NamespaceUris = new NamespaceTable(session.NamespaceUris)
                };
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 50
                };
                MonitoredItem item = CreateItem(session, subscription);
                if (xmlEncoding)
                {
                    item.Encoding = new QualifiedName(Ua.BrowseNames.DefaultXml);
                }
                ChannelReader<DataValue> values = Watch(item);
                subscription.AddItem(item);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    Action<WotNotification> publishLow = await lowStarted.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Action<WotNotification> publishHigh = await highStarted.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    var later = new DateTimeUtc(2026, 2, 4, 0, 0, 0);
                    var earlier = new DateTimeUtc(2026, 2, 3, 0, 0, 0);
                    publishLow(new WotNotification(
                        new DataValue(new Variant(1.5), StatusCodes.UncertainInitialValue, later)));
                    DataValue incomplete = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(incomplete.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                    Assert.That(incomplete.WrappedValue.IsNull, Is.True);
                    publishHigh(new WotNotification(new DataValue(new Variant(9.5), StatusCodes.Good, earlier)));
                    DataValue first = await ReceiveAsync(values).ConfigureAwait(false);
                    AssertRange(first, 1.5, 9.5, StatusCodes.UncertainInitialValue, earlier, messageContext);
                    if (xmlEncoding)
                    {
                        Assert.That(first.WrappedValue.TryGetValue(out ExtensionObject encoded), Is.True);
                        Assert.That(ExpandedNodeId.ToNodeId(encoded.TypeId, session.NamespaceUris),
                            Is.EqualTo(ExpandedNodeId.Parse("nsu=urn:wot-observe;i=9104", session.NamespaceUris)));
                    }
                    Assert.That(lowReader.ReadCount, Is.Zero);
                    Assert.That(highReader.ReadCount, Is.Zero);

                    if (separateRead)
                    {
                        DataValue read = await session.ReadValueAsync(item.ResolvedNodeId).ConfigureAwait(false);
                        Assert.That(read.WrappedValue.TryGetValue(out ExtensionObject body), Is.True);
                        Assert.That(body.TryGetValue(out ObservedRange? range), Is.True);
                        Assert.That(range!.Low, Is.EqualTo(111.0));
                        Assert.That(range.High, Is.EqualTo(222.0));
                        Assert.That(lowReader.ReadCount, Is.EqualTo(1));
                        Assert.That(highReader.ReadCount, Is.EqualTo(1));
                    }

                    publishLow(new WotNotification(new DataValue(new Variant("invalid"), StatusCodes.Good, later)));
                    DataValue malformed = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(malformed.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                    Assert.That(malformed.WrappedValue.IsNull, Is.True);
                    publishLow(new WotNotification(new DataValue(new Variant(2.5), StatusCodes.GoodClamped, later)));
                    AssertRange(await ReceiveAsync(values).ConfigureAwait(false),
                        2.5, 9.5, StatusCodes.GoodClamped, earlier, messageContext);
                    Assert.That(lowObserver.ObserveCount, Is.EqualTo(1));
                    Assert.That(highObserver.ObserveCount, Is.EqualTo(1));
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                lowSubscription.Verify(s => s.DisposeAsync(), Times.Once);
                highSubscription.Verify(s => s.DisposeAsync(), Times.Once);
            }, dataType: "ns=1;i=9102",
                runtimeFactory: new RecordingRuntimeFactory(channels, RegisterObservedRange),
                additionalNodes: CreateObservedRangeNodes()).ConfigureAwait(false);
        }

        [Test]
        public async Task StructuredStartupFailureReleasesAcquiredFieldsAndRejectsTheirLateValues()
        {
            WotCompiledForm high = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value",
                    targetTypeNodeId: "nsu=urn:wot-observe;i=9102", fieldPath: "High"),
                affordanceName: "high");
            WotCompiledForm low = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value",
                    targetTypeNodeId: "nsu=urn:wot-observe;i=9102", fieldPath: "Low"),
                affordanceName: "low");
            var highLease = new Mock<IWotSubscription>();
            var lowLease = new Mock<IWotSubscription>();
            highLease.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            lowLease.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            Action<WotNotification>? highCallback = null;
            Action<WotNotification>? lowCallback = null;
            bool failLow = true;
            var highChannel = new FakeWotBindingChannel(high)
            {
                OnObserve = (callback, _) =>
                {
                    highCallback = callback;
                    return new ValueTask<IWotSubscription>(highLease.Object);
                }
            };
            var lowChannel = new FakeWotBindingChannel(low)
            {
                OnObserve = (callback, _) =>
                {
                    if (failLow)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoCommunication);
                    }
                    lowCallback = callback;
                    return new ValueTask<IWotSubscription>(lowLease.Object);
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(high, highChannel);
            channels.SetChannel(low, lowChannel);
            await WithProjectionAsync([high, low], channels, async (session, _) =>
            {
                RegisterObservedRange(session.Factory);
                var messageContext = new ServiceMessageContext(NUnitTelemetryContext.Create(), session.Factory)
                {
                    NamespaceUris = new NamespaceTable(session.NamespaceUris)
                };
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
                    DataValue failure = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
                    Assert.That(failure.WrappedValue.IsNull, Is.True);
                    highLease.Verify(s => s.DisposeAsync(), Times.Once);
                    lowLease.Verify(s => s.DisposeAsync(), Times.Never);
                    Action<WotNotification> retiredHigh = highCallback!;

                    await subscription.SetMonitoringModeAsync(MonitoringMode.Disabled, [item]).ConfigureAwait(false);
                    failLow = false;
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, [item]).ConfigureAwait(false);
                    var timestamp = new DateTimeUtc(2026, 4, 5, 6, 7, 8);
                    retiredHigh(new WotNotification(new DataValue(new Variant(999.0), StatusCodes.Good, timestamp)));
                    lowCallback!(new WotNotification(new DataValue(new Variant(2.5), StatusCodes.Good, timestamp)));
                    DataValue incomplete = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(incomplete.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                    Assert.That(incomplete.WrappedValue.IsNull, Is.True);
                    highCallback!(new WotNotification(new DataValue(new Variant(10.5), StatusCodes.Good, timestamp)));
                    AssertRange(await ReceiveAsync(values).ConfigureAwait(false),
                        2.5, 10.5, StatusCodes.Good, timestamp, messageContext);
                    Assert.That(highChannel.ObserveCount, Is.EqualTo(2));
                    Assert.That(lowChannel.ObserveCount, Is.EqualTo(2));
                    Assert.That(highChannel.ReadCount, Is.Zero);
                    Assert.That(lowChannel.ReadCount, Is.Zero);
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                highLease.Verify(s => s.DisposeAsync(), Times.Exactly(2));
                lowLease.Verify(s => s.DisposeAsync(), Times.Once);
            }, dataType: "ns=1;i=9102",
                runtimeFactory: new RecordingRuntimeFactory(channels, RegisterObservedRange),
                additionalNodes: CreateObservedRangeNodes()).ConfigureAwait(false);
        }

        private static void AssertRange(
            in DataValue value,
            double low,
            double high,
            StatusCode status,
            DateTimeUtc timestamp,
            IServiceMessageContext context)
        {
            Assert.That(value.StatusCode, Is.EqualTo(status));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject body), Is.True);
            Assert.That(body.TryGetValue(out ObservedRange? range, context), Is.True);
            Assert.That(range!.Low, Is.EqualTo(low));
            Assert.That(range.High, Is.EqualTo(high));
        }
    }
}
