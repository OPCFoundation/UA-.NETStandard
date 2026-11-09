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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class MonitorMutationTests
{
    [Test]
    public async Task DirectOfflineConstructionDoesNotRequireAnAvaloniaMessageLoop()
    {
        Assert.That(Avalonia.Application.Current, Is.Null,
            "Run the non-desktop tests separately from the real-backend desktop fixtures.");
        var model = new SubscriptionViewModel("Offline", null, NullLogger.Instance);
        try
        {
            await model.AddItemAsync(Item("A")).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await model.ConfigureItemAsync(model.Items[0], Settings(75))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(75)));
            await model.RemoveItemAsync(model.Items[0]).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(model.Items, Is.Empty);
        }
        finally
        {
            await model.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task AlreadyCancelledAttachmentStillDisposesTheSuppliedAdapter()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        var adapter = new DeferredAdapter();

        await Assert.ThatAsync(() => model.AttachAdapterAsync(adapter.Adapter, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(adapter.DisposeCount, Is.EqualTo(1));
        Assert.That(model.IsBound, Is.False);
    }

    [Test]
    public async Task AlreadyCancelledConfigurationDoesNotChangeLocalIntent()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        MonitoredItemConfig original = model.Items[0];
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => model.ConfigureItemAsync(original, Settings(75), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(model.Items.Single(), Is.EqualTo(original));
    }

    [Test]
    public async Task OfflineEditsRetainIdentityAndNeverReuseRemovedIds()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        await model.AddItemAsync(Item("B")).ConfigureAwait(false);
        MonitoredItemConfig first = model.Items[0];
        MonitoredItemConfig second = model.Items[1];
        await model.RemoveItemAsync(first).ConfigureAwait(false);
        await model.ConfigureItemAsync(second, Settings(75)).ConfigureAwait(false);
        await model.AddItemAsync(Item("C")).ConfigureAwait(false);

        Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(new[] { second.Id, second.Id + 1 }));
        Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(75)));
        Assert.That(model.Items[1].Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task DeferredConfigurationCannotOverwriteAnotherItemsShiftedIndex()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        await model.AddItemAsync(Item("B")).ConfigureAwait(false);
        var adapter = new DeferredAdapter();
        await model.AttachAdapterAsync(adapter.Adapter).ConfigureAwait(false);
        MonitoredItemConfig first = model.Items[0];
        MonitoredItemConfig second = model.Items[1];
        HeldMutation held = adapter.HoldNext("configure");
        try
        {
            Task configure = model.ConfigureItemAsync(second, Settings(75));
            await held.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task remove = model.RemoveItemAsync(first);
            Task add = model.AddItemAsync(Item("C"));
            Assert.That(model.Items.Select(item => item.DisplayName), Is.EqualTo(s_expectedSurvivors));
            held.Release();
            await Task.WhenAll(configure, remove, add).ConfigureAwait(false);

            Assert.That(model.Items[0].Id, Is.EqualTo(second.Id));
            Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(75)));
            Assert.That(model.Items[1].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1000)));
            Assert.That(adapter.ConfiguredIds, Is.EqualTo(new[] { second.Id }));
            Assert.That(adapter.DisposedWhileRunning, Is.False);
        }
        finally
        {
            held.Release();
        }
    }

    [TestCase("add", "close")]
    [TestCase("remove", "close")]
    [TestCase("configure", "close")]
    [TestCase("publishing", "close")]
    [TestCase("mode", "close")]
    [TestCase("add", "disconnect")]
    [TestCase("remove", "disconnect")]
    [TestCase("configure", "disconnect")]
    [TestCase("publishing", "disconnect")]
    [TestCase("mode", "disconnect")]
    [TestCase("add", "replace")]
    [TestCase("remove", "replace")]
    [TestCase("configure", "replace")]
    [TestCase("publishing", "replace")]
    [TestCase("mode", "replace")]
    public async Task LifetimeCancelsAndDrainsEveryDirectMutationBeforeDisposal(string operation, string transition)
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        await model.AddItemAsync(Item("B")).ConfigureAwait(false);
        var old = new DeferredAdapter();
        await model.AttachAdapterAsync(old.Adapter).ConfigureAwait(false);
        HeldMutation held = old.HoldNext(operation);
        old.LateHandle = 999;
        try
        {
            MonitoredItemConfig item = model.Items[1];
            Task mutation = operation switch
            {
                "add" => model.AddItemAsync(Item("C")),
                "remove" => model.RemoveItemAsync(item),
                "configure" => model.ConfigureItemAsync(item, Settings(75)),
                "publishing" => model.ApplySubscriptionAsync(model.Subscription with { Priority = 7 }),
                "mode" => model.SetMonitoringModeAsync(model.ItemStatuses[1], MonitoringMode.Sampling),
                _ => throw new ArgumentException("Unknown operation.", nameof(operation))
            };
            CancellationToken token = await held.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            MonitoredItemConfig[] accepted = [.. model.Items];
            SubscriptionConfig acceptedSubscription = model.Subscription;
            var replacement = new DeferredAdapter();
            Task transitionTask = transition switch
            {
                "close" => model.DisposeAsync().AsTask(),
                "disconnect" => model.DetachAdapterAsync().AsTask(),
                "replace" => model.AttachAdapterAsync(replacement.Adapter),
                _ => throw new ArgumentException("Unknown transition.", nameof(transition))
            };

            await held.Cancelled.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(held.IsRunning, Is.True);
            Assert.That(transitionTask.IsCompleted, Is.False);
            Assert.That(old.DisposeCount, Is.Zero);
            if (transition != "close")
            {
                await model.AddItemAsync(Item("Offline")).ConfigureAwait(false);
                await model.RemoveItemAsync(model.Items[0]).ConfigureAwait(false);
                accepted = [.. model.Items];
            }
            held.Release();
            if (operation == "configure")
            {
                await Assert.ThatAsync(() => mutation, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
            }
            else
            {
                await mutation.ConfigureAwait(false);
            }
            await transitionTask.ConfigureAwait(false);

            Assert.That(old.DisposeCount, Is.EqualTo(1));
            Assert.That(old.DisposedWhileRunning, Is.False);
            Assert.That(model.Items, Is.EqualTo(accepted));
            Assert.That(model.Subscription, Is.EqualTo(acceptedSubscription));
            Assert.That(model.Items.Any(config => config.Id == 999), Is.False);
            Assert.That(model.ErrorText, Is.Empty);
            if (transition == "replace")
            {
                Assert.That(replacement.Items.Values.OrderBy(config => config.Id),
                    Is.EqualTo(model.Items.OrderBy(config => config.Id)));
            }
            else if (transition == "close")
            {
                await model.DetachAdapterAsync().ConfigureAwait(false);
                await model.DisposeAsync().ConfigureAwait(false);
                Assert.That(old.DisposeCount, Is.EqualTo(1));
                await Assert.ThatAsync(() => model.AddItemAsync(Item("Rejected")),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
            }
        }
        finally
        {
            held.Release();
        }
    }

    [Test]
    public async Task CancellationSignalsBelongToTheCurrentMutationAfterAnEarlierCallCompletes()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        var adapter = new DeferredAdapter();
        await model.AttachAdapterAsync(adapter.Adapter).ConfigureAwait(false);
        HeldMutation first = adapter.HoldNext("configure");
        try
        {
            Task earlier = model.ConfigureItemAsync(model.Items[0], Settings(75));
            CancellationToken earlierToken = await first.Entered.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            first.Release();
            await earlier.ConfigureAwait(false);
            HeldMutation current = adapter.HoldNext("configure");
            try
            {
                Assert.That(current.Entered.IsCompleted, Is.False);
                Assert.That(current.Cancelled.IsCompleted, Is.False);
                Task mutation = model.ConfigureItemAsync(model.Items[0], Settings(25));
                CancellationToken currentToken = await current.Entered.WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(currentToken, Is.Not.EqualTo(earlierToken));
                Task detaching = model.DetachAdapterAsync().AsTask();

                await current.Cancelled.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(currentToken.IsCancellationRequested, Is.True);
                Assert.That(first.Cancelled.IsCompleted, Is.False);
                Assert.That(current.IsRunning, Is.True);
                Assert.That(detaching.IsCompleted, Is.False);
                Assert.That(adapter.DisposeCount, Is.Zero);
                current.Release();
                await Assert.ThatAsync(() => mutation, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                await detaching.ConfigureAwait(false);

                Assert.That(adapter.DisposeCount, Is.EqualTo(1));
                Assert.That(adapter.DisposedWhileRunning, Is.False);
                Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(25)));
            }
            finally
            {
                current.Release();
            }
        }
        finally
        {
            first.Release();
        }
    }

    [Test]
    public async Task ReplacementCancelsAndDrainsAnInProgressAttachmentWithoutLosingOfflineEdits()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        var old = new DeferredAdapter();
        HeldMutation held = old.HoldNext("publishing");
        try
        {
            Task attaching = model.AttachAdapterAsync(old.Adapter);
            CancellationToken token = await held.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var next = new DeferredAdapter();
            Task replacement = model.AttachAdapterAsync(next.Adapter);
            await held.Cancelled.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(held.IsRunning, Is.True);
            Assert.That(old.DisposeCount, Is.Zero);
            Assert.That(replacement.IsCompleted, Is.False);
            await model.AddItemAsync(Item("B")).ConfigureAwait(false);
            held.Release();
            await Assert.ThatAsync(() => attaching, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await replacement.ConfigureAwait(false);

            Assert.That(model.Items.Select(item => item.DisplayName), Is.EqualTo(s_expectedRestoredItems));
            Assert.That(next.Items.Values.OrderBy(item => item.Id), Is.EqualTo(model.Items));
            Assert.That(old.DisposeCount, Is.EqualTo(1));
            Assert.That(old.DisposedWhileRunning, Is.False);
        }
        finally
        {
            held.Release();
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task QueuedMutationsAreCancelledWithoutCallingTheOldAdapter(bool awaitCancellation)
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        var old = new DeferredAdapter();
        await model.AttachAdapterAsync(old.Adapter).ConfigureAwait(false);
        HeldMutation held = old.HoldNext("configure");
        try
        {
            Task configure = model.ConfigureItemAsync(model.Items[0], Settings(50));
            CancellationToken token = await held.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task add = model.AddItemAsync(Item("B"));
            Task remove = model.RemoveItemAsync(model.Items[0]);
            var next = new DeferredAdapter();
            Task replace = model.AttachAdapterAsync(next.Adapter);
            if (awaitCancellation)
            {
                await held.Cancelled.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(token.IsCancellationRequested, Is.True);
            }
            Assert.That(held.IsRunning, Is.True);
            Assert.That(replace.IsCompleted, Is.False);
            Assert.That(old.DisposeCount, Is.Zero);
            held.Release();
            await Assert.ThatAsync(() => configure, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await Task.WhenAll(add, remove, replace).ConfigureAwait(false);

            Assert.That(old.AddCount, Is.EqualTo(1), "The queued B add must not reach the old generation.");
            Assert.That(old.RemoveCount, Is.Zero);
            Assert.That(old.DisposeCount, Is.EqualTo(1));
            Assert.That(old.DisposedWhileRunning, Is.False);
            Assert.That(model.Items.Single().DisplayName, Is.EqualTo("B"));
            Assert.That(next.Items.Values.Single().DisplayName, Is.EqualTo("B"));
        }
        finally
        {
            held.Release();
        }
    }

    [Test]
    public async Task EditsAcceptedDuringAttachmentAreNotReplacedByItsInitialSnapshot()
    {
        var model = CreateModel();
        await using var lifetime = model.ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        await model.AddItemAsync(Item("B")).ConfigureAwait(false);
        var adapter = new DeferredAdapter();
        HeldMutation held = adapter.HoldNext("publishing");
        try
        {
            Task attach = model.AttachAdapterAsync(adapter.Adapter);
            await held.Entered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task remove = model.RemoveItemAsync(model.Items[0]);
            Task configure = model.ConfigureItemAsync(model.Items[0], Settings(25));
            Task add = model.AddItemAsync(Item("C"));
            held.Release();
            await Task.WhenAll(attach, remove, configure, add).ConfigureAwait(false);

            Assert.That(model.Items.Select(item => item.DisplayName), Is.EqualTo(s_expectedSurvivors));
            Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(25)));
            Assert.That(adapter.Items.Values.OrderBy(item => item.Id), Is.EqualTo(model.Items));
        }
        finally
        {
            held.Release();
        }
    }

    private static SubscriptionViewModel CreateModel()
    {
        return new SubscriptionViewModel("Monitor", null, NullLogger.Instance, InlineWorkspaceDispatcher.Instance);
    }

    private static MonitoredItemConfig Item(string name)
    {
        return new MonitoredItemConfig { DisplayName = name, NodeId = new NodeId(name, 0) };
    }

    private static MonitoredItemSettings Settings(int sampling)
    {
        return new MonitoredItemSettings { SamplingInterval = TimeSpan.FromMilliseconds(sampling) };
    }

    private sealed class DeferredAdapter
    {
        public DeferredAdapter()
        {
            m_mock.SetupGet(value => value.Events).Returns(m_events.Reader);
            m_mock.SetupGet(value => value.Items).Returns(() => new ArrayOf<MonitoredItemConfig>(
                Items.Values.OrderBy(item => item.Id).ToArray()));
            m_mock.SetupGet(value => value.Counters).Returns(new SubscriptionCounters());
            m_mock.Setup(value => value.ApplySubscriptionAsync(
                It.IsAny<SubscriptionConfig>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionConfig _, CancellationToken token) => WaitAsync("publishing", token));
            m_mock.Setup(value => value.AddItemAsync(It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
                .Returns(async (MonitoredItemConfig item, CancellationToken token) =>
                {
                    AddCount++;
                    await WaitAsync("add", token).ConfigureAwait(false);
                    Items[item.Id] = item;
                    return LateHandle ?? item.Id;
                });
            m_mock.Setup(value => value.RemoveItemAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (int id, CancellationToken token) =>
                {
                    RemoveCount++;
                    await WaitAsync("remove", token).ConfigureAwait(false);
                    Items.Remove(id);
                });
            m_mock.Setup(value => value.ConfigureItemAsync(
                It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
                .Returns(async (MonitoredItemConfig item, CancellationToken token) =>
                {
                    ConfiguredIds.Add(item.Id);
                    await WaitAsync("configure", token).ConfigureAwait(false);
                    Items[item.Id] = item;
                });
            m_mock.Setup(value => value.SetMonitoringModeAsync(
                It.IsAny<int>(), It.IsAny<MonitoringMode>(), It.IsAny<CancellationToken>()))
                .Returns(async (int id, MonitoringMode mode, CancellationToken token) =>
                {
                    await WaitAsync("mode", token).ConfigureAwait(false);
                    Items[id] = Items[id] with { MonitoringMode = mode };
                });
            m_mock.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                DisposedWhileRunning |= Volatile.Read(ref m_currentMutation)?.IsRunning == true;
                DisposeCount++;
                m_events.Writer.TryComplete();
                return ValueTask.CompletedTask;
            });
        }

        public ISubscriptionAdapter Adapter => m_mock.Object;
        public Dictionary<int, MonitoredItemConfig> Items { get; } = [];
        public List<int> ConfiguredIds { get; } = [];
        public int? LateHandle { get; set; }
        public int DisposeCount { get; private set; }
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool DisposedWhileRunning { get; private set; }

        public HeldMutation HoldNext(string operation)
        {
            var held = new HeldMutation(operation);
            if (Interlocked.CompareExchange(ref m_pendingMutation, held, null) is not null)
            {
                throw new InvalidOperationException("Another mutation is already armed.");
            }
            return held;
        }

        private Task WaitAsync(string operation, CancellationToken token)
        {
            if (Volatile.Read(ref m_currentMutation)?.IsRunning == true)
            {
                throw new InvalidOperationException("The adapter was called before its held mutation completed.");
            }
            HeldMutation? held = Volatile.Read(ref m_pendingMutation);
            if (held is null || operation != held.Operation)
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
            if (Interlocked.CompareExchange(ref m_pendingMutation, null, held) != held)
            {
                throw new InvalidOperationException("The held mutation was entered concurrently.");
            }
            Volatile.Write(ref m_currentMutation, held);
            return held.WaitAsync(token);
        }

        private readonly Mock<ISubscriptionAdapter> m_mock = new();
        private readonly Channel<NotificationEvent> m_events = Channel.CreateUnbounded<NotificationEvent>();
        private HeldMutation? m_pendingMutation;
        private HeldMutation? m_currentMutation;
    }

    private sealed class HeldMutation(string operation)
    {
        public string Operation { get; } = operation;
        public Task<CancellationToken> Entered => m_entered.Task;
        public Task Cancelled => m_cancelled.Task;
        public bool IsRunning => Volatile.Read(ref m_running);

        public void Release()
        {
            m_release.TrySetResult();
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => m_cancelled.TrySetResult());
            Volatile.Write(ref m_running, true);
            m_entered.SetResult(cancellationToken);
            try
            {
                // Deliberately finish late despite cancellation to exercise the document's drain boundary.
                await m_release.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref m_running, false);
            }
        }

        private readonly TaskCompletionSource<CancellationToken> m_entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool m_running;
    }

    private static readonly string[] s_expectedSurvivors = ["B", "C"];
    private static readonly string[] s_expectedRestoredItems = ["A", "B"];
}
