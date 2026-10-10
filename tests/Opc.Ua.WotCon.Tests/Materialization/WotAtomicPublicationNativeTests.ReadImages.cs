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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeReadImagePreparationRejectsNullSnapshotsBeforeAndAfterInitialization(bool initialized)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotRegistryNodeManager owner = NativeRegistry();
            var projection = (IWotRegistryReadImageProjection)owner;
            WotRegistrySnapshot snapshot = m_registry.Current;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            var images = (INodeManagerReadImageSource)m_server.CurrentInstance.NodeManager;
            if (initialized)
            {
                Assert.That(projection.PrepareReadImage(snapshot, snapshot).Owner, Is.SameAs(owner));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => projection.PrepareReadImage(null!, snapshot),
                    Throws.TypeOf<ArgumentNullException>()
                        .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("previousSnapshot"));
                Assert.That(() => projection.PrepareReadImage(snapshot, null!),
                    Throws.TypeOf<ArgumentNullException>()
                        .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("intendedSnapshot"));
            }

            Assert.That(m_registry.Current, Is.SameAs(snapshot));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
            Assert.That(images.GetReadImage(owner), Is.Null);
            Assert.That(projection.PrepareReadImage(snapshot, snapshot).Owner, Is.SameAs(owner));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task NativeReadRetainsResourceMetadataAfterNewMetadataHasReconciled(
            bool withView, bool firstPublication)
        {
            var gate = new WotProjectionViewCanonicalLiveTests.BlockingSourceFactory();
            await m_server.NodeManagerLifecycle.AddAsync(gate, callerContext: null).ConfigureAwait(false);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            if (withView)
            {
                await AddStockViewAsync("child", false).ConfigureAwait(false);
            }
            if (!firstPublication)
            {
                await m_coordinator.RefreshAsync(HandoffRequest("retained-read-initial")).ConfigureAwait(false);
            }
            WotResource previous = m_registry.Current.FindResourceByXid(source.Xid)!;
            if (!firstPublication)
            {
                await UpsertStockSourceAsync(true).ConfigureAwait(false);
            }
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kStockSourceNamespace);
            ArrayOf<ReadValueId> nodes =
            [
                new() { NodeId = gate.Created.Identity("First"), AttributeId = Attributes.Value },
                .. await NativeResourceReadNodesAsync(source).ConfigureAwait(false)
            ];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            gate.Created.BlockNextValidation();
            Task<ReadResponse> pending = m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodes, timeout.Token).AsTask();
            Task<WotRefreshResult>? updating = null;
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WotRegistryChangedEventArgs> changed = (_, _) =>
            {
                if (m_registry.Current.RefreshGeneration == (firstPublication ? 1u : 2u))
                {
                    published.TrySetResult(true);
                }
            };
            m_registry.Changed += changed;
            try
            {
                await Task.WhenAny(gate.Created.Entered, pending).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(gate.Created.Entered.IsCompleted, Is.True);
                Assert.That(pending.IsCompleted, Is.False);
                updating = m_coordinator.RefreshAsync(
                    HandoffRequest("retained-read-update", firstPublication ? 0u : 1u)).AsTask();
                await Task.WhenAny(published.Task, updating).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!published.Task.IsCompleted)
                {
                    await updating.ConfigureAwait(false);
                    Assert.Fail("The registry did not publish the requested generation.");
                }
                await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
                Assert.That(NativeRegistry().FindPredefinedNode<WoTDocumentState>(ResourceId(source))!
                    .ActiveVersionId!.Value, Is.EqualTo(firstPublication ? "v1" : "v2"),
                    "The mutable NodeState must already contain the new image.");
                Assert.That(pending.IsCompleted, Is.False);
                ReadResponse current = await m_session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, timeout.Token).ConfigureAwait(false);
                AssertNativeResourceImage(
                    current, m_registry.Current.FindResourceByXid(source.Xid)!, firstPublication ? 42 : 84, 1);
            }
            finally
            {
                m_registry.Changed -= changed;
                gate.Created.Resume();
                if (updating is not null)
                {
                    await updating.ConfigureAwait(false);
                }
            }
            ReadResponse retained = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertNativeResourceImage(retained, previous, firstPublication ? null : 42, 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeReadPublishesRetiredMetadataWithoutAReplacementSource(bool withView)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource? child = withView ? await AddStockViewAsync("child", false).ConfigureAwait(false) : null;
            await m_coordinator.RefreshAsync(HandoffRequest("read-retire-initial")).ConfigureAwait(false);
            ArrayOf<ReadValueId> nodes = await NativeResourceReadNodesAsync(source).ConfigureAwait(false);
            if (child is not null)
            {
                await StageDesiredDisableAsync(child).ConfigureAwait(false);
            }
            await StageDesiredDisableAsync(source).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("read-retire", 1))
                .ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(2u));
            WotResource retired = m_registry.Current.FindResourceByXid(source.Xid)!;
            Assert.That(retired.ActiveVersionId, Is.Null);
            Assert.That(retired.RootNodeId.IsNull, Is.True);
            Assert.That(retired.MaterializedNodeCount, Is.Zero);
            ReadResponse read = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None).ConfigureAwait(false);
            AssertNativeResourceImage(read, retired, null);
        }

        [Test]
        public async Task NativeReadRemainsCoherentWhenAnEarlierRecoveryProjectionFails()
        {
            var failed = new Mock<IWotRegistryRecoveryProjection>(MockBehavior.Strict);
            failed.Setup(value => value.SynchronizeAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                .Returns(() => throw new IOException("The earlier native projection cannot acknowledge recovery."));
            using IDisposable registration = m_registry.RegisterRecoveryProjection(failed.Object);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("read-failure-initial")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            ArrayOf<ReadValueId> nodes = await NativeResourceReadNodesAsync(source).ConfigureAwait(false);
            m_indeterminateDecision = true;
            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("read-failure-unknown", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
            string directory = Path.Combine(m_root, "registry");
            File.Move(Directory.GetFiles(directory, "manifest.json.tmp-*").Single(),
                Path.Combine(directory, "manifest.json"));
            m_indeterminateDecision = false;

            await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync().ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>()).ConfigureAwait(false);

            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotResource recovered = m_registry.Current.FindResourceByXid(source.Xid)!;
            Assert.That(NativeRegistry().FindPredefinedNode<WoTDocumentState>(ResourceId(source))!
                .ActiveVersionId!.Value, Is.EqualTo("v1"), "The failed earlier participant prevented reconciliation.");
            ReadResponse read = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None).ConfigureAwait(false);
            AssertNativeResourceImage(read, recovered, 84);
            await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                source.GroupId, source.ResourceId, false).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            registration.Dispose();
            Assert.That(await m_coordinator.RecoverAsync().ConfigureAwait(false), Is.True);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            ReadResponse retried = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None).ConfigureAwait(false);
            AssertNativeResourceImage(retried, recovered, 84);
            failed.Verify(value => value.SynchronizeAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task NativeMetadataOnlyPublicationRetainsOwnersAndTheOldReadImage()
        {
            var gate = new WotProjectionViewCanonicalLiveTests.BlockingSourceFactory();
            await m_server.NodeManagerLifecycle.AddAsync(gate, callerContext: null).ConfigureAwait(false);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("read-observation-initial")).ConfigureAwait(false);
            WotResource previous = m_registry.Current.FindResourceByXid(source.Xid)!;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            var images = (INodeManagerReadImageSource)m_server.CurrentInstance.NodeManager;
            INodeManagerReadImage? previousImage = images.GetReadImage(NativeRegistry());
            Assert.That(previousImage, Is.Not.Null);
            ArrayOf<ReadValueId> nodes =
            [
                new() { NodeId = gate.Created.Identity("First"), AttributeId = Attributes.Value },
                .. await NativeResourceReadNodesAsync(source).ConfigureAwait(false)
            ];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            gate.Created.BlockNextValidation();
            Task<ReadResponse> pending = m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodes, timeout.Token).AsTask();
            try
            {
                await Task.WhenAny(gate.Created.Entered, pending).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(gate.Created.Entered.IsCompleted, Is.True);
                WotRefreshResult unchanged = await m_coordinator.RefreshAsync(HandoffRequest("read-observation", 1))
                    .ConfigureAwait(false);
                await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
                Assert.That(unchanged.NewGeneration, Is.EqualTo(1u));
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
                Assert.That(images.GetReadImage(NativeRegistry()), Is.Not.SameAs(previousImage));
                Assert.That(pending.IsCompleted, Is.False);
                ReadResponse current = await m_session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, timeout.Token).ConfigureAwait(false);
                AssertNativeResourceImage(current, m_registry.Current.FindResourceByXid(source.Xid)!, 42, 1);
            }
            finally
            {
                gate.Created.Resume();
            }
            ReadResponse retained = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertNativeResourceImage(retained, previous, 42, 1);
        }

        [Test]
        public async Task NativePublicationRejectsAHostThatCannotBindReadImagesBeforeDecision()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("read-provider-initial")).ConfigureAwait(false);
            WotResource previous = m_registry.Current.FindResourceByXid(source.Xid)!;
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            WotRegistrySnapshot pending = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            int decisions = probe.DecisionCount;
            probe.RejectReadImages = true;

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("read-provider-reject", 1)).ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);

            Assert.That(probe.DecisionCount, Is.EqualTo(decisions));
            Assert.That(m_registry.Current, Is.SameAs(pending));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                await NativeResourceReadNodesAsync(source).ConfigureAwait(false), CancellationToken.None)
                .ConfigureAwait(false);
            AssertNativeResourceImage(read, previous, 42);
        }

        private WotRegistryNodeManager NativeRegistry()
        {
            return m_server.NodeManagerLifecycle.Registrations.ToList()
                .Select(registration => registration.NodeManager).OfType<WotRegistryNodeManager>()
                .Single(manager => ReferenceEquals(manager.Registry, m_registry));
        }

        private async Task<ArrayOf<ReadValueId>> NativeResourceReadNodesAsync(WotResource resource)
        {
            var properties = (await BrowseStockAsync(ResourceId(resource), Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).ToDictionary(reference => reference.BrowseName.Name ??
                    throw new InvalidOperationException("A Resource Property has no BrowseName."),
                    reference => ExpandedNodeId.ToNodeId(reference.NodeId, m_server.CurrentInstance.NamespaceUris));
            return
            [
                new() { NodeId = StockNode("Source/Reading"), AttributeId = Attributes.Value },
                new() { NodeId = properties["ActiveVersionId"], AttributeId = Attributes.Value },
                new() { NodeId = properties["RefreshGeneration"], AttributeId = Attributes.Value },
                new() { NodeId = properties["MaterializedNodeCount"], AttributeId = Attributes.Value },
                new() { NodeId = properties["RootNodeId"], AttributeId = Attributes.Value },
                new() { NodeId = properties["LoadState"], AttributeId = Attributes.Value },
                new() { NodeId = properties["LastRefreshTime"], AttributeId = Attributes.Value }
            ];
        }

        private static void AssertNativeResourceImage(
            ReadResponse read, WotResource expected, int? sourceValue, int offset = 0)
        {
            Assert.That(read.Results.Count, Is.EqualTo(7 + offset));
            if (sourceValue is { } reading)
            {
                Assert.That(read.Results[offset].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[offset].WrappedValue, Is.EqualTo(Variant.From(reading)));
            }
            else
            {
                Assert.That(read.Results[offset].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            Assert.That(read.Results.ToList().Skip(offset + 1).All(value => value.StatusCode == StatusCodes.Good),
                Is.True);
            Assert.That(read.Results[offset + 1].WrappedValue,
                Is.EqualTo(Variant.From(expected.ActiveVersionId ?? string.Empty)));
            Assert.That(read.Results[offset + 2].WrappedValue, Is.EqualTo(Variant.From(expected.RefreshGeneration)));
            Assert.That(read.Results[offset + 3].WrappedValue,
                Is.EqualTo(Variant.From((uint)expected.MaterializedNodeCount)));
            Assert.That(read.Results[offset + 4].WrappedValue, Is.EqualTo(Variant.From(expected.RootNodeId)));
            Assert.That(read.Results[offset + 5].WrappedValue, Is.EqualTo(Variant.From((int)expected.LoadState)));
            Assert.That(read.Results[offset + 6].WrappedValue,
                Is.EqualTo(Variant.From((DateTimeUtc)expected.LastRefreshTime)));
        }
    }
}
