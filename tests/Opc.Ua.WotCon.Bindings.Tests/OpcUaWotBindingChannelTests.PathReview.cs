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
 *
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotBindingChannelTests
    {
        [Test]
        public async Task LivePathInitialCreationCannotReplaceItsAdmittedGeneration()
        {
            using var clock = new PathClock();
            bool changed = false;
            int notifications = 0;
            Mock<ISession> proxy = PathSessionProxy((requests, token) => changed
                ? new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
                })
                : m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token));
            proxy.SetupGet(value => value.SessionId).Returns(() =>
                changed ? new NodeId("reactivated", 0) : m_session.SessionId);
            proxy.Setup(value => value.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) =>
                {
                    changed = true;
                    proxy.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
                    return m_session.AddSubscription(subscription);
                });
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/StartTime", "i=85");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            int before = m_session.Subscriptions.Count();
            IWotSubscription? lifetime = null;
            try
            {
                ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    lifetime = await channel.ObserveAsync(_ => Interlocked.Increment(ref notifications))
                        .ConfigureAwait(false));

                Assert.That(error!.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState).Or.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(changed, Is.True);
                Assert.That(notifications, Is.Zero);
                Assert.That(m_session.Subscriptions.Count(), Is.EqualTo(before));
            }
            finally
            {
                if (lifetime is not null)
                {
                    await lifetime.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task LivePathReplacementRemainsDisabledUntilItsCurrentGenerationIsReady()
        {
            using var clock = new PathClock();
            int translations = 0;
            int changes = 0;
            var nextStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var next = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISession> proxy = PathSessionProxy((requests, token) =>
            {
                int count = Interlocked.Increment(ref translations);
                if (count == 1)
                {
                    return m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token);
                }
                if (count == 3)
                {
                    nextStarted.TrySetResult(true);
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(next.Task.WaitAsync(token));
                }
                return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(PathTranslation(new NodeId(2257)));
            });
            Subscription? nativeSubscription = null;
            proxy.Setup(value => value.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) =>
                {
                    nativeSubscription = subscription;
                    subscription.StateChanged += (sender, _) =>
                    {
                        MonitoredItem? item = sender.MonitoredItems.SingleOrDefault();
                        if (item is not null &&
                            item.Created &&
                            item.StartNodeId == new NodeId(2257) &&
                            Interlocked.CompareExchange(ref changes, 1, 0) == 0)
                        {
                            proxy.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
                        }
                    };
                    return m_session.AddSubscription(subscription);
                });
            DataValue startTime = await m_session.ReadValueAsync(new NodeId(2257)).ConfigureAwait(false);
            Assert.That(startTime.WrappedValue.TryGetValue(out DateTimeUtc expected), Is.True);
            var initial = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovered = new TaskCompletionSource<WotNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/CurrentTime", "i=85");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            await using IWotSubscription lifetime = await channel.ObserveAsync(value =>
            {
                if (StatusCode.IsGood(value.Value.StatusCode))
                {
                    initial.TrySetResult(true);
                    if (value.Value.WrappedValue.TryGetValue(out DateTimeUtc time) && time == expected)
                    {
                        recovered.TrySetResult(value);
                    }
                }
            }).ConfigureAwait(false);
            try
            {
                await initial.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                clock.Tick();
                await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                Assert.That(changes, Is.EqualTo(1));
                Assert.That(nativeSubscription!.MonitoredItems.Single().MonitoringMode,
                    Is.EqualTo(MonitoringMode.Disabled));
                Assert.That(recovered.Task.IsCompleted, Is.False);
                next.TrySetResult(PathTranslation(new NodeId(2257)));
                WotNotification notification = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);

                Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notification.Value.WrappedValue.TryGetValue(out DateTimeUtc actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            }
            finally
            {
                next.TrySetResult(PathTranslation(new NodeId(2257)));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LivePathNotificationRetainsItsAdmittedNamespaceMeaning(bool race)
        {
            using var clock = new PathClock();
            NamespaceTable namespaces = new(m_session.NamespaceUris);
            string expectedNamespace = namespaces.GetString(1) ??
                throw new InvalidOperationException("The source fixture must declare namespace one.");
            string[] reordered = namespaces.ToArray();
            (reordered[1], reordered[2]) = (reordered[2], reordered[1]);
            bool armed = false;
            bool changed = false;
            int accesses = 0;
            Mock<ISession> proxy = PathSessionProxy((requests, token) =>
                m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token));
            proxy.SetupGet(value => value.NamespaceUris).Returns(() =>
            {
                if (armed && race && Interlocked.Increment(ref accesses) == 2)
                {
                    namespaces = new NamespaceTable(reordered);
                    changed = true;
                    proxy.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
                }
                return namespaces;
            });
            var initial = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WotNotification? marker = null;
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/StartTime", "i=85");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            await using IWotSubscription lifetime = await channel.ObserveAsync(value =>
            {
                initial.TrySetResult(true);
                if (value.Value.WrappedValue.TryGetValue(out NodeId id) && id == new NodeId("marker", 1))
                {
                    marker = value;
                }
            }).ConfigureAwait(false);
            await initial.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            MonitoredItem item = m_session.Subscriptions.Single(value => value.DisplayName == "wot-path")
                .MonitoredItems.Single();
            armed = true;

            item.SaveValueInCache(new MonitoredItemNotification
            {
                ClientHandle = item.ClientHandle,
                Value = new DataValue(new Variant(new NodeId("marker", 1)))
            });
            armed = false;

            if (changed)
            {
                Assert.That(marker, Is.Null, "A known invalidated admission must not publish a payload.");
            }
            else
            {
                Assert.That(marker, Is.Not.Null, "A current native route must deliver the marker.");
                Assert.That(marker!.Context, Is.Not.Null);
                Assert.That(marker.Context!.NamespaceUris.GetString(1), Is.EqualTo(expectedNamespace));
            }
        }
    }
}
