/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests generation-bound subscription ownership and that every detach state transition keeps
    /// <see cref="MonitoredItem.HasRecoverableItems"/> exact. Recovery of detached items is
    /// skipped while no item is counted, so a missed increment would suppress reattachment
    /// and a missed decrement would bring back the scans of every monitored item.
    /// </summary>
    /// <remarks>
    /// The count is process-wide and other tests run in parallel, so the tests assert the
    /// per-item flag, and only that the count is positive while an item is counted.
    /// </remarks>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MonitoredItemRecoverableCountTests
    {
        [Test]
        public void NewItemIsNotCounted()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));

            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        /// <summary>
        /// Generation membership begins at subscription ownership, not item construction.
        /// </summary>
        [Test]
        public void SubscriptionClaimTracksTheExactGenerationAndOwner()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            var owner = new Mock<ISubscription>().Object;
            var binding = (IMonitoredItemGenerationBinding)item;

            Assert.That(generation.MonitoredItems, Is.Empty);
            item.SubscriptionCallback = owner;
            Assert.That(generation.MonitoredItems, Is.Empty);
            binding.BindSubscription(owner);
            binding.BindSubscription(owner);

            Assert.That(generation.MonitoredItems, Has.Count.EqualTo(1));
            Assert.That(generation.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            binding.UnbindSubscription(new Mock<ISubscription>().Object);
            Assert.That(generation.MonitoredItems.ContainsKey(item), Is.True);
        }

        /// <summary>
        /// A handoff moves the subscription claim without changing item identity.
        /// </summary>
        [Test]
        public void RebindTransfersSubscriptionMembershipToTheReplacementGeneration()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            uint id = item.Id;

            ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Has.Count.EqualTo(1));
            Assert.That(replacement.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            Assert.That(item.Generation!.Identifies(replacement), Is.True);
            Assert.That(item.Id, Is.EqualTo(id));
        }

        /// <summary>
        /// Neither ordering of deletion and handoff can restore a removed subscription claim.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SubscriptionReleaseAndRebindCannotResurrectMembership(bool releaseFirst)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            if (releaseFirst)
            {
                binding.UnbindSubscription(owner);
            }

            ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());
            binding.UnbindSubscription(owner);

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Is.Empty);
            Assert.That(item.SubscriptionCallback, Is.SameAs(owner),
                "Manager cleanup still needs the callback after subscription ownership ends.");
        }

        /// <summary>
        /// Concurrent handoff and subscription removal leave neither generation retaining the item.
        /// </summary>
        [Test]
        public async Task SubscriptionReleaseRacingRebindLeavesNoGenerationMembershipAsync()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);

            await Task.WhenAll(
                Task.Run(() => ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object())),
                Task.Run(() => binding.UnbindSubscription(owner))).ConfigureAwait(false);

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Is.Empty);
        }

        /// <summary>
        /// Disposal releases the generation claim and refuses later attempts to reclaim the item.
        /// </summary>
        [Test]
        public void DisposedItemCannotReenterGenerationMembership()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);

            item.Dispose();

            Assert.That(generation.MonitoredItems, Is.Empty);
            Assert.That(() => binding.BindSubscription(owner), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(generation.MonitoredItems, Is.Empty);
        }

        /// <summary>
        /// An item detached from its node does not count as an active source binding.
        /// </summary>
        [Test]
        public void DetachedSubscriptionItemDoesNotBlockItsOriginalGeneration()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            using MonitoredItem item = CreateItem(server);
            IAsyncNodeManager manager = item.NodeManager;
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.True);

            ((IDetachableMonitoredItem)item).BeginDetach();

            Assert.That(generation.MonitoredItems.ContainsKey(item), Is.True);
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.False);
            ((IDetachableMonitoredItem)item).Rebind(manager, new object());
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.True);
        }

        /// <summary>
        /// A lost item belongs to its stable slot until a compatible generation adopts it.
        /// </summary>
        [Test]
        public void LostItemRemainsOnItsSlotUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            server.Setup(value => value.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
            using MonitoredItem item = CreateItem(server);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.Detach(server.Object);
            lifecycle.MarkNodeDeleted();
            lifecycle.MarkNodeDeleted();

            Assert.That(original.Slot.UnresolvedItems, Has.Count.EqualTo(1));
            Assert.That(original.Slot.UnresolvedItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager).Slot.UnresolvedItems, Is.Empty,
                "A parking manager must never become the stable owner of the lost item.");

            lifecycle.Rebind(replacement.NodeManager, new object());

            Assert.That(original.Slot.UnresolvedItems, Is.Empty);
            Assert.That(replacement.MonitoredItems.ContainsKey(item), Is.True);
        }

        /// <summary>
        /// Subscription removal releases unresolved ownership as well as generation membership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LostItemReleasesSlotOwnershipWhenDeleted(bool dispose)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerSlot slot = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager).Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            ((IDetachableMonitoredItem)item).MarkNodeDeleted();
            Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);

            if (dispose)
            {
                item.Dispose();
            }
            else
            {
                binding.UnbindSubscription(owner);
            }

            Assert.That(slot.UnresolvedItems, Is.Empty);
        }

        /// <summary>
        /// Terminal invalidation removes even an existing recovery claim and permanently refuses adoption.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TerminalInvalidationRejectsRebindingAndRemovesRecoveryClaim(bool alreadyLost)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerSlot slot = NodeManagerGenerationRegistry.GetOrCreate(item.NodeManager).Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            if (alreadyLost)
            {
                binding.Invalidate(terminal: false);
                Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);
            }

            binding.Invalidate(terminal: true);
            binding.Invalidate(terminal: false);

            Assert.That(binding.IsTerminal, Is.True);
            Assert.That(((IDetachableMonitoredItem)item).IsDetached, Is.True);
            Assert.That(((IDetachableMonitoredItem)item).IsDeleted, Is.True);
            Assert.That(slot.UnresolvedItems, Is.Empty);
            Assert.That(
                () => ((IDetachableMonitoredItem)item).Rebind(new Mock<IAsyncNodeManager>().Object, new object()),
                Throws.InvalidOperationException);
            IStoredMonitoredItem stored = item.ToStorableMonitoredItem();
            Assert.That(((IStoredMonitoredItemInvalidation)stored).IsTerminal, Is.True);
            using var restored = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                item.NodeManager,
                new object(),
                stored,
                null);
            restored.SubscriptionCallback = owner;
            var restoredBinding = (IMonitoredItemGenerationBinding)restored;
            restoredBinding.BindSubscription(owner);
            Assert.That(restoredBinding.IsTerminal, Is.True);
            Assert.That(slot.UnresolvedItems, Is.Empty);
            Assert.That(((IDetachableMonitoredItem)restored).TryBeginAttach(), Is.False);
            Assert.That(
                () => ((IDetachableMonitoredItem)restored).Rebind(item.NodeManager, new object()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Existing persistence implementations restore without having to implement the new optional capability.
        /// </summary>
        [Test]
        public void LegacyStoredItemRestoresWithoutTerminalInvalidation()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            var stored = new TestStoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0)),
                IsDeleted = true,
                IsDetached = true
            };
            Assert.That(stored, Is.Not.InstanceOf<IStoredMonitoredItemInvalidation>());
            using var item = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored,
                null);

            Assert.That(((IMonitoredItemGenerationBinding)item).IsTerminal, Is.False);
            var lifecycle = (IDetachableMonitoredItem)item;
            Assert.That(lifecycle.TryBeginAttach(), Is.True);
            Assert.That(lifecycle.EndAttach(), Is.True);
        }

        /// <summary>
        /// Repeated reconciliation reports one lost-node notification without changing the client handle.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedInvalidationPublishesOneBadNodeIdUnknown(bool terminal)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.Invalidate(terminal);
            binding.Invalidate(terminal);
            using var context = new OperationContext(item);
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool more = item.Publish(context, notifications, diagnostics, 10, telemetry.CreateLogger<MonitoredItem>());

            Assert.That(more, Is.False);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Peek().ClientHandle, Is.EqualTo(3));
            Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            binding.Invalidate(terminal);
            notifications.Clear();
            diagnostics.Clear();
            more = item.Publish(context, notifications, diagnostics, 10, telemetry.CreateLogger<MonitoredItem>());
            Assert.That(more, Is.False);
            Assert.That(notifications, Is.Empty);
        }

        [Test]
        public void DeletedItemIsCountedUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.True);
            Assert.That(MonitoredItem.HasRecoverableItems, Is.True);

            // a second transition into the same state does not count the item twice.
            lifecycle.QueueNodeIdUnknown();
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [Test]
        public void DetachedItemIsCountedUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            server.Setup(s => s.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
            using MonitoredItem item = CreateItem(server);
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.BeginDetach();
            Assert.That(item.IsCountedAsRecoverable, Is.True);
            Assert.That(MonitoredItem.HasRecoverableItems, Is.True);

            lifecycle.Detach(server.Object);
            Assert.That(lifecycle.IsDetached, Is.True);
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(lifecycle.IsDetached, Is.False);
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [Test]
        public void DisposedItemIsNotCounted()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            item.Dispose();
            Assert.That(item.IsCountedAsRecoverable, Is.False);

            // a disposed item cannot be attached again, so later transitions do not count it.
            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void RestoredItemIsCountedWhenStoredDetachedOrDeleted(bool isDetached, bool isDeleted)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            var stored = new StoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0)),
                IsDetached = isDetached,
                IsDeleted = isDeleted
            };

            using var item = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored,
                null);

            Assert.That(item.IsCountedAsRecoverable, Is.EqualTo(isDetached || isDeleted));
        }

        private static MonitoredItemQueueFactory CreateQueueFactory(out ITelemetryContext telemetry)
        {
            telemetry = NUnitTelemetryContext.Create();
            return new MonitoredItemQueueFactory(telemetry);
        }

        private static Mock<IServerInternal> CreateServer(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queueFactory)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            server.Setup(s => s.SubscriptionStore).Returns(new Mock<ISubscriptionStore>().Object);
            server.Setup(s => s.DiagnosticsNodeManager).Returns(new Mock<IDiagnosticsNodeManager>().Object);
            return server;
        }

        private static MonitoredItem CreateItem(Mock<IServerInternal> server)
        {
            return new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                null!,
                1,
                2,
                new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.Value },
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                null,
                new MonitoringFilter(),
                null,
                1000,
                10,
                true,
                1000);
        }
    }
}
