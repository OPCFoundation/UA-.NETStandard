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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task OrdinarySourceCorrelationDoesNotRequireAViewProvider(
            bool thingModel, bool preparedViews)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = ObserveHandoff(
                preparedViews ? views : null, new StockViewSourceConverter());
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false, thingModel).ConfigureAwait(false);

            WotRefreshResult initial = await m_coordinator.RefreshAsync(HandoffRequest("ordinary-correlation"))
                .ConfigureAwait(false);

            Assert.That(initial.Summary.Failed, Is.Zero);
            Assert.That(initial.NewGeneration, Is.EqualTo(1u));
            await AssertOrdinaryCorrelationAsync(source).ConfigureAwait(false);
            Assert.That(m_coordinator.CommittedPublication.Views.IsEmpty, Is.True);
            Assert.That(m_registry.Current.CanonicalViewGraphState.IsNull, Is.True);
            await UpsertStockSourceAsync(true, thingModel, numeric: true).ConfigureAwait(false);
            m_failDecision = true;

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("rejected-correlation-replacement", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            await AssertOrdinaryCorrelationAsync(source).ConfigureAwait(false);
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            m_failDecision = false;

            WotRefreshResult replacement = await m_coordinator.RefreshAsync(
                HandoffRequest("replaced-correlation", 1)).ConfigureAwait(false);

            Assert.That(replacement.NewGeneration, Is.EqualTo(2u));
            await AssertOrdinaryCorrelationAsync(source, numeric: true).ConfigureAwait(false);
            var replacementRoot = new NodeId(100u, StockNode("Source").NamespaceIndex);
            DataValue reading = await m_session.ReadValueAsync(
                new NodeId(101u, replacementRoot.NamespaceIndex)).ConfigureAwait(false);
            Assert.That(reading.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reading.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(84));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.EqualTo(1));
            await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);

            WotRefreshResult retired = await m_coordinator.RefreshAsync(
                HandoffRequest("retired-correlation", 3)).ConfigureAwait(false);

            Assert.That(retired.NewGeneration, Is.EqualTo(3u));
            Assert.That(await BrowseStockAsync(ResourceId(source), HasProjectionId()).ConfigureAwait(false),
                Is.Empty);
            Assert.That((await ReadNodeClassAsync(replacementRoot).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        private async Task AssertOrdinaryCorrelationAsync(WotResource source, bool numeric = false)
        {
            NodeId referenceType = HasProjectionId();
            NodeId resourceId = ResourceId(source);
            NodeId root = numeric ? new NodeId(100u, StockNode("Source").NamespaceIndex) : StockNode("Source");
            Assert.That((await BrowseStockAsync(resourceId, referenceType).ConfigureAwait(false))
                .Select(reference => (reference.ReferenceTypeId, reference.IsForward, reference.NodeId)),
                Is.EqualTo(new[] { (referenceType, true, new ExpandedNodeId(root)) }));
            Assert.That((await BrowseStockAsync(root, referenceType, BrowseDirection.Inverse).ConfigureAwait(false))
                .Select(reference => (reference.ReferenceTypeId, reference.IsForward, reference.NodeId)),
                Is.EqualTo(new[] { (referenceType, false, new ExpandedNodeId(resourceId)) }));
        }
    }
}
