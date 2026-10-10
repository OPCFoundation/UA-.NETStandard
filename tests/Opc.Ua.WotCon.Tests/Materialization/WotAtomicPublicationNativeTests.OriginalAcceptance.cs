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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [TestCase(WoTAtomicityEnum.PerRegistry)]
        public async Task NewInvalidDependentKeepsItsPreviouslyActiveNativePrerequisite(WoTAtomicityEnum atomicity)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("original-active-prerequisite")).ConfigureAwait(false);
            WotResource active = m_registry.Current.FindResourceByXid(first.Xid)!;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotResource dependent = await AddAsync("new-dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, first.ResourceId).ConfigureAwait(false);
            m_converter.MarkInvalid(dependent.ResourceId);
            var request = new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                Selection = [UnitSelector(dependent)],
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
            };

            WotRefreshResult failed = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(failed.NewGeneration, Is.EqualTo(1u));
            Assert.That(failed.Summary.Atomicity,
                Is.EqualTo(atomicity == WoTAtomicityEnum.PerResource ? WoTAtomicityEnum.PerClosure : atomicity));
            Assert.That(failed.Summary.Total, Is.EqualTo(2u));
            Assert.That(failed.Summary.Failed, Is.EqualTo(2u));
            Assert.That(failed.Results.Single(row => row.Xid == dependent.Xid).Phase,
                Is.EqualTo(WoTPhaseEnum.FormatValidation));
            WotResource retained = m_registry.Current.FindResourceByXid(first.Xid)!;
            Assert.That(retained.ActiveVersionId, Is.EqualTo(active.ActiveVersionId));
            Assert.That(retained.RefreshGeneration, Is.EqualTo(active.RefreshGeneration));
            Assert.That(retained.RootNodeId, Is.EqualTo(active.RootNodeId));
            Assert.That(retained.MaterializedNodeCount, Is.EqualTo(active.MaterializedNodeCount));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            m_converter.ClearInvalid(dependent.ResourceId);

            WotRefreshResult committed = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(committed.NewGeneration, Is.EqualTo(2u));
            Assert.That(committed.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(committed.Summary.Failed, Is.Zero);
            Assert.That(m_registry.Current.FindResourceByXid(first.Xid)!.RefreshGeneration, Is.EqualTo(2u));
            Assert.That(m_registry.Current.FindResourceByXid(dependent.Xid)!.RefreshGeneration, Is.EqualTo(2u));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [TestCase(WoTAtomicityEnum.PerRegistry)]
        public async Task InvalidReplacementViewKeepsTheCommittedNativeSourceGraphAndToken(WoTAtomicityEnum atomicity)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("original-valid-view")).ConfigureAwait(false);
            WotCommittedPublicationState committed = m_coordinator.CommittedPublication;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            await AddStockViewAsync("child", false, collideWithSource: true).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            m_events.Clear();

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                Selection = [UnitSelector(child)],
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
            }).ConfigureAwait(false), Throws.TypeOf<ArgumentException>()
                .With.Message.EqualTo("A retained canonical Resource or View identity cannot be reassigned."))
                .ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(committed));
            Assert.That(m_registry.Current.CanonicalViewGraphState,
                Is.EqualTo(committed.RegistrySnapshot.CanonicalViewGraphState));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            Assert.That((await BrowseStockAsync(ResourceId(child), HasProjectionId()).ConfigureAwait(false))
                .Select(reference => reference.NodeId), Is.EqualTo(new[] { new ExpandedNodeId(StockView("child")) }));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.EqualTo(1));
            Assert.That(m_events.Any(change => change.Kind == WotMaterializationEventKind.Resource &&
                change.LoadState == WoTLoadStateEnum.Active), Is.False);
            await AddStockViewAsync("child", false).ConfigureAwait(false);

            WotRefreshResult retry = await m_coordinator.RefreshAsync(
                HandoffRequest("valid-view-after-rejection", 1, force: true))
                .ConfigureAwait(false);

            Assert.That(retry.Summary.Failed, Is.Zero);
            Assert.That(retry.NewGeneration, Is.EqualTo(2u));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
        }
    }
}
