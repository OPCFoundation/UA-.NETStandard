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
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed partial class WotObservedPropertyRuntimeTests
    {
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        public async Task ObservationUsesItsOwnSourceAcrossMonitoringModes(
            bool separateRead, bool metadataSubscriber, bool separateDocuments)
        {
            string directory = Path.Combine(Path.GetTempPath(), "wot-observe-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            var sourceStarted = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"),
                formIndex: separateDocuments ? 0 : 1);
            WotCompiledForm read = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var subscription = new Mock<IWotSubscription>();
            subscription.Setup(s => s.DisposeAsync()).Returns(() =>
            {
                sourceStopped.TrySetResult(true);
                return default;
            });
            Action<WotNotification>? latestNotification = null;
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = (notification, _) =>
                {
                    latestNotification = notification;
                    sourceStarted.TrySetResult(notification);
                    return new ValueTask<IWotSubscription>(subscription.Object);
                }
            };
            var readChannel = new FakeWotBindingChannel(read)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(99))))
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(observe, channel);
            channels.SetChannel(read, readChannel);
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                var host = new LifecycleWotProjectionHost(
                    server.NodeManagerLifecycle, new WotProjectionBindingRuntimeFactory(channels));
                WotProjectionHandle handle = await host.AddAsync(
                    CreateProjection(separateRead ? [read, observe] : [observe],
                        bindingPlans: separateDocuments
                            ? [new WotBindingPlan("read-source", [], [read], [], []),
                                new WotBindingPlan("observe-source", [], [observe], [], [])]
                            : default)).ConfigureAwait(false);
                using var client = new ClientFixture(NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                using ISession session = await client.ConnectAsync(
                    new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", fixture.Port).Uri, SecurityPolicies.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(channel.ObserveCount, Is.Zero);
                    using var local = new Subscription(session.DefaultSubscription)
                    {
                        PublishingEnabled = true,
                        PublishingInterval = 50
                    };
                    var received = new TaskCompletionSource<DataValue>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var resumed = new TaskCompletionSource<DataValue>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var values = new ConcurrentQueue<int>();
                    var item = new MonitoredItem(local.DefaultItem)
                    {
                        StartNodeId = ExpandedNodeId.Parse("nsu=urn:wot-observe;s=Value", session.NamespaceUris),
                        AttributeId = Attributes.Value,
                        SamplingInterval = 0,
                        QueueSize = 10,
                        DiscardOldest = true
                    };
                    item.Notification += (_, _) =>
                    {
                        foreach (DataValue value in item.DequeueValues())
                        {
                            if (value.WrappedValue.TryGetValue(out int number))
                            {
                                values.Enqueue(number);
                                if (number == 42)
                                {
                                    received.TrySetResult(value);
                                }
                                if (number == 43)
                                {
                                    resumed.TrySetResult(value);
                                }
                            }
                        }
                    };
                    if (metadataSubscriber)
                    {
                        local.AddItem(new MonitoredItem(local.DefaultItem)
                        {
                            StartNodeId = item.StartNodeId,
                            AttributeId = Attributes.DisplayName,
                            SamplingInterval = 0
                        });
                    }
                    else
                    {
                        local.AddItem(item);
                    }
                    session.AddSubscription(local);
                    await local.CreateAsync().ConfigureAwait(false);
                    try
                    {
                        if (metadataSubscriber)
                        {
                            Assert.That(channel.ObserveCount, Is.Zero);
                            local.AddItem(item);
                            await local.ApplyChangesAsync().ConfigureAwait(false);
                        }
                        Action<WotNotification> publish = await sourceStarted.Task
                            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
                        publish(new WotNotification(new DataValue(
                            new Variant(42), StatusCodes.UncertainInitialValue, timestamp)));
                        DataValue value = await received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
                        Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
                        Assert.That(channel.ObserveCount, Is.EqualTo(1));
                        Assert.That(readChannel.ReadCount, Is.Zero);
                        if (separateRead)
                        {
                            DataValue current = await session.ReadValueAsync(item.ResolvedNodeId).ConfigureAwait(false);
                            Assert.That(current.WrappedValue.TryGetValue(out int number), Is.True);
                            Assert.That(number, Is.EqualTo(99));
                            Assert.That(readChannel.ReadCount, Is.EqualTo(1));
                        }
                        await local.SetMonitoringModeAsync(MonitoringMode.Disabled, [item]).ConfigureAwait(false);
                        await sourceStopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        await local.SetMonitoringModeAsync(MonitoringMode.Reporting, [item]).ConfigureAwait(false);
                        Assert.That(channel.ObserveCount, Is.EqualTo(2));
                        Assert.That(readChannel.ReadCount, Is.EqualTo(separateRead ? 1 : 0));
                        publish(new WotNotification(new DataValue(new Variant(-1))));
                        latestNotification!(new WotNotification(new DataValue(new Variant(43))));
                        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        Assert.That(values, Does.Not.Contain(99));
                        Assert.That(values, Does.Not.Contain(-1),
                            "A retired source cannot publish into its successor.");
                    }
                    finally
                    {
                        await local.DeleteAsync(true).ConfigureAwait(false);
                    }
                    await sourceStopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    subscription.Verify(s => s.DisposeAsync(), Times.Exactly(2));
                }
                finally
                {
                    await session.CloseAsync().ConfigureAwait(false);
                    await host.RemoveAsync(handle).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task ValueSubscribersShareTheirSourceAndResumeItsCurrentObservation()
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
                MonitoredItem first = CreateItem(session, subscription);
                MonitoredItem second = CreateItem(session, subscription);
                ChannelReader<DataValue> firstValues = Watch(first);
                ChannelReader<DataValue> secondValues = Watch(second);
                subscription.AddItem(first);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    Action<WotNotification> publish = await started.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    publish(new WotNotification(new DataValue(new Variant(42))));
                    AssertInteger(await ReceiveAsync(firstValues).ConfigureAwait(false), 42);

                    subscription.AddItem(second);
                    await subscription.ApplyChangesAsync().ConfigureAwait(false);
                    AssertInteger(await ReceiveAsync(secondValues).ConfigureAwait(false), 42);
                    Assert.That(channel.ObserveCount, Is.EqualTo(1));
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Disabled, [second]).ConfigureAwait(false);
                    publish(new WotNotification(new DataValue(new Variant(43))));
                    AssertInteger(await ReceiveAsync(firstValues).ConfigureAwait(false), 43);
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, [second]).ConfigureAwait(false);
                    AssertInteger(await ReceiveAsync(secondValues).ConfigureAwait(false), 43);
                    Assert.That(channel.ObserveCount, Is.EqualTo(1));

                    subscription.RemoveItem(first);
                    await subscription.ApplyChangesAsync().ConfigureAwait(false);
                    sourceSubscription.Verify(s => s.DisposeAsync(), Times.Never);
                    publish(new WotNotification(new DataValue(new Variant(44))));
                    AssertInteger(await ReceiveAsync(secondValues).ConfigureAwait(false), 44);
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                sourceSubscription.Verify(s => s.DisposeAsync(), Times.Once);
            }).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ObservedNodeIdsUseTheirSourceNamespaceAuthority(bool completeContext, bool missingAuthority)
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
                MonitoredItem item = CreateItem(session, subscription);
                ChannelReader<DataValue> values = Watch(item);
                subscription.AddItem(item);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    Action<WotNotification> publish = await started.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    var sourceNamespaces = new NamespaceTable();
                    ushort sourceIndex = sourceNamespaces.GetIndexOrAppend("urn:observed-source");
                    var timestamp = new DateTimeUtc(2026, 2, 3, 4, 5, 6);
                    var sourceValue = new DataValue(
                        new Variant(new NodeId("Sensor", sourceIndex)), StatusCodes.UncertainInitialValue, timestamp);
                    WotNotification notification = missingAuthority
                        ? new WotNotification(sourceValue)
                        : new WotNotification(sourceValue, null, null, sourceNamespaces.ToArrayOf());
                    if (completeContext)
                    {
                        notification = notification.WithContext(
                            new ServiceMessageContext(NUnitTelemetryContext.Create(), session.Factory)
                            {
                                NamespaceUris = sourceNamespaces
                            });
                    }
                    publish(notification);
                    DataValue observed = await ReceiveAsync(values).ConfigureAwait(false);
                    if (missingAuthority)
                    {
                        Assert.That(observed.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
                        Assert.That(observed.WrappedValue.IsNull, Is.True);
                        return;
                    }
                    Assert.That(observed.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
                    Assert.That(observed.SourceTimestamp, Is.EqualTo(timestamp));
                    Assert.That(observed.WrappedValue.TryGetValue(out NodeId mapped), Is.True);
                    Assert.That(mapped.TryGetValue(out string identifier), Is.True);
                    Assert.That(identifier, Is.EqualTo("Sensor"));
                    Assert.That(mapped.NamespaceIndex, Is.Not.EqualTo(sourceIndex));
                    await session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                    Assert.That(session.NamespaceUris.GetString(mapped.NamespaceIndex),
                        Is.EqualTo("urn:observed-source"));
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
            }, dataType: "i=17").ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingTheGenerationCancelsAnObservationThatIsStarting(bool openingChannel)
        {
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceSubscription = new Mock<IWotSubscription>();
            sourceSubscription.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            async Task WaitForCancellationAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                try
                {
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
            }
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = async (_, token) =>
                {
                    await WaitForCancellationAsync(token).ConfigureAwait(false);
                    return sourceSubscription.Object;
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(observe, channel);
            if (openingChannel)
            {
                channels.SetOpener(observe, async token =>
                {
                    await WaitForCancellationAsync(token).ConfigureAwait(false);
                    return channel;
                });
            }
            var runtimeFactory = new RecordingRuntimeFactory(channels);
            await WithProjectionAsync([observe], channels, async (session, _) =>
            {
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 50
                };
                subscription.AddItem(CreateItem(session, subscription));
                session.AddSubscription(subscription);
                Task creating = subscription.CreateAsync();
                Task? disposing = null;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(runtimeFactory.Runtime, Is.Not.Null);
                    disposing = runtimeFactory.Runtime!.DisposeAsync().AsTask();
                    await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(channel.ObserveCount, Is.EqualTo(openingChannel ? 0 : 1));
                    Assert.That(channel.DisposeCount, Is.EqualTo(openingChannel ? 0 : 1));
                    sourceSubscription.Verify(s => s.DisposeAsync(), Times.Never);
                }
                finally
                {
                    release.TrySetResult(true);
                    await creating.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (disposing is not null)
                    {
                        await disposing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    if (subscription.Created)
                    {
                        await subscription.DeleteAsync(true).ConfigureAwait(false);
                    }
                }
            }, runtimeFactory: runtimeFactory).ConfigureAwait(false);
        }

        [Test]
        public async Task ObservationStopsDisclosingValuesWhenTheSubscriberLosesReadPermission()
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
            var runtimeFactory = new RecordingRuntimeFactory(channels);
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
                    publish(new WotNotification(new DataValue(new Variant(42))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 42);
                    Assert.That(runtimeFactory.ObservedNode, Is.Not.Null);
                    INodeBuilder target = runtimeFactory.ObservedNode!;
                    target.Node.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                        }
                    ];
                    await target.Node.ClearChangeMasksAsync(target.Builder.Context, false).ConfigureAwait(false);
                    ReadResponse read = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Both,
                        [new ReadValueId { NodeId = item.ResolvedNodeId, AttributeId = Attributes.Value }],
                        CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                    publish(new WotNotification(new DataValue(new Variant(43))));
                    DataValue denied = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(denied.WrappedValue.IsNull, Is.True);
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
            }, runtimeFactory: runtimeFactory).ConfigureAwait(false);
        }

        [Test]
        public async Task ObservationStartupReportsItsFailureAndCanRetryWithoutReading()
        {
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var started = new TaskCompletionSource<Action<WotNotification>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceSubscription = new Mock<IWotSubscription>();
            sourceSubscription.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            int attempts = 0;
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = (notification, _) =>
                {
                    if (++attempts == 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported, "Observation is unavailable.");
                    }
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
                MonitoredItem item = CreateItem(session, subscription);
                ChannelReader<DataValue> values = Watch(item);
                subscription.AddItem(item);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                try
                {
                    DataValue failed = await ReceiveAsync(values).ConfigureAwait(false);
                    Assert.That(failed.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(failed.WrappedValue.IsNull, Is.True);
                    Assert.That(channel.ReadCount, Is.Zero);
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Disabled, [item]).ConfigureAwait(false);
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, [item]).ConfigureAwait(false);
                    Action<WotNotification> publish = await started.Task
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    publish(new WotNotification(new DataValue(new Variant(57))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 57);
                    Assert.That(channel.ObserveCount, Is.EqualTo(2));
                    Assert.That(channel.ReadCount, Is.Zero);
                    Assert.That(channels.OpenCount, Is.EqualTo(1));
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                sourceSubscription.Verify(s => s.DisposeAsync(), Times.Once);
            }).ConfigureAwait(false);
        }

        private static MonitoredItem CreateItem(ISession session, Subscription subscription)
        {
            return new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ExpandedNodeId.Parse("nsu=urn:wot-observe;s=Value", session.NamespaceUris),
                AttributeId = Attributes.Value,
                SamplingInterval = 0,
                QueueSize = 10,
                DiscardOldest = true
            };
        }

        private static ChannelReader<DataValue> Watch(MonitoredItem item)
        {
            var values = Channel.CreateUnbounded<DataValue>();
            item.Notification += (_, _) =>
            {
                foreach (DataValue value in item.DequeueValues())
                {
                    values.Writer.TryWrite(value);
                }
            };
            return values.Reader;
        }

        private static Task<DataValue> ReceiveAsync(ChannelReader<DataValue> values)
        {
            return values.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static void AssertInteger(in DataValue value, int expected)
        {
            Assert.That(StatusCode.IsNotBad(value.StatusCode), Is.True, value.StatusCode.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(expected));
        }

        private static async Task WithProjectionAsync(
            WotCompiledForm[] forms,
            IWotBindingChannelFactory channels,
            Func<ISession, Func<ValueTask>, Task> action,
            string dataType = "i=6",
            int valueRank = ValueRanks.Scalar,
            IWotProjectionBindingRuntimeFactory? runtimeFactory = null,
            UANode[]? additionalNodes = null,
            Action<Func<ValueTask>>? configureReload = null,
            bool migrateMonitoredItems = false)
        {
            string directory = Path.Combine(Path.GetTempPath(), "wot-observe-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                IWotProjectionBindingRuntimeFactory bindings =
                    runtimeFactory ?? new WotProjectionBindingRuntimeFactory(channels);
                Func<ValueTask> reload;
                Func<ValueTask> remove;
                WotProjectionDocument projection = CreateProjection(forms, dataType, valueRank, additionalNodes);
                if (migrateMonitoredItems)
                {
                    var options = new RuntimeNodeSetOptions
                    {
                        Sources =
                        [
                            RuntimeNodeSetSource.FromStream(
                                projection.Sources[0].Name,
                                _ => new ValueTask<Stream>(new MemoryStream(projection.Sources[0].NodeSetXml, false)),
                                ["urn:wot-observe"])
                        ],
                        ConfigureAsync = (builder, token) =>
                            bindings.CreateAsync(builder, projection.BindingPlans, token)
                    };
                    Ua.Server.NodeManagerRegistration registration = await server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(options, callerContext: null).ConfigureAwait(false);
                    reload = async () => registration = await server.NodeManagerLifecycle
                            .ReloadRuntimeNodeSetAsync(registration, options, callerContext: null)
                            .ConfigureAwait(false);
                    remove = () => server.NodeManagerLifecycle.RemoveAsync(registration, callerContext: null);
                }
                else
                {
                    var host = new LifecycleWotProjectionHost(server.NodeManagerLifecycle, bindings);
                    WotProjectionHandle handle = await host.AddAsync(projection).ConfigureAwait(false);
                    reload = async () => handle = await host.ShadowReloadAsync(handle, projection).ConfigureAwait(false);
                    remove = () => host.RemoveAsync(handle);
                }
                configureReload?.Invoke(reload);
                Task? retirement = null;
                async Task RetireCoreAsync() => await remove().ConfigureAwait(false);
                ValueTask RetireAsync() => new(retirement ??= RetireCoreAsync());
                using var client = new ClientFixture(NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                using ISession session = await client.ConnectAsync(
                    new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", fixture.Port).Uri, SecurityPolicies.None)
                    .ConfigureAwait(false);
                try
                {
                    await action(session, RetireAsync).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await RetireAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static WotProjectionDocument CreateProjection(
            WotCompiledForm[] forms,
            string dataType = "i=6",
            int valueRank = ValueRanks.Scalar,
            UANode[]? additionalNodes = null,
            ArrayOf<WotBindingPlan> bindingPlans = default)
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:wot-observe"],
                Models = [new ModelTableEntry { ModelUri = "urn:wot-observe", Version = "1.0.0" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;s=Device",
                        BrowseName = "1:Device",
                        References =
                        [
                            new Reference { ReferenceType = "i=35", IsForward = false, Value = "i=85" },
                            new Reference { ReferenceType = "i=47", Value = "ns=1;s=Value" },
                            new Reference { ReferenceType = "i=40", Value = "i=58" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;s=Value",
                        BrowseName = "1:Value",
                        ParentNodeId = "ns=1;s=Device",
                        DataType = dataType,
                        ValueRank = valueRank,
                        AccessLevel = AccessLevels.CurrentRead,
                        UserAccessLevel = AccessLevels.CurrentRead,
                        References = [new Reference { ReferenceType = "i=40", Value = "i=63" }]
                    }
                ]
            };
            if (additionalNodes is not null)
            {
                nodeSet.Items = [.. nodeSet.Items ?? [], .. additionalNodes];
            }
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return new WotProjectionDocument(
                "observed-property",
                [new WotProjectionSource("observed-property", ["urn:wot-observe"], stream.ToArray())],
                bindingPlans.IsNull ? [WotProjectionBindingRuntimeTestHarness.Plan(forms)] : bindingPlans);
        }

        private sealed class RecordingRuntimeFactory : IWotProjectionBindingRuntimeFactory
        {
            public RecordingRuntimeFactory(
                IWotBindingChannelFactory channels,
                Action<IEncodeableFactory>? configureTypes = null,
                WotProjectionBindingRuntimeOptions? options = null,
                Action<INodeBuilder>? configureNode = null)
            {
                m_inner = options is null
                    ? new WotProjectionBindingRuntimeFactory(channels)
                    : new WotProjectionBindingRuntimeFactory(
                        channels,
                        null,
                        new WotProjectionEventPublisher(),
                        new WotProjectionConditionFactory(),
                        options);
                m_configureTypes = configureTypes;
                m_configureNode = configureNode;
            }

            public IAsyncDisposable? Runtime { get; private set; }

            public INodeBuilder? ObservedNode { get; private set; }

            public async ValueTask<IAsyncDisposable?> CreateAsync(
                INodeManagerBuilder builder,
                ArrayOf<WotBindingPlan> plans,
                CancellationToken cancellationToken = default)
            {
                m_configureTypes?.Invoke(builder.Context.EncodeableFactory);
                Runtime = await m_inner.CreateAsync(builder, plans, cancellationToken).ConfigureAwait(false);
                ObservedNode = builder.Node(ExpandedNodeId.Parse(
                    "nsu=urn:wot-observe;s=Value", builder.Context.NamespaceUris));
                m_configureNode?.Invoke(ObservedNode);
                return Runtime;
            }

            private readonly WotProjectionBindingRuntimeFactory m_inner;
            private readonly Action<IEncodeableFactory>? m_configureTypes;
            private readonly Action<INodeBuilder>? m_configureNode;
        }
    }
}
