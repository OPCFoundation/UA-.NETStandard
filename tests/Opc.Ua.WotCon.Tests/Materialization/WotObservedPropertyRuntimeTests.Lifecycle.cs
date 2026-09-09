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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotObservedPropertyRuntimeTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RetainedMonitoredItemsUseTheCorrectSourceForTheReloadPolicy(
            bool migrateMonitoredItems, bool failedPreparation)
        {
            var mapping = new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value");
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty, mapping, formIndex: 1);
            WotCompiledForm read = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping);
            var observers = Channel.CreateUnbounded<Action<WotNotification>>();
            var leases = new ConcurrentQueue<Mock<IWotSubscription>>();
            var channels = new FakeWotBindingChannelFactory();
            var reader = new FakeWotBindingChannel(read)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(99))))
            };
            channels.SetChannel(read, reader);
            channels.SetOpener(observe, () => new ValueTask<IWotBindingChannel>(new FakeWotBindingChannel(observe)
            {
                OnObserve = (notification, _) =>
                {
                    var lease = new Mock<IWotSubscription>();
                    lease.Setup(subscription => subscription.DisposeAsync()).Returns(default(ValueTask));
                    leases.Enqueue(lease);
                    observers.Writer.TryWrite(notification);
                    return new ValueTask<IWotSubscription>(lease.Object);
                }
            }));
            int generations = 0;
            var factory = new RecordingRuntimeFactory(
                channels,
                configureTypes: _ =>
                {
                    if (Interlocked.Increment(ref generations) == 2 && failedPreparation)
                    {
                        throw new InvalidOperationException("Replacement preparation failed.");
                    }
                },
                configureNode: node =>
                {
                    Assert.That(node.Node, Is.InstanceOf<BaseVariableState>());
                    var variable = (BaseVariableState)node.Node;
                    variable.Value = new Variant(99);
                    variable.StatusCode = StatusCodes.Good;
                });
            Func<ValueTask>? reload = null;

            await WithProjectionAsync([read, observe], channels, async (session, _) =>
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
                    Action<WotNotification> first = await observers.Reader.ReadAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    first(new WotNotification(new DataValue(new Variant(42))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 42);
                    uint itemId = item.Status.Id;

                    if (failedPreparation)
                    {
                        Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await reload!().ConfigureAwait(false));
                        Assert.That(leases, Has.Count.EqualTo(1));
                        leases.ToArray()[0].Verify(lease => lease.DisposeAsync(), Times.Never);
                        first(new WotNotification(new DataValue(new Variant(44))));
                        AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 44);
                    }
                    await reload!().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    if (!migrateMonitoredItems)
                    {
                        Assert.That(leases, Has.Count.EqualTo(1));
                        first(new WotNotification(new DataValue(new Variant(43))));
                        AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 43);
                        return;
                    }
                    Action<WotNotification> replacement = await observers.Reader.ReadAsync().AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(item.Status.Id, Is.EqualTo(itemId), "The client retains its existing monitored item.");
                    Assert.That(leases, Has.Count.EqualTo(2));
                    Mock<IWotSubscription>[] subscriptions = [.. leases];
                    subscriptions[0].Verify(lease => lease.DisposeAsync(), Times.Once);
                    subscriptions[1].Verify(lease => lease.DisposeAsync(), Times.Never);
                    first(new WotNotification(new DataValue(new Variant(-1))));
                    replacement(new WotNotification(new DataValue(new Variant(43))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 43);
                    Assert.That(reader.ReadCount, Is.Zero);
                }
                finally
                {
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                    foreach (Mock<IWotSubscription> lease in leases)
                    {
                        lease.Verify(subscription => subscription.DisposeAsync(), Times.Once);
                    }
                }
            }, runtimeFactory: factory, configureReload: callback => reload = callback,
                migrateMonitoredItems: migrateMonitoredItems).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LastDisabledSubscriberCancelsStartupButNotTheSharedChannel(bool openingChannel)
        {
            WotCompiledForm observe = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: "nsu=urn:wot-observe;s=Value"));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lease = new Mock<IWotSubscription>();
            lease.Setup(subscription => subscription.DisposeAsync()).Returns(default(ValueTask));
            CancellationToken sharedOpenToken = default;
            Action<WotNotification>? publish = null;
            int observations = 0;
            var channel = new FakeWotBindingChannel(observe)
            {
                OnObserve = async (notification, token) =>
                {
                    if (!openingChannel && Interlocked.Increment(ref observations) == 1)
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
                    publish = notification;
                    return lease.Object;
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetOpener(observe, async token =>
            {
                sharedOpenToken = token;
                if (openingChannel)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
                return channel;
            });
            await WithProjectionAsync([observe], channels, async (session, _) =>
            {
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 50
                };
                MonitoredItem item = CreateItem(session, subscription);
                item.MonitoringMode = MonitoringMode.Disabled;
                ChannelReader<DataValue> values = Watch(item);
                subscription.AddItem(item);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
                async Task<SetMonitoringModeResponse> SetModeAsync(MonitoringMode mode)
                {
                    return await session.SetMonitoringModeAsync(
                        null, subscription.Id, mode, [item.Status.Id], CancellationToken.None).ConfigureAwait(false);
                }
                Task<SetMonitoringModeResponse> enabling = SetModeAsync(MonitoringMode.Reporting);
                Task<SetMonitoringModeResponse>? disabling = null;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    disabling = SetModeAsync(MonitoringMode.Disabled);
                    SetMonitoringModeResponse disabled = await disabling.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                    Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
                    await enabling.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (!openingChannel)
                    {
                        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    Assert.That(sharedOpenToken.IsCancellationRequested, Is.False);
                    lease.Verify(subscription => subscription.DisposeAsync(), Times.Never);

                    release.TrySetResult(true);
                    await subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, [item]).ConfigureAwait(false);
                    Assert.That(channels.OpenCount, Is.EqualTo(1));
                    Assert.That(publish, Is.Not.Null);
                    publish!(new WotNotification(new DataValue(new Variant(42))));
                    AssertInteger(await ReceiveAsync(values).ConfigureAwait(false), 42);
                }
                finally
                {
                    release.TrySetResult(true);
                    await enabling.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (disabling is not null)
                    {
                        await disabling.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    await subscription.DeleteAsync(true).ConfigureAwait(false);
                }
                lease.Verify(subscription => subscription.DisposeAsync(), Times.Once);
            }).ConfigureAwait(false);
        }
    }
}
